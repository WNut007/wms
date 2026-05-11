# Configuration

Phase 26 baseline — what's in `appsettings.json` vs what MUST come from
environment variables (or User Secrets in development).

## Connection strings — env-var only in production

`appsettings.Production.json` ships with empty `ConnectionStrings` values
so a fresh deploy without env vars fails fast at startup via
`ConfigurationValidator.cs`.

Required env vars (ASP.NET Core mapping for nested keys uses `__`):

| Env var                                    | Maps to                                | Example value                                                                                          |
|--------------------------------------------|----------------------------------------|--------------------------------------------------------------------------------------------------------|
| `ConnectionStrings__MasterDb`              | `ConnectionStrings:MasterDb`           | `Server=tcp:prod-sql.example.net;Database=WMS_Master;User Id=wms_app;Password=...;TrustServerCertificate=true` |
| `ConnectionStrings__TenantTemplate`        | `ConnectionStrings:TenantTemplate`     | `Server=tcp:prod-sql.example.net;Database={0};User Id=wms_app;Password=...;TrustServerCertificate=true`         |

`TenantTemplate` is a connection-string template — `{0}` (or the literal
token `WMS_TenantTemplate`) is replaced with `master.Tenants.DatabaseName`
when the tenant migrator iterates active tenants.

### IIS

Set env vars at the **Application Pool** level so they're inherited by
the worker process:

1. IIS Manager → Application Pools → Right-click the pool → **Advanced Settings**
2. Use the [IIS Configuration Editor](https://docs.microsoft.com/iis/get-started/getting-started-with-iis/getting-started-with-iis-manager) on the site:
   `system.webServer/aspNetCore/environmentVariables` → add a Collection Editor row.

Alternative: edit the auto-generated `web.config` (created by `dotnet publish`):

```xml
<system.webServer>
  <aspNetCore ...>
    <environmentVariables>
      <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
      <environmentVariable name="ConnectionStrings__MasterDb" value="..." />
      <environmentVariable name="ConnectionStrings__TenantTemplate" value="..." />
    </environmentVariables>
  </aspNetCore>
</system.webServer>
```

> ⚠️ `web.config` env vars are visible to anyone with file-system access.
> For real secret management, use Azure Key Vault / Windows DPAPI / a
> reverse proxy — TD-066.

### Development

`dotnet user-secrets` is the canonical home for local connection
strings. From the repo root:

```pwsh
dotnet user-secrets --project src/WMS.Web set "ConnectionStrings:MasterDb" "Server=localhost;..."
dotnet user-secrets --project src/WMS.Web set "ConnectionStrings:TenantTemplate" "Server=localhost;Database={0};..."
```

Falls back to `appsettings.json` defaults (`Server=localhost;Trusted_Connection=true`)
for local SQL Server instances. ConfigurationValidator emits a stderr
warning if both are empty + ASP.NET environment ≠ Production, but boot
continues.

## Other configuration

| Section                     | File                                | Notes |
|-----------------------------|-------------------------------------|-------|
| `Serilog`                   | `appsettings.Production.json`       | Console + File sinks; daily rolling; 30-day retention; 100MB cap per file |
| `Storage:Local:RootPath`    | env var or `appsettings.Production.json` | Absolute path on the server for document storage (e.g. `D:\WMSData\storage`) |
| `Storage:MaxFileSizeMB`     | `appsettings.json`                  | Default 50 — pack videos may need 50-100 |
| `PackVideoRetention`        | `appsettings.json`                  | Default 10 days; cron `0 3 * * *` (03:00 UTC daily) |
| `SecurityHeaders`           | `appsettings.Production.json`       | CSP / Frame-Options / Referrer-Policy / Permissions-Policy — Phase 26 T2 |

## Verifying configuration on startup

```pwsh
$env:ASPNETCORE_ENVIRONMENT="Production"
$env:ConnectionStrings__MasterDb="Server=...;Database=WMS_Master;..."
$env:ConnectionStrings__TenantTemplate="Server=...;Database={0};..."
dotnet src/WMS.Web/bin/Release/net8.0-windows/WMS.Web.dll
```

`ConfigurationValidator` runs first. On missing keys the app exits with
`InvalidOperationException` listing the missing settings — the Serilog
File sink may not have flushed yet, so check stderr / Console.
