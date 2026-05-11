# Operations Runbook

> Audience: production operator (you, future hires, on-call rotation).
> Purpose: daily ops + incident response for WMS in production.

This runbook complements the Phase 26 deployment docs. Read those
first if you're standing up a new environment:

- [Deployment configuration](../deployment/configuration.md) — env vars + connection strings + per-env config
- [IIS setup](../deployment/iis-setup.md) — site bindings, app pool, TLS, Application Initialization
- [Database migration](../deployment/migration.md) — `dotnet WMS.Migrate.dll` invocation patterns
- [Deployment checklist](../deployment/checklist.md) — pre/during/post deploy

---

## 1. Deployment

### Pre-deploy checklist

Walk through [docs/deployment/checklist.md](../deployment/checklist.md) — every item, every time. Highlights:

- [ ] All tests pass locally (`dotnet test` exits 0)
- [ ] Branch merged to `main` and tagged (`v2.x.y-feature`)
- [ ] Release notes / CHANGELOG entry covering schema + breaking changes
- [ ] Master DB + every active tenant DB backed up + a restore drill done
- [ ] Maintenance window announced via comms channel
- [ ] Env vars in place: `ASPNETCORE_ENVIRONMENT=Production`,
      `ConnectionStrings__MasterDb`, `ConnectionStrings__TenantTemplate`

### Deploy steps

1. **Stop IIS app pool** — `Stop-WebAppPool -Name "WMSApp"`
2. **Build artifacts** (from build agent or local):
   ```pwsh
   dotnet publish src/WMS.Web/WMS.Web.csproj -c Release -o D:\WMSApp\publish\web --no-self-contained
   dotnet publish tools/WMS.Migrate/WMS.Migrate.csproj -c Release -o D:\WMSApp\publish\tools --no-self-contained
   ```
3. **Copy publish output** to the server (`xcopy` / `robocopy` / Octopus / etc.)
4. **Apply Master migrations** — must finish before tenant migrations:
   ```pwsh
   cd D:\WMSApp\publish\tools
   dotnet WMS.Migrate.dll up master
   ```
   Expect: `Latest applied version: <timestamp>`. Exit code 0.
5. **Apply Tenant migrations** to every active tenant:
   ```pwsh
   dotnet WMS.Migrate.dll up tenants
   ```
   Output prints `--- Tenant: ACME (WMS_Tenant_ACME) ---` per tenant. Stops on first failure.
6. **Start IIS app pool** — `Start-WebAppPool -Name "WMSApp"`
7. **Smoke** — see Section 2 (Monitoring).

### Rollback

If post-deploy smoke fails:

- **Code-level rollback**: re-deploy the previous publish artifact (kept at `D:\WMSApp\publish.previous`).
  ```pwsh
  Stop-WebAppPool -Name "WMSApp"
  Remove-Item -Recurse -Force D:\WMSApp\publish\*
  Copy-Item -Recurse D:\WMSApp\publish.previous\* D:\WMSApp\publish\
  Start-WebAppPool -Name "WMSApp"
  ```
- **Schema rollback**: a single-step Master rollback:
  ```pwsh
  dotnet WMS.Migrate.dll down master
  ```
  Tenant rollback is per-tenant (no fan-out `down tenants`). For each tenant DB:
  ```pwsh
  $env:ConnectionStrings__TenantTemplate = "Server=...;Database=WMS_Tenant_ACME;..."
  dotnet WMS.Migrate.dll down tenant
  ```
- **Data-level rollback**: restore from backup. Document the restore drill — pair with TD-075 (DR runbook).

Keep at least the last 2 publish outputs (`publish` + `publish.previous`) on the server.

### Post-deploy smoke

