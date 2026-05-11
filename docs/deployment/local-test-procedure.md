# Local deployment test procedure

Phase 30A — what to run on your workstation to exercise the full
deploy chain before a real server is provisioned. Catches the
config / migration / publish-bundle / health-endpoint surfaces that
Phase 26 added.

This is **dogfood validation**, not production deployment. The
publish artifact is identical to what would land on a server, but
it runs under Kestrel instead of IIS, against your local SQL Server.

---

## 0. Prerequisites

| Tool | Version | How to check |
|------|---------|--------------|
| .NET SDK | 8.0+ | `dotnet --version` |
| SQL Server | 2019+ or LocalDB | `sqlcmd -L` |
| PowerShell | 7+ | `$PSVersionTable.PSVersion` |
| Git | any | `git --version` |

You also need:
- An empty `WMS_Master` database (created manually in SSMS — the migration tool does NOT create databases per `feedback_no_auto_create_db.md`).
- A SQL login with `db_owner` on `WMS_Master` + `dbcreator` server-role (Phase 27 tenant provisioning needs to CREATE DATABASE).

---

## 1. Set connection-string env vars

Required (matches Phase 26 `ConfigurationValidator` strict mode in Production):

```powershell
$env:ConnectionStrings__MasterDb = `
    "Server=localhost;Database=WMS_Master;Trusted_Connection=True;TrustServerCertificate=True"

$env:ConnectionStrings__TenantTemplate = `
    "Server=localhost;Database={0};Trusted_Connection=True;TrustServerCertificate=True"
```

`{0}` is the placeholder the tenant migrator replaces with
`master.Tenants.DatabaseName` when fanning out.

Use SQL auth instead of Trusted_Connection when running under a
service account that doesn't own the SQL login:

```powershell
$env:ConnectionStrings__MasterDb = `
    "Server=localhost;Database=WMS_Master;User Id=wms_app;Password=...;TrustServerCertificate=True"
```

If you don't set these, `Test-Local-Deploy.ps1` will prompt for them
interactively (handy for one-off runs) but they vanish at the end
of the PowerShell session.

### Optional — email (Phase 30A M1)

Without Email env vars, `EmailOptions.TestMode` stays `true` and the
service logs emails instead of sending them. To test live Gmail SMTP:

```powershell
$env:Email__TestMode    = "false"
$env:Email__Username    = "your-wms-test@gmail.com"
$env:Email__Password    = "your-16-char-app-password"  # NOT your Gmail password
$env:Email__FromAddress = "your-wms-test@gmail.com"
$env:Email__LoginUrl    = "http://localhost:5500/Auth/Login"
```

App Password setup: Google Account → Security → 2-Step Verification
→ App passwords → "WMS test". Copy the 16-char string. The TD-110
SendGrid switch removes this friction post-launch.

---

## 2. Run the deploy script

From the repo root:

```powershell
.\scripts\deploy\Test-Local-Deploy.ps1
```

What this does:

1. Validates `ConnectionStrings__*` env vars (prompts if missing).
2. `dotnet publish src/WMS.Web/WMS.Web.csproj -c Release -o publish-local`.
3. Applies Master migrations (`tools/WMS.Migrate up master`).
4. Applies Tenant migrations across all `Active` tenants (`up tenants`).
5. Verifies `WMS.Web.dll` + `WMS.BLL.dll` (embedded email templates) present.
6. Sets `ASPNETCORE_URLS=http://localhost:5500` + `ASPNETCORE_ENVIRONMENT=Production`.
7. Launches Kestrel under `dotnet WMS.Web.dll`.

Flags:

| Flag | Use |
|------|-----|
| `-Port 5550` | Different port (default 5500 avoids 5000/5001 conflict) |
| `-PublishPath C:\deploys\wms` | Custom publish dir |
| `-Environment Staging` | Override env (default `Production`) |
| `-SkipMigrate` | Iterate on UI without re-running migrations |
| `-SkipBuild` | Re-run against an existing publish |
| `-LaunchBrowser` | Open http://localhost:5500 after start |
| `-NoStart` | Stop after publish + migrate (debug manually) |

---

## 3. Run the automated smoke

In a SECOND PowerShell window (Kestrel keeps the first):

```powershell
.\scripts\smoke\Smoke-Local.ps1
```

12 scenarios are checked:

