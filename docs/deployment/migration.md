# Database Migration

Phase 26 — order of operations for applying schema changes in
production. Uses `tools/WMS.Migrate` (FluentMigrator runner).

## Pre-migration checklist

- [ ] Backup `WMS_Master` (full backup; not differential)
- [ ] Backup each active tenant DB (loop `master.Tenants` WHERE Status='Active')
- [ ] Verify backups restore on a separate instance — at least one tenant DB + master
- [ ] Smoke `/healthz/ready` on the existing deployment (confirms baseline before maintenance window)
- [ ] Notify users of maintenance window via comms channel
- [ ] Stop IIS app pool to prevent live mutations during migration:
  ```pwsh
  Stop-WebAppPool -Name "WMSApp"
  ```

## Step 1 — apply Master migrations

```pwsh
cd D:\WMSApp\publish\tools
$env:ConnectionStrings__MasterDb = "Server=...;Database=WMS_Master;..."
$env:ConnectionStrings__TenantTemplate = "Server=...;Database={0};..."
dotnet WMS.Migrate.dll up master
```

Output should end with `Latest applied version: <timestamp>`. Exit code
0 = success.

## Step 2 — apply Tenant migrations to ALL active tenants

The Phase 26 `tenants` mode reads `master.Tenants WHERE Status='Active'`
and runs Tenant-tagged migrations against each tenant's `DatabaseName`.

```pwsh
dotnet WMS.Migrate.dll up tenants
```

Output (per tenant):

```
Found 3 active tenant(s). Running 'up' against each...

--- Tenant: ACME (WMS_Acme) ---
[FluentMigrator] ...
--- Tenant: GLOBEX (WMS_Globex) ---
...

All 3 tenant(s) migrated successfully.
```

**Stops on first failure** (fail-fast). Re-running is safe — FluentMigrator's
per-DB `VersionInfo` table records applied migrations.

## Step 3 — restart the application

```pwsh
Start-WebAppPool -Name "WMSApp"
```

Browse `/healthz/ready` and confirm `status=Healthy` JSON. Sign in as
ADMIN and spot-check the Reports + Audit Log pages.

## Rollback (Master only)

```pwsh
dotnet WMS.Migrate.dll down master
```

Rolls back ONE migration. For multi-step rollback, run repeatedly.

### Tenant rollback

`down tenants` is intentionally NOT supported — fan-out rollback is too
easy to misuse. Instead, roll back tenant-by-tenant against each
tenant's connection string directly:

```pwsh
$env:ConnectionStrings__TenantTemplate = "Server=...;Database=WMS_Acme;..."
dotnet WMS.Migrate.dll down tenant
```

## Suspending a tenant

To exclude a tenant from the next `up tenants` run, set Status:

```sql
UPDATE master.Tenants SET Status = 'Suspended' WHERE Code = 'ACME';
```

Re-enable with `Status='Active'` and re-run the coordinator.

## Listing applied migrations

```pwsh
dotnet WMS.Migrate.dll list master            # master DB
dotnet WMS.Migrate.dll list tenant            # against TenantTemplate
dotnet WMS.Migrate.dll version
```

`list` shows `[applied]` / `[pending]` per migration version. Useful
for sanity-checking before kicking off `up`.