- [ ] `curl https://wms.example.com/healthz/live` → 200 + `{"status":"Healthy"}`
- [ ] `curl https://wms.example.com/healthz/ready` → 200 + `master-db` entry Healthy
- [ ] Sign in as ADMIN at `/Auth/Login`
- [ ] Smoke test (10 minutes):
  - Dashboard loads
  - `/Reports/Inventory` renders with non-empty stats
  - `/Users` lists users
  - `/AuditLog` shows the deploy-time `LoginSuccess`
  - `/hangfire` accessible to ADMIN; 403 to non-ADMIN
- [ ] Serilog file logs at `D:\WMSApp\logs\wms-{yyyy-MM-dd}.log` — recent entries timestamped
- [ ] Hangfire dashboard shows recurring jobs (`pack-video-retention-cleanup`)
- [ ] PR closed, tag pushed, release notes published

---

## 2. Monitoring

### Health endpoints

| Endpoint | Purpose | Auth | Target SLA |
|---|---|---|---|
| `/healthz/live` | Process alive (no DB) | None | <100ms |
| `/healthz/ready` | Process + Master DB reachable (5s SELECT 1) | None | <2s |
| `/healthz` | Alias for `/healthz/ready` | None | <2s |
| `/health` | Backward-compat plain "Healthy" | None | <100ms |

Wire load balancer / monitoring probe at `/healthz/live` (k8s-style liveness) and `/healthz/ready` (readiness).

### Logs

- **Location**: `D:\WMSApp\logs\wms-{yyyy-MM-dd}.log` (configurable in `appsettings.Production.json:Serilog`)
- **Rotation**: daily, 30-day retention, 100MB file size cap
- **Format**: structured (key-value), Inter-readable in editor; tail with `Get-Content -Wait`
- **Levels in prod**: Information default; Microsoft / System filtered to Warning

What to grep for under stress:

| Symptom | Grep |
|---|---|
| Failed login spike | `LoginFailure` in same minute window |
| 5xx errors | `Level=Error` + status code 500/502/503 |
| DB connection failures | `SqlException` or `connection` near `Error` |
| Hangfire job failures | `Hangfire` + `Error` |
| Lockout cascade | `AccountLockout` events stacking |

### What to monitor (operationally)

| Metric | Threshold | Where |
|---|---|---|
| `/healthz/ready` 200 | 99.9% | Monitoring tool / Pingdom / UptimeRobot |
| Failed login rate | spike > 50/hour | Audit log + log file |
| 5xx error rate | > 5/minute | Log file + future App Insights (TD-068) |
| Disk space — logs | < 80% full | Windows perfmon |
| Disk space — tenant DBs | per SQL Server | DBA dashboard or `sys.master_files` |
| Hangfire job failures | any | `/hangfire` dashboard |
| Tenant audit log writes | not zero (idle = suspect) | `master.SystemAuditLog` |

Application Insights / Prometheus integration is TD-068; for v3.0.0 launch use Serilog file + manual checks + uptime probe.

---

## 3. Incident Response

### Database connection failures

**Symptom**: `/healthz/ready` returns 503; `master-db` entry `Unhealthy`. Login fails for everyone.

Triage:

1. Check Master DB connectivity from the IIS host:
   ```pwsh
   sqlcmd -S prod-sql.example.net -d WMS_Master -E -Q "SELECT 1"
   ```
2. If `sqlcmd` fails: SQL Server service status / network / firewall:
   ```pwsh
   Test-NetConnection -ComputerName prod-sql.example.net -Port 1433
   Get-Service -ComputerName prod-sql.example.net -Name "MSSQLSERVER"
   ```
3. If SQL Server is down: restart service; verify pre-incident backups have not been disturbed.
4. If tenant DBs are unreachable but Master is fine: the tenant DB itself may have crashed. Find the broken tenant:
   ```sql
   USE master;
   SELECT name, state_desc, recovery_model_desc
   FROM sys.databases
   WHERE name LIKE 'WMS_Tenant_%';
   ```
   `state_desc = ONLINE` is what you want. Anything else = recover the specific tenant.

### Tenant DB migration failure during deploy

**Symptom**: `dotnet WMS.Migrate.dll up tenants` halts on a specific tenant.

