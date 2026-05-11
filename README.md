# WMS Platform

> Multi-tenant warehouse management system.
> .NET 8 · SQL Server · Multi-tenant SaaS (DB-per-tenant per ADR-001).

Designed for B2B + B2C + 3PL workloads. Targets 5,000+ B2C orders/day at production scale.

## Status

| | |
|---|---|
| Current tag | `v2.14.0-docs` |
| Working toward | `v3.0.0` (first customer onboarding) |
| Tests | **1024 passing** (353 unit + 671 integration + 5 skipped) |
| Mobile suite | 6 of 6 PWAs complete (Pick / Receive / Pack / Putaway / Count / Locate) |
| Deployable | Yes — see `docs/deployment/` |
| Onboardable | Yes — SuperAdmin tenant provisioning via `/SuperAdmin/Tenants/Create` |

v3.0.0 chapter completion:

- ✅ Reports (v2.9.0)
- ✅ Tenant Admin (v2.10.0)
- ✅ Security Hardening (v2.11.0)
- ✅ Deployment Foundation (v2.12.0)
- ✅ SuperAdmin Tenant Onboarding (v2.13.0)
- ✅ Documentation MVP (v2.14.0) ← you are here
- 🔜 Beta Polish (v2.15.0)
- 🔜 First Customer = v3.0.0

## Tech stack

- **.NET 8** — ASP.NET Core MVC + Razor views
- **Dapper** — data access (no Entity Framework per ADR-002)
- **SQL Server 2022** — DB-per-tenant (ADR-001)
- **FluentMigrator** — schema migrations (master + tenant tags)
- **Hangfire** — background jobs + dashboard at `/hangfire`
- **Serilog** — structured logging (Console + daily-rolling File sink in production)
- **BCrypt.Net-Next** — password hashing (cost 12 prod / 4 test)
- **ApexCharts** — Reports dashboards
- **ClosedXML** — Excel export
- **htmx + Alpine.js** — light client-side interactivity for desktop
- **PWA + Bootstrap 5** — 6 mobile workflows (no native app)
- **xUnit + Moq** — tests

Build target: `net8.0` (libraries) / `net8.0-windows` (WMS.Web + WMS.IntegrationTests; FastReport demo dep).

## Quick start (developer)

```bash
# Clone
git clone https://github.com/WNut007/wms.git
cd wms

# Setup user secrets (one-time)
dotnet user-secrets --project src/WMS.Web set "ConnectionStrings:MasterDb" "Server=localhost;Database=WMS_Master;Trusted_Connection=true;TrustServerCertificate=true"
dotnet user-secrets --project src/WMS.Web set "ConnectionStrings:TenantTemplate" "Server=localhost;Database={0};Trusted_Connection=true;TrustServerCertificate=true"

# Create DBs (manually in SSMS — migrator does NOT create databases)
# CREATE DATABASE WMS_Master;
# CREATE DATABASE WMS_Tenant_Template;

# Migrate
cd tools/WMS.Migrate
dotnet run -- up master
dotnet run -- up tenant   # against TenantTemplate
cd ../..

# Run
dotnet run --project src/WMS.Web
```

Then browse `http://localhost:5000/Auth/Login` — seeded admin (Migration_041) is `nwuthipongworachoke@gmail.com` with the password baked into Migration_041's BCrypt hash.

