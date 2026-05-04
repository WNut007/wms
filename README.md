# WMS — Warehouse Management System

Multi-tenant Warehouse Management System for B2B + B2C marketplace + 3PL/SaaS, targeting 5,000+ B2C orders/day.

See `docs/01_WMS_Master_Design.md` for the full system design and `CLAUDE.md` for the architecture rules every change must follow.

## Tech stack

- .NET 8 (ASP.NET Core MVC, Razor Views)
- Dapper for data access (no Entity Framework)
- SQL Server 2022 (DB-per-tenant)
- Telerik UI for ASP.NET Core (office screens)
- Bootstrap 5 + htmx + Alpine.js (mobile PWA workflows)
- Serilog (logging)
- xUnit + Moq (tests)
- FastReport (reports / PDFs)

## Build / Run / Test

```bash
# Build entire solution
dotnet build src/WMS.sln

# Run web app (binds to ports from launchSettings.json)
dotnet run --project src/WMS.Web

# Run all tests
dotnet test src/WMS.sln
```

Health check once the web app is running: `GET /health` → `200 Healthy`.

## Project structure

```
.
├── src/
│   ├── WMS.sln
│   ├── WMS.Web/              ASP.NET Core MVC entry point (net8.0-windows)
│   ├── WMS.BLL/              Business services + base service abstractions
│   ├── WMS.DAL/              Dapper repositories + base repository abstractions
│   ├── WMS.Domain/           Entities, DTOs, enums (BaseEntity lives here)
│   ├── WMS.Plugins/          Marketplace + carrier adapters
│   ├── WMS.Jobs/             Hangfire background jobs
│   └── WMS.Common/           Shared utilities
├── tests/
│   ├── WMS.UnitTests/        xUnit + Moq
│   └── WMS.IntegrationTests/ xUnit + WebApplicationFactory (net8.0-windows)
├── tools/                    Migration runner, seed runner (planned)
└── docs/                     Design docs and ADRs
```

## Documentation

- `CLAUDE.md` — architecture rules (read first before any change)
- `docs/01_WMS_Master_Design.md` — system architecture
- `docs/02_WMS_Database_Schema.md` — tables and relationships
- `docs/03_WMS_Implementation_Roadmap.md` — phase plan (5 months)
- `docs/04_WMS_Quick_Reference.md` — decision cheatsheet
- `docs/05_Week1_Action_Plan.md` — Week 1 day-by-day plan
- `docs/decisions/` — ADRs

## Current state

Day 1 scaffolding complete: solution + 9 projects + project references + base classes (BaseEntity, BaseRepository, BaseService, BaseController as stubs) + Serilog + appsettings hierarchy + `/health` endpoint. No business logic, migrations, auth, or DI for tenant resolution yet — those land in upcoming days per the Week 1 plan.
