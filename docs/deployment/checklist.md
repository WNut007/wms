# Deployment Checklist

Phase 26 — pre / during / post checklist for every production deploy.

## Pre-deploy

- [ ] All tests pass locally (`dotnet test` exits 0)
- [ ] Branch merged to `main` with a tag (`v2.x.y-feature`)
- [ ] Release notes / CHANGELOG.md entry covering schema changes + new env vars + breaking changes
- [ ] Backups taken — see `migration.md`
- [ ] Maintenance window announced
- [ ] Verify env vars match config:
  - `ASPNETCORE_ENVIRONMENT=Production`
  - `ConnectionStrings__MasterDb`
  - `ConnectionStrings__TenantTemplate`

## Build artifact

```pwsh
dotnet publish src/WMS.Web/WMS.Web.csproj -c Release -o D:\WMSApp\publish\web --no-self-contained
dotnet publish tools/WMS.Migrate/WMS.Migrate.csproj -c Release -o D:\WMSApp\publish\tools --no-self-contained
```

- [ ] `web.config` present in `D:\WMSApp\publish\web`
- [ ] `appsettings.Production.json` present (committed; env vars override)
- [ ] No `appsettings.Development.json` deployed (could leak dev defaults)

## Deploy

- [ ] Stop IIS app pool: `Stop-WebAppPool -Name "WMSApp"`
- [ ] Copy publish output to the server (xcopy / robocopy / Octopus / etc.)
- [ ] Apply Master migrations: `dotnet D:\WMSApp\publish\tools\WMS.Migrate.dll up master`
- [ ] Apply Tenant migrations: `dotnet D:\WMSApp\publish\tools\WMS.Migrate.dll up tenants`
- [ ] Start IIS app pool: `Start-WebAppPool -Name "WMSApp"`

## Post-deploy verification

- [ ] `GET /healthz/live` returns 200 + `{"status":"Healthy",...}`
- [ ] `GET /healthz/ready` returns 200 + `master-db` entry healthy
- [ ] Sign in as ADMIN
- [ ] Smoke test:
  - `/Dashboard` loads
  - `/Reports/Inventory` loads with non-empty stats
  - `/Users` (Phase 24) lists users
  - `/AuditLog` shows recent events including the deploy-time LoginSuccess
  - `/hangfire` accessible to ADMIN (403 to non-ADMIN — verify with second account)
- [ ] Check Serilog file logs at `D:\WMSApp\logs\wms-{yyyy-MM-dd}.log` — recent entries from this deploy timestamped
- [ ] Verify Hangfire dashboard shows recurring jobs (`pack-video-retention-cleanup`)
- [ ] Pull request closed; tag pushed; release notes published

## Rollback (if needed)

- [ ] Restore Master DB from backup
- [ ] Restore each tenant DB from backup
- [ ] Re-deploy previous publish output (`D:\WMSApp\publish.previous`)
- [ ] Start IIS app pool
- [ ] Re-verify health endpoints

> 💡 Keep at least the last 2 publish outputs on the server (`publish` +
> `publish.previous`) so rollback is fast.

## Smoke after rollback

Same as post-deploy verification, but expectations match the
**previous** version's feature set.