SuperAdmin login at `/SuperAdmin/Login` — seeded by `SuperAdminBootstrap.EnsureAsync` from `appsettings.json:InitialSuperAdmin`.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         WMS Platform                             │
│                                                                  │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────────┐    │
│  │  Tenant UI   │   │ Mobile PWAs  │   │  SuperAdmin UI   │    │
│  │  (/Auth)     │   │ (6 routes)   │   │ (/SuperAdmin)    │    │
│  └──────┬───────┘   └──────┬───────┘   └────────┬─────────┘    │
│         │                  │                     │              │
│         └──────────────────┼─────────────────────┘              │
│                            │                                     │
│                  ┌─────────▼──────────┐                          │
│                  │   ASP.NET Core     │                          │
│                  │   WMS.Web          │                          │
│                  └─────────┬──────────┘                          │
│                            │                                     │
│              ┌─────────────┴──────────────┐                      │
│              │                            │                      │
│       ┌──────▼──────┐              ┌──────▼──────┐               │
│       │  WMS.BLL    │              │  WMS.Jobs   │               │
│       │  (Services) │              │  (Hangfire) │               │
│       └──────┬──────┘              └──────┬──────┘               │
│              │                            │                      │
│       ┌──────▼──────┐                     │                      │
│       │  WMS.DAL    │                     │                      │
│       │  (Dapper)   │                     │                      │
│       └──────┬──────┘                     │                      │
│              │                            │                      │
│   ┌──────────┴──────────┐                 │                      │
│   │                     │                 │                      │
│   ▼                     ▼                 ▼                      │
│ ┌────────────┐   ┌──────────────┐    ┌────────────┐              │
│ │ Master DB  │   │ Tenant DBs   │    │ HangFire   │              │
│ │            │   │ (per tenant) │    │ schema     │              │
│ │ Tenants    │   │              │    │            │              │
│ │ SuperAdmins│   │ Users        │    │ on Master  │              │
│ │ SystemAudit│   │ Stock        │    │            │              │
│ │ UserTenant │   │ SalesOrders  │    │            │              │
│ │  Map       │   │ AuditLog     │    │            │              │
│ │            │   │ ... + ~30    │    │            │              │
│ │            │   │ more tables  │    │            │              │
│ └────────────┘   └──────────────┘    └────────────┘              │
└─────────────────────────────────────────────────────────────────┘
```

Per-tenant connection resolved via `ITenantConnectionFactory` (cached 5-min via `IMemoryCache`).

## Modules

| Module | Surfaces | Status |
|---|---|---|
| Auth | `/Auth/*` 3-step login | ✅ Phase 1 |
| Master Data | Products / Customers / Warehouses / Categories / UoMs / Carriers / Owners | ✅ Phase 6B + 7 |
| Inbound | PO + Receiving + Putaway (desktop + mobile) | ✅ Phase 9-10 + 18 + 20 |
| Inventory | Stock + Adjustments + Cycle Counts + Transfers | ✅ Phase 11-13 + 21 |
| Outbound | SalesOrders + Allocation + Pick + Pack + Ship (desktop + mobile) | ✅ Phase 14A-E + 16 + 19 |
| Mobile PWA Suite | `/pick` `/receive` `/pack` `/putaway` `/count` `/locate` | ✅ Phase 16 + 18-22 |
| Reports | Inventory / Orders / KPIs + Excel export | ✅ Phase 23 |
| Tenant Admin | Users + Roles + AuditLog | ✅ Phase 24 |
| Security | Password mgmt + rate limit + Hangfire admin gate | ✅ Phase 25 |
| Deployment | Health endpoints + Serilog file + security headers + migration coordinator | ✅ Phase 26 |
| SuperAdmin | Tenant CRUD + cross-tenant audit | ✅ Phase 27 |

Mobile PWA install: each route has `<route>/manifest.json` — operator visits in Chromium → menu → "Add to home screen".

## Documentation

### Operations
- [`docs/operations/runbook.md`](docs/operations/runbook.md) — daily ops + incident response + SQL cheat sheet
- [`docs/operations/onboarding-playbook.md`](docs/operations/onboarding-playbook.md) — sales-led customer onboarding

### Deployment
- [`docs/deployment/configuration.md`](docs/deployment/configuration.md) — env vars + connection strings + per-env config
- [`docs/deployment/iis-setup.md`](docs/deployment/iis-setup.md) — IIS site + app pool + TLS
- [`docs/deployment/migration.md`](docs/deployment/migration.md) — Master + Tenant migration order
- [`docs/deployment/checklist.md`](docs/deployment/checklist.md) — pre/during/post deploy

### Architecture
- [`docs/decisions/`](docs/decisions/) — ADRs (ADR-008 through ADR-020 codified; ADR-001 through ADR-007 informal in CLAUDE.md)
- [`docs/01_WMS_Master_Design.md`](docs/01_WMS_Master_Design.md) — system architecture overview
- [`docs/02_WMS_Database_Schema.md`](docs/02_WMS_Database_Schema.md) — tables + relationships
- [`docs/03_WMS_Implementation_Roadmap.md`](docs/03_WMS_Implementation_Roadmap.md) — 5-month phase plan
- [`docs/04_WMS_Quick_Reference.md`](docs/04_WMS_Quick_Reference.md) — decision cheatsheet
- [`docs/TECH_DEBT.md`](docs/TECH_DEBT.md) — open tech debt items

### Internal (codebase rules)
- [`CLAUDE.md`](CLAUDE.md) — architecture rules + phase log — **read first** before any change

## Contributing

Single-developer project today. Future contributors:

1. Read [`CLAUDE.md`](CLAUDE.md) entirely before touching code
2. Follow existing patterns — see `docs/decisions/` for the rationale
3. Audit-first methodology — before any phase, read the relevant `feedback_*.md` memory files
4. Tests must pass (`dotnet test` exits 0; currently 1024)
5. Each chunk should be commit-clean — see the chunk-by-chunk pattern in memory
6. Update CLAUDE.md if introducing a new pattern
7. Add an ADR if making an architectural decision

## License

TBD. Single-developer project pre-revenue; license decision deferred until first customer contract.

## Contact

- Email: nwuthipongworachoke@gmail.com
- GitHub: [@WNut007](https://github.com/WNut007)

Sales / partnership conversations welcome.
