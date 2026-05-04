# Week 1 Action Plan: Project Foundation

**Goal**: Working solution structure + first authenticated page + database connectivity

---

## 📅 Day 1 (Monday): Solution Setup

### Morning: Tech Stack Spike (2-3 hours)

Before generating code, validate the tech stack works on your machine:

```bash
# Check .NET version
dotnet --version  # Should be 8.0+

# Create scratch project to test
mkdir test-spike && cd test-spike
dotnet new mvc -n TestApp
cd TestApp

# Add packages
dotnet add package Dapper
dotnet add package Microsoft.Data.SqlClient
dotnet add package Telerik.UI.for.AspNet.Core  # Need license

# Confirm builds
dotnet build
dotnet run
```

If anything fails — fix BEFORE starting real project.

### Afternoon: Real Project Setup

**Prompt to Claude Code:**

```
I'm starting the WMS project. Please set up the solution structure.

Tech stack (verified working):
- .NET 8 Core MVC
- Dapper for data access (NO Entity Framework)
- SQL Server 2022
- Telerik UI for ASP.NET Core MVC (office)
- Bootstrap 5 + htmx + Alpine.js (mobile)
- Hangfire for background jobs
- SignalR for real-time
- xUnit + Moq for tests

Solution structure:
WMS/
├── src/
│   ├── WMS.Web/           # ASP.NET Core MVC (entry point)
│   ├── WMS.BLL/           # Business logic services
│   ├── WMS.DAL/           # Dapper repositories
│   ├── WMS.Domain/        # Entities, DTOs, enums
│   ├── WMS.Plugins/       # Marketplace + carrier adapters
│   ├── WMS.Jobs/          # Hangfire jobs
│   └── WMS.Common/        # Shared utilities
├── tests/
│   ├── WMS.UnitTests/     # xUnit
│   └── WMS.IntegrationTests/  # Testcontainers
├── db/
│   ├── migrations/        # FluentMigrator
│   └── seeds/             # Test data
├── docs/                  # Design docs (I'll provide)
└── tools/                 # Migration runner, seed runner

Tasks:
1. Create solution and all projects
2. Set up project references (Web → BLL → DAL → Domain)
3. Add NuGet packages with stable versions
4. Configure base classes:
   - BaseController (with tenant resolution)
   - BaseService 
   - BaseRepository
5. Configure DI container (Program.cs)
6. Set up Serilog logging
7. Set up appsettings hierarchy (Development/Production)
8. Create initial README.md

Don't implement business logic yet. Just structure.

Show me your plan first, then implement.
```

**Expected output**: Working `dotnet build` succeeds. Empty solution structure ready.

---

## 📅 Day 2 (Tuesday): Database + Migrations

### Morning: Master DB Schema

**Prompt:**

```
Set up the Master DB schema for multi-tenant WMS.

Reference: docs/02_Database_Schema.md → "MASTER DB" section

Tasks:
1. Create FluentMigrator project: tools/WMS.Migrate
2. Add migration: Migration_20250101_001_CreateMasterDB
3. Implement these tables:
   - master.Tenants
   - master.UserTenantMap
   - master.SuperAdmins
   - master.LoginAttempts
   - master.PreAuthTokens
   - master.SystemAuditLog
4. Add seed data: 1 default tenant for testing
5. Create migration runner that I can invoke from CLI
6. Test rollback works

Important:
- Use TenantId as UNIQUEIDENTIFIER
- Encrypt connection strings (placeholder for now, real impl Day 3)
- Add indexes per design doc

Show me the migration code and run it against my local SQL.
```

### Afternoon: Tenant DB Schema (Layer 1)

**Prompt:**