| ID | Scenario | Verifies |
|----|----------|----------|
| H1 | `GET /healthz/live` | Process alive, returns 200 |
| H2 | `GET /healthz/ready` | Master DB probe + JSON envelope shape |
| H3 | `GET /healthz` | Alias for `/healthz/ready` |
| H4 | `GET /health` | Phase 17 legacy endpoint still works |
| P1 | `GET /` | 200 or 302 to /Auth/Login |
| P2 | `GET /Auth/Login` | Renders |
| P3 | `GET /SuperAdmin/Auth/Login` | Separate cookie scheme reachable |
| S1 | `X-Frame-Options: DENY` | SecurityHeadersMiddleware wired |
| S2 | `X-Content-Type-Options: nosniff` | ditto |
| S3 | `Referrer-Policy` | ditto |
| S4 | `Server` header stripped | ditto (no stack fingerprint) |
| E1 | 404 page branded | Error pages routed |

Exit code 0 = green, 1 = anything red. Use as a pre-tag gate.

---

## 4. Manual smoke (M3 checklist)

`Smoke-Local.ps1` covers the request-shape surfaces. For UI / E2E
flows (SuperAdmin login → create tenant → tenant admin login →
MustChangePassword → SO → Pick → Pack → Ship), follow
`docs/deployment/local-smoke-checklist.md` (M3, this phase).

---

## 5. Common failures

### `ConfigurationValidator` throws on startup

**Symptom**: Kestrel exits with `Production environment requires
ConnectionStrings:MasterDb and ConnectionStrings:TenantTemplate`.

**Fix**: env vars not set OR misnamed. Double underscore (`__`)
maps to the config-section separator. `ConnectionStrings_MasterDb`
(single underscore) silently no-ops.

### Master migration fails with `Cannot open database "WMS_Master"`

**Symptom**: FluentMigrator can't reach the master DB.

**Fix**: The migration tool does NOT create databases. Create
`WMS_Master` manually in SSMS:

```sql
CREATE DATABASE WMS_Master;
GO
```

Then re-run with `-SkipBuild` to skip the publish step.

### Tenant fan-out exits non-zero with empty tenant list

**Symptom**: `up tenants` exits with a warning + "0 tenants
processed".

**Fix**: Expected on a fresh `master.Tenants`. The script asks for
confirmation. Press `y` to continue — you'll create the first
tenant via the SuperAdmin UI after Kestrel starts.

### Smoke S1-S4 fail — security headers missing

**Symptom**: Headers absent in `/Auth/Login` response.

**Fix**: Likely running under `Development` environment (security
headers middleware applies in all environments, but
`appsettings.json` ships an empty `SecurityHeaders` section that
non-Production environments inherit). Re-run with
`-Environment Production` to pick up the production header policy
defined in `appsettings.Production.json`.

### Email health check reports Unhealthy

**Symptom**: `/healthz/ready` returns Unhealthy with `Email:Username`
+ `Email:Password` in the description.

**Fix**: Either set the `Email__*` env vars (section 1 above), or
flip `Email__TestMode=true` to mark the check Degraded but not
failing. TestMode is the safe default — the readiness probe failing
because SMTP isn't configured is annoying but expected on the first
deploy.

### `error MSB4225` / publish fails

**Symptom**: Build error referencing missing NuGet packages.

**Fix**: Re-restore:

```powershell
dotnet restore
.\scripts\deploy\Test-Local-Deploy.ps1
```

### Port 5500 in use

**Symptom**: `Failed to bind to address http://127.0.0.1:5500`.

**Fix**: A previous Kestrel didn't exit cleanly. Either kill the
stray process (`Get-Process -Name dotnet | Stop-Process -Force` —
careful, kills ALL dotnet processes including your IDE if it's
hosting one), or use `-Port 5550`.

---

## 6. Cleanup

After validating:

```powershell
# Stop Kestrel (Ctrl+C in the deploy window)

# Remove publish artifact
Remove-Item -Recurse -Force .\publish-local

# Optionally drop the test tenant DBs (if you created any via
# SuperAdmin during the smoke). Phase 27 provisioning rollback
# DROPs DBs on failure but leaves successful ones in place.
# In SSMS:
#   SELECT name FROM sys.databases WHERE name LIKE 'WMS_Tenant_%';
#   DROP DATABASE WMS_Tenant_TESTCO;
#   DELETE FROM WMS_Master.master.Tenants WHERE Code = 'TESTCO';
```

---

## 7. What this does NOT cover

Logged for Phase 30B (real server deployment):

- IIS installation + Application Initialization
- TLS cert + HSTS preload
- Windows service account + DB grants
- Real DNS + firewall rules
- Backup automation
- CI/CD pipeline integration
- Multi-instance deployment + distributed cache (Redis)
- Application Insights / log aggregation
- DR runbook + restore drills

See `docs/deployment/iis-setup.md` for the IIS-side procedure.
