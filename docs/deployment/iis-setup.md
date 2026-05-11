# IIS Setup

Phase 26 baseline — what's needed to host WMS on Windows Server + IIS.

## Prerequisites

1. **Windows Server 2019+** with IIS role installed
2. **[.NET 8 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/8.0)** — installs the ASP.NET Core Module + the runtime. After install:
   ```pwsh
   net stop was /y; net start w3svc
   ```
3. **SQL Server 2019+** (reachable from the IIS host) — `WMS_Master` DB pre-created (the migrator does NOT create databases; per `feedback_no_auto_create_db.md`)

## Application Pool

- **.NET CLR version**: `No Managed Code` (ASP.NET Core uses the in-process hosting model — the worker doesn't load .NET Framework)
- **Identity**: dedicated service account (e.g. `IIS APPPOOL\WMSApp` or a domain account) with:
  - SQL Server login mapped to `db_owner` on `WMS_Master` + every tenant DB (or db_datareader + db_datawriter + EXECUTE on stored procs)
  - Modify rights on `D:\WMSData\storage` (document storage path)
  - Modify rights on `D:\WMSApp\logs` (Serilog file sink)
- **Idle Timeout**: `0` (never idle out — keeps Hangfire scheduler running)
- **Start Mode**: `AlwaysRunning`

## Site Configuration

1. **Physical path**: `D:\WMSApp\publish` (output of `dotnet publish -c Release`)
2. **Application pool**: the one above
3. **Bindings**:
   - HTTPS on 443 with a valid TLS certificate
   - HTTP on 80 → redirect to HTTPS via `UseHttpsRedirection` (already wired in Program.cs)
4. **Site → Configuration Editor**:
   - `system.webServer/aspNetCore/environmentVariables` — set
     `ASPNETCORE_ENVIRONMENT=Production`, `ConnectionStrings__MasterDb=...`,
     `ConnectionStrings__TenantTemplate=...` (see `configuration.md`)

## Application Initialization (warm-start)

Avoids the first-request hit by pre-warming the worker. Install the
**Application Initialization** IIS feature, then:

- App pool → Advanced Settings → `Start Mode = AlwaysRunning`
- Site → Advanced Settings → `Preload Enabled = True`
- Edit `web.config` to add an initialization page (already a HealthCheck endpoint):
  ```xml
  <system.webServer>
    <applicationInitialization doAppInitAfterRestart="true">
      <add initializationPage="/healthz/ready" />
    </applicationInitialization>
  </system.webServer>
  ```

## Web.config (publish output)

`dotnet publish` auto-generates `web.config` with the `aspNetCore`
handler block. The team should ONLY edit the `environmentVariables` +
`applicationInitialization` sections; everything else is regenerated on
each publish.

## TLS / HTTPS

- Use a real certificate from your enterprise CA or Let's Encrypt
- Inside Program.cs `UseHsts()` is wired (non-Development); max-age
  defaults to 30 days
- `Strict-Transport-Security` header is emitted; subdomains NOT
  included by default (tune in Program.cs if you serve subdomains)

## Hangfire Dashboard

`/hangfire` requires ADMIN role (Phase 25). Operators MUST sign in
through the regular `/Auth/Login` flow first — the dashboard reads the
session cookie + checks `security.UserRoles` for ADMIN.

## Health Endpoints

Configure load balancer / monitoring probes:

| Endpoint              | Purpose                                      | Auth needed |
|-----------------------|----------------------------------------------|-------------|
| `/healthz/live`       | Pure process-alive (no DB)                   | No          |
| `/healthz/ready`      | Process-alive + Master DB reachable (5s timeout) | No          |
| `/healthz`            | Alias for `/healthz/ready`                   | No          |
| `/health`             | Backward-compat (Phase 17 minimal endpoint)  | No          |

All return JSON: `{ status, totalDuration, entries: { ... } }`.

For IIS health monitoring (the built-in feature), point at `/healthz/live`
(<100ms) — not `/healthz/ready` (1-2s under load).