```
Set up the Tenant DB schema - Foundation tables only.

Reference: docs/02_Database_Schema.md → schemas: master, security

Tasks:
1. Create migration: Migration_20250101_002_CreateTenantDB_Foundation
2. Implement schema "master":
   - master.Warehouses
   - master.WarehouseDocks
   - master.SystemSettings
3. Implement schema "security":
   - security.Users
   - security.Roles
   - security.Functions
   - security.RoleFunctionPermissions
   - security.UserRoles
   - security.AuditLog
4. Add seed data: admin user, default roles (Admin, Picker, Packer, Manager)
5. Document tenant DB provisioning process

Deferred for later weeks:
- master schema layers 2-6
- Other operational schemas

Show me migration + seed scripts.
```

---

## 📅 Day 3 (Wednesday): Authentication

### Morning: 3-Step Login Flow

**Prompt:**

```
Implement the 3-step login flow per docs/01_Master_Design.md "Login Flow".

Step 1: Email/password authentication
Step 2: Tenant selection (skip if user has only 1 tenant)
Step 3: Warehouse selection (skip if user has only 1 warehouse)

Architecture:
- Step 1 happens against Master DB
- Issue pre-auth token (5-min expiry)
- Step 2 uses pre-auth token, returns full session token
- Step 3 sets warehouse context

Tasks:
1. Create AuthController in WMS.Web/Controllers
2. Create LoginViewModel, TenantSelectViewModel, WarehouseSelectViewModel
3. Create AuthService in WMS.BLL/Services/Auth/
4. Implement password hashing (BCrypt)
5. Implement pre-auth tokens (master.PreAuthTokens table)
6. Create middleware: TenantResolutionMiddleware
7. Add cookie-based authentication
8. Login pages with Razor + Bootstrap 5

Constraints:
- Smart skip: if user has 1 tenant → skip step 2
- Smart skip: if user has 1 warehouse → skip step 3
- Rate limiting on login (track in master.LoginAttempts)
- Clear error messages without leaking info

Show me wireframe before implementation.
```

### Afternoon: Permission System

**Prompt:**

```
Implement the Function-CRUD permission system.

Reference: docs/01_Master_Design.md "Permissions: Function-CRUD Matrix"

Tasks:
1. Create PermissionService in WMS.BLL
2. Implement permission resolution:
   - Get user roles
   - Get function-CRUD matrix per role
   - OR aggregate across multiple roles
   - Apply user-level overrides (grants/denies)
3. Create [RequirePermission] attribute
4. Add permission caching (15-min sliding)
5. Apply to AuthController endpoints

Sample functions to register:
- "Stock.View"
- "Stock.Adjust"
- "Order.Create"
- "Order.Cancel"
- "User.Manage"

CRUD types: View, Add, Edit, Delete, Approve

Show me usage example:

[RequirePermission("Stock.View")]
public async Task<IActionResult> Index() { ... }
```

---

## 📅 Day 4 (Thursday): First Working Pages

### Morning: Layouts (Office + Mobile)

**Prompt:**

```
Set up the dual layout system per docs/01_Master_Design.md.

Reference files I'll provide:
- _OfficeLayout.cshtml (Telerik + BS5 + htmx)
- _MobileLayout.cshtml (BS5 + Alpine + htmx)

Tasks:
1. Place layouts in Views/Shared/
2. Set default layout per area:
   - /Office/* → _OfficeLayout
   - /Mobile/* → _MobileLayout
3. Add ViewStart.cshtml in each area
4. Set up wwwroot structure:
   - css/site.css (custom)
   - js/site.js
5. Add htmx and Alpine.js via CDN or local
6. Configure Telerik license
7. Create empty dashboard pages:
   - /Office/Dashboard
   - /Mobile/Picker (placeholder)
   - /Mobile/Packer (placeholder)
   - /Mobile/Receiver (placeholder)

Show me one working office page + one mobile page with proper layouts.
```

### Afternoon: PWA Manifests

**Prompt:**