Output ends with:
```
--- Tenant: ACME (WMS_Tenant_ACME) ---
ERROR: <FluentMigrator error>
STOPPED on 'ACME' (exit 2). N succeeded, M remaining.
```

Triage:

1. **DO NOT proceed**. The coordinator stops on first failure to prevent half-migrated tenants.
2. Read the error in the WMS.Migrate stdout (preserved by Serilog or the launching shell).
3. Common causes:
   - Tenant DB lock — kill blocking SQL session: `sp_who2`, then `KILL <spid>`.
   - Schema drift — a prior migration was edited by hand; restore from backup or write a fix-up migration.
   - Out of disk — verify tenant DB file growth space.
4. Re-run when fixed:
   ```pwsh
   dotnet WMS.Migrate.dll up tenants
   ```
   FluentMigrator's `VersionInfo` per-DB makes re-runs idempotent — already-migrated tenants skip; the failed one re-applies; remaining tenants pick up.
5. If a tenant truly can't migrate: temporarily suspend in master:
   ```sql
   UPDATE master.Tenants SET Status = 'Suspended' WHERE Code = 'ACME';
   ```
   Re-run `up tenants` — the suspended tenant is skipped. Fix offline, reactivate, migrate, resume.

### Tenant admin lockout

**Symptom**: Customer's ADMIN user can't log in. `Account is locked` or `Invalid email or password.` 5+ times.

Triage:

1. Confirm via audit log:
   ```sql
   USE WMS_Tenant_ACME;
   SELECT TOP 20 * FROM security.AuditLog
   WHERE EventType IN ('LoginFailure', 'AccountLockout')
   ORDER BY CreatedAt DESC;
   ```
2. Decide: real attack? operator typo? compromised password?

3. **If operator typo**: unlock + reset failed counter:
   ```sql
   UPDATE security.Users
   SET LockedUntil = NULL, FailedLoginAttempts = 0
   WHERE Email = 'admin@acme.com';
   ```
4. **If compromised password**: force-reset via /SuperAdmin/Tenants/{id} → Reset admin password (Phase 27 surface). Capture the new temp password + send via secure channel.
5. **If real attack**: rate-limit per-IP is already in place (5/min/IP); also suspend the tenant briefly until investigation completes.

### SuperAdmin lockout

**Symptom**: Can't log into `/SuperAdmin/Login`. Same patterns as tenant admin lockout but against `master.SuperAdmins`.

```sql
USE WMS_Master;
UPDATE master.SuperAdmins
SET LockedUntil = NULL, FailedLoginAttempts = 0
WHERE Email = 'superadmin@wms.local';
```

**If totally locked out** (no remaining SuperAdmin account):