```
Set up 3 PWA manifests for mobile workflows.

Reference: docs/01_Master_Design.md "Mobile Strategy: Hybrid PWA"

Tasks:
1. Create wwwroot/manifests/ directory
2. Create 3 manifest files:
   - picker.json (icon, name, start_url)
   - packer.json
   - receiver.json
3. Add service worker (basic, online-only - per design)
4. Add manifest links to mobile pages
5. Add install prompt
6. Test on actual mobile device

Goal: Users can "Add to home screen" and get separate icons for each role.
```

---

## 📅 Day 5 (Friday): Master Data + Demo

### Morning: First Master CRUD

**Prompt:**

```
Implement Warehouse master data CRUD as the first complete vertical slice.

Reference: docs/02_Database_Schema.md → master.Warehouses

Tasks:
1. Domain entity: Warehouse.cs in WMS.Domain
2. Repository: WarehouseRepository in WMS.DAL
   - GetByIdAsync, GetAllAsync, AddAsync, UpdateAsync, DeleteAsync (soft)
3. Service: WarehouseService in WMS.BLL
   - With validation
4. Controller: WarehousesController
   - List, Details, Create, Edit, Deactivate
5. Razor Views with Telerik Grid
6. Permission checks: "Warehouse.View", "Warehouse.Edit"
7. Audit logging
8. Unit tests for service
9. Integration test for full CRUD flow

Important:
- Soft delete (IsActive flag)
- Multi-tenant filter (always)
- Use Telerik Kendo Grid for list view
- Form with validation

This is the TEMPLATE for all other master CRUD. Make it clean.
```

### Afternoon: Integration Testing + Demo Prep

**Tasks:**

```
1. Run full integration test suite
2. Test on actual machine (not just dev)
3. Prepare demo:
   - Login → tenant select → warehouse select
   - Navigate to dashboard
   - Open warehouses list
   - Add a new warehouse
   - Edit it
   - Soft delete
4. Document any bugs in GitHub issues
5. Update CLAUDE.md "Current Phase" to "Week 2"
6. Commit + push
```

---

## 🎯 End of Week 1 Checklist

```
☐ Solution builds successfully
☐ Migrations run + rollback works
☐ Login flow working (3-step + smart skip)
☐ Permission system enforced
☐ First master CRUD complete (Warehouses)
☐ Office + Mobile layouts working
☐ 3 PWA manifests installed
☐ Unit tests passing (>80% on new code)
☐ Integration test for login + CRUD
☐ Demo recorded for stakeholders
☐ CLAUDE.md updated
☐ Documentation up to date
```

---

## 🚨 Common Week 1 Pitfalls

### Pitfall 1: Trying to Build Too Much

❌ "Let me build all master data this week"
✅ "Let me build ONE master CRUD perfectly as the template"

### Pitfall 2: Skipping Tests

❌ "I'll add tests later"
✅ "Test alongside implementation"

### Pitfall 3: Ignoring Layouts Early

❌ "I'll style it later"
✅ "Get layouts right Day 4 — affects everything"

### Pitfall 4: Big Migrations

❌ "One huge migration with all 80 tables"
✅ "Many small migrations, one per logical group"

### Pitfall 5: Not Using CLAUDE.md

❌ Forgetting to update "Current Phase"
✅ Update at start of every day

---

## 🎯 Week 2 Preview

After Week 1 success, Week 2 builds:

```
Day 1-2: Master data layer 2 (Locations, Zones, Pack Stations)
Day 3:   Master data layer 3 (Owners, Customers, Suppliers)
Day 4:   Master data layer 4 (Products, UoM, Categories)
Day 5:   Bulk import framework + demo
```

---

## 📞 When to Ask for Help

**Ask Claude (in chat):**
- Architecture decisions
- "Should I use pattern X or Y?"
- Trade-off analysis
- Code review

**Use Claude Code:**
- Implementation
- Following established patterns
- Generating boilerplate
- Writing tests

**Ask team/community:**
- Tool-specific issues (Telerik, SQL Server)
- Environment problems
- Production incidents

---

**Good luck with Week 1! 🚀**