1. Reset directly on the DB:
   ```sql
   USE WMS_Master;
   UPDATE master.SuperAdmins
   SET PasswordHash = '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/LewdBPj/IZmK5b8gG',  -- BCrypt for 'TempPass2026!' at cost 12
       LockedUntil = NULL,
       FailedLoginAttempts = 0,
       MustChangePassword = 1
   WHERE Email = 'superadmin@wms.local';
   ```
   Replace the BCrypt hash with one you generate from a fresh password (use the WMS.Web `IAuthService.HashPassword` route or `BCrypt.Net.BCrypt.HashPassword("YourTemp", 12)` in a one-off C# script).

2. Log in with the new temp, get redirected to `/SuperAdmin/ChangePassword`, rotate immediately.

### Hangfire job stuck

**Symptom**: Job stays in `Processing` state on `/hangfire` for > 10 minutes.

Triage:

1. Open `/hangfire/jobs/processing` — see which job, who's processing it, since when.
2. If clearly hung (no log activity in 5+ min): use the "Requeue" or "Delete" button on the job detail.
3. For the daily pack-video retention job specifically:
   - Inspect `D:\WMSApp\logs\wms-{today}.log` for `PackVideoRetentionCleanupJob` entries.
   - Common cause: disk-space failure when deleting a video file. Free space; retry.

### Failed login spike (suspected attack)

**Symptom**: `master.SystemAuditLog` and `security.AuditLog` show `LoginFailure` events at 100+/hour from a single IP or stretching across many users.

Response:

1. Identify the IP:
   ```sql
   USE WMS_Master;
   SELECT IpAddress, COUNT(*) AS Attempts, MIN(Timestamp), MAX(Timestamp)
   FROM master.SystemAuditLog
   WHERE EventType IN ('SuperAdminLoginFailure', 'LoginFailure')
     AND Timestamp >= DATEADD(HOUR, -1, SYSUTCDATETIME())
   GROUP BY IpAddress
   ORDER BY Attempts DESC;
   ```
   Cross-reference with `security.AuditLog` per tenant for tenant-side hits.

2. Block at the firewall / IIS Dynamic IP Restrictions for the offending IP range.

3. Force-rotate any account that authenticated successfully from that IP recently:
   ```sql
   USE master;
   SELECT TOP 50 * FROM master.SystemAuditLog
   WHERE IpAddress = '1.2.3.4' AND EventType = 'SuperAdminLoginSuccess'
   ORDER BY Timestamp DESC;
   ```

---

## 4. SQL Cheat Sheet

### Tenant lookup

```sql
-- All tenants + status
USE master;
SELECT Code, Name, DatabaseName, Status, CreatedAt FROM master.Tenants ORDER BY Code;

-- Active tenants only
SELECT Code, Name FROM master.Tenants WHERE Status = 'Active' ORDER BY Code;

-- Tenant by code (find the DatabaseName for switching context)
SELECT Code, Name, DatabaseName, Status FROM master.Tenants WHERE Code = 'ACME';
```

### User unlock

```sql
-- Tenant user (do this in the tenant DB)
USE WMS_Tenant_ACME;
UPDATE security.Users
SET LockedUntil = NULL, FailedLoginAttempts = 0
WHERE Email = 'admin@acme.com';

-- SuperAdmin (master DB)
USE master;
UPDATE master.SuperAdmins
SET LockedUntil = NULL, FailedLoginAttempts = 0
WHERE Email = 'superadmin@wms.local';
```

### Audit log inspection

```sql
-- Tenant-side audit (in tenant DB)
USE WMS_Tenant_ACME;
SELECT TOP 100 CreatedAt, EventType, EntityType,
       (SELECT Email FROM security.Users WHERE Id = a.UserId) AS Actor,
       IpAddress, Details
FROM security.AuditLog a
ORDER BY CreatedAt DESC;

-- Cross-tenant (master DB) — SuperAdmin actions + tenant lifecycle
USE master;
SELECT TOP 100 Timestamp, EventType, Severity,
       UserEmail, TenantId, EntityType, IpAddress, Details
FROM master.SystemAuditLog
ORDER BY Timestamp DESC;

-- Failed logins in the last hour
SELECT IpAddress, UserEmail, COUNT(*) AS Attempts
FROM master.SystemAuditLog
WHERE EventType IN ('SuperAdminLoginFailure', 'LoginFailure')
  AND Timestamp >= DATEADD(HOUR, -1, SYSUTCDATETIME())
GROUP BY IpAddress, UserEmail
ORDER BY Attempts DESC;
```

### User counts per tenant

```sql
USE master;
SELECT
    t.Code,
    t.Name,
    t.Status,
    (SELECT COUNT(*) FROM master.UserTenantMap m WHERE m.TenantId = t.Id) AS UserCount
FROM master.Tenants t
ORDER BY UserCount DESC;
```

### Tenant DB size

```sql
-- Run from master, returns size for every WMS_Tenant_ DB
SELECT
    name AS DatabaseName,
    CAST(SUM(size) * 8.0 / 1024 AS DECIMAL(10,2)) AS SizeMB
FROM sys.master_files
WHERE database_id IN (
    SELECT database_id FROM sys.databases WHERE name LIKE 'WMS_Tenant_%'
)
GROUP BY name
ORDER BY SizeMB DESC;
```

### Stock value across all warehouses (one tenant)

```sql
USE WMS_Tenant_ACME;
SELECT
    SUM(s.QuantityOnHand) AS TotalUnits,
    COUNT(DISTINCT s.ProductId) AS DistinctProducts,
    COUNT(DISTINCT s.LocationId) AS DistinctLocations
FROM inventory.Stock s
WHERE s.QuantityOnHand > 0;
```

(Note: value calc requires owner-scoped pricing via `master.ProductOwners.SettlementPrice` — TD-045 from Phase 23.)

---

## 5. Common Operations

### Add a SuperAdmin

Today: config-driven first-run seed only (TD-088 covers a UI add path). For manual addition:

```sql
USE master;
-- Hash 'TempPass2026!' at BCrypt cost 12 via a C# snippet or online tool
INSERT INTO master.SuperAdmins
    (Id, Email, PasswordHash, FullName, IsActive, MustChangePassword, FailedLoginAttempts, CreatedAt)
VALUES
    (NEWID(),
     'second.admin@wms.local',
     '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/LewdBPj/IZmK5b8gG',  -- replace with real BCrypt hash
     'Second Platform Admin',
     1, 1, 0, SYSUTCDATETIME());
```

Send the temp password through a secure channel. They'll be forced to rotate on first login.

### Force-clear a stuck Hangfire recurring job

```pwsh
# From a C# Hangfire-aware script or directly via SQL on Master DB
USE master;
-- Inspect the Hangfire tables (HangFire schema)
SELECT * FROM HangFire.Job WHERE StateName <> 'Succeeded' ORDER BY CreatedAt DESC;
SELECT * FROM HangFire.Schedule;

-- Delete a stuck recurring job entry by Id
DELETE FROM HangFire.Job WHERE Id = <job-id>;
```

Prefer the `/hangfire` dashboard's "Delete" button when possible.

### Force log rotation

Serilog's File sink rotates on calendar day boundary; no manual trigger needed. To free up disk space immediately:

```pwsh
# Compress or move logs older than 7 days
Get-ChildItem D:\WMSApp\logs\wms-*.log |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-7) } |
    Compress-Archive -DestinationPath "D:\WMSApp\logs\archive\$(Get-Date -Format 'yyyy-MM').zip" -Update
```

### View live tenant statistics

```sql
USE master;
WITH TenantStats AS (
    SELECT
        t.Code,
        t.Status,
        t.DatabaseName,
        (SELECT COUNT(*) FROM master.UserTenantMap m WHERE m.TenantId = t.Id) AS MappedUsers
    FROM master.Tenants t
)
SELECT * FROM TenantStats ORDER BY Code;
```

For richer stats (active user count, stock counts, doc storage size) see TD-082 (per-tenant stats dashboard).

---

## 6. Backup & Recovery

### Recommended schedule

| Backup type | Frequency | Retention | Tool |
|---|---|---|---|
| Master DB full | Daily | 30 days | SQL Server Agent / Ola Hallengren scripts |
| Master DB log | Hourly | 7 days | SQL Server Agent (transaction log) |
| Tenant DB full | Daily | 30 days | SQL Server Agent — script iterates `master.Tenants` |
| Tenant DB log | Hourly | 7 days | SQL Server Agent |
| Document storage | Daily | 90 days | robocopy to network share / blob lifecycle |
| Logs | Daily compress | 90 days | Manual or scheduled task (see "Force log rotation") |

Backup automation is TD-071 — until automated, the schedule lives in SQL Server Agent / Task Scheduler.

### Restore drill (quarterly)

At least once per quarter:

1. Pick a tenant DB at random (not in active use).
2. Restore the last night's full backup to a standby SQL Server instance under a different name (`WMS_Tenant_ACME_RestoreTest`).
3. Connect from the WMS web app with overridden `TenantTemplate` env var pointing to the restored DB.
4. Smoke-test login + 3-5 typical reads (dashboard, reports, audit log).
5. Document: time taken, any errors, lessons learned.

This catches latent backup-corruption issues before a real DR scenario.

### Per-tenant restore (DR scenario)

Single-tenant corruption: customer reports data loss / DB unavailable.

1. Suspend the tenant in master (blocks user logins immediately):
   ```sql
   UPDATE master.Tenants SET Status = 'Suspended' WHERE Code = 'ACME';
   ```
2. Restore the tenant DB from last good backup:
   ```sql
   RESTORE DATABASE [WMS_Tenant_ACME]
   FROM DISK = 'D:\Backup\WMS_Tenant_ACME_2026-05-15.bak'
   WITH REPLACE, NORECOVERY;
   ```
3. Apply transaction log backups up to the recovery point:
   ```sql
   RESTORE LOG [WMS_Tenant_ACME]
   FROM DISK = 'D:\Backup\WMS_Tenant_ACME_log_2026-05-15_18.trn'
   WITH NORECOVERY;
   -- repeat for each log, last one WITH RECOVERY
   ```
4. Verify integrity:
   ```sql
   USE WMS_Tenant_ACME;
   DBCC CHECKDB;
   ```
5. Re-run pending migrations against the restored DB:
   ```pwsh
   $env:ConnectionStrings__TenantTemplate = "Server=...;Database=WMS_Tenant_ACME;..."
   dotnet WMS.Migrate.dll up tenant
   ```
6. Reactivate in master:
   ```sql
   UPDATE master.Tenants SET Status = 'Active' WHERE Code = 'ACME';
   ```
7. Notify customer + audit:
   ```sql
   USE master;
   INSERT INTO master.SystemAuditLog
       (Id, EventType, Severity, UserId, UserEmail, TenantId, EntityType, EntityId, Details, IpAddress, Timestamp)
   VALUES
       (NEWID(), 'TenantRestored', 'Warning', NULL, NULL,
        (SELECT Id FROM master.Tenants WHERE Code = 'ACME'),
        'Tenant',
        (SELECT Id FROM master.Tenants WHERE Code = 'ACME'),
        '{"backup_timestamp":"2026-05-15 02:00","recovery_point":"2026-05-15 18:00","reason":"reported corruption"}',
        NULL, SYSUTCDATETIME());
   ```

---

## 7. Routine Tasks

### Weekly

- [ ] Review `master.SystemAuditLog` for the past week — flag anomalies (unusual SuperAdmin actions, repeated failed logins)
- [ ] Check disk space on log + database drives
- [ ] Verify Hangfire daily jobs ran (`pack-video-retention-cleanup`)
- [ ] Tail `wms-{today}.log` and look for `Error` / `Fatal` entries
- [ ] Compress logs older than 14 days

### Monthly

- [ ] Review tenant list — confirm Status reflects current relationship
- [ ] Audit SuperAdmin user list — deactivate any who've left the team
- [ ] Review uncategorised TDs in `docs/TECH_DEBT.md` for prioritisation
- [ ] Spot-check a tenant's user list for orphaned / unused accounts

### Quarterly

- [ ] Restore drill (Section 6)
- [ ] DR runbook review — verify still-current contacts + procedures
- [ ] Password policy review — confirm Phase 25 baseline (8+/mixed/digit) still meets compliance asks
- [ ] Performance benchmarking against baseline (TD-073)

### Yearly

- [ ] SSL/TLS certificate rotation
- [ ] SuperAdmin password rotation (force-change via DB if needed)
- [ ] Cull cancelled / archived tenant DBs that are past retention policy
- [ ] Full DR simulation (host failure)

---

## Related Documents

- [Customer onboarding playbook](./onboarding-playbook.md) — sales-led tenant creation
- [Deployment checklist](../deployment/checklist.md) — pre/during/post deploy
- [Tech debt log](../TECH_DEBT.md) — open work items
- [ADRs](../decisions/) — architectural rationale
