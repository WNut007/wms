# WMS Quick Reference Cheatsheet

**สรุปการตัดสินใจสำคัญทั้งหมดในเอกสารเดียว**

---

## 🎯 30-Second Summary

```
ระบบ:    WMS for B2B + B2C marketplace + 3PL/SaaS
Stack:   .NET Core MVC + Dapper + SQL Server
            + Telerik (office) / BS5+Alpine+htmx (mobile)
            + FastReport + Hangfire + SignalR
Multi-tenant:  DB per tenant
Volume:  5,000+ B2C orders/day target
Build:   4-5 months (1-3 devs + Claude Code)
Tables:  ~80+ across 12 schemas
Functions: ~680
Layers:  5 (Strategic/Tactical/Execution/Commercial/Analytical)
```

---

## 🏗️ Architecture Decisions Cheatsheet

| Decision | Choice | Why |
|----------|--------|-----|
| Tenant model | DB per tenant | Isolation, easier scaling |
| Login flow | 3-step (auth → tenant → warehouse) | Security, smart routing |
| Mobile pattern | PWA hybrid (1 codebase, 3 manifests) | Reuse, maintainability |
| Pattern | MPA (Razor) + htmx | Avoid SPA complexity |
| Real-time | SignalR + Redis backplane | Scale-ready |
| Stock PK | (LocationId, ProductId, LotId, PalletId, OwnerId, UomId) | Owner concept critical |
| Order model | 3 dimensions (Type/Source/Channel) | Capture all real scenarios |
| Wave: B2B | Discrete (1 order = 1 trip) | Match business rhythm |
| Wave: B2C | Batch (30 orders/wave) | Volume efficiency |
| Pick scan | 4-tier (Loc/Pallet/SKU/Lot) | Accuracy + speed |
| Pack mode | Dual (ScanEach vs Scan+Qty), per product | Flexibility |
| Carrier mode | Deferred default | Adapt to phase 1-3 reality |
| Putaway | Hybrid (Hard + Template + Scoring) | Best of all approaches |
| Allocation | Strategy-driven (Push/Pull/JIT) | Configurable per business |
| Rotation | Configurable (FIFO/FEFO/LIFO) | Not hardcoded |
| Reservation | Hybrid (Hard B2C, Soft B2B) | Match business need |
| Cycle count | Web UI generation + Approval | Human-controlled |
| Permissions | Function-CRUD matrix | Granular yet manageable |
| Billing | Activity-based + Aging + Category + Tier | Real 3PL needs |

---

## 🎨 Tech Stack Cheatsheet

```
┌─────────────────────────────────────────┐
│ FRONTEND (Office)                        │
│   ├── Telerik UI for ASP.NET MVC        │
│   ├── Bootstrap 5                       │
│   ├── htmx (HTML over wire)             │
│   └── Alpine.js (light interactivity)   │
├─────────────────────────────────────────┤
│ FRONTEND (Mobile - 3 PWA apps)           │
│   ├── Bootstrap 5                       │
│   ├── htmx                              │
│   ├── Alpine.js                         │
│   └── PWA (manifest + service worker)   │
├─────────────────────────────────────────┤
│ BACKEND                                  │
│   ├── ASP.NET Core MVC                  │
│   ├── Razor Pages                       │
│   ├── Dapper (micro-ORM)                │
│   └── FluentValidation                  │
├─────────────────────────────────────────┤
│ DATA                                     │
│   ├── SQL Server                        │
│   ├── Read replicas (Phase 2+)          │
│   └── Materialized views (analytics)    │
├─────────────────────────────────────────┤
│ INFRASTRUCTURE                           │
│   ├── Hangfire (background jobs)        │
│   ├── SignalR (real-time)               │
│   ├── Redis (cache + SignalR backplane) │
│   └── FastReport.WEB (reports)          │
├─────────────────────────────────────────┤
│ INTEGRATIONS                             │
│   ├── Marketplace plugins (Shopee...)   │
│   ├── Carrier plugins (Flash...)        │
│   └── ERP push (CSV/XML/EDI)            │
└─────────────────────────────────────────┘
```

---

## 📊 Module Dependency Graph

```
                    [Auth + Tenant]
                          │
                          ▼
                    [Master Data]
                    (6 layers)
                          │
        ┌─────────────────┼─────────────────┐
        ▼                 ▼                 ▼
   [Receiving]      [Order Mgmt]      [User/Perm]
   [Putaway]            │                   │
        │                ▼                  │
        │           [Wave Engine]           │
        │                │                  │
        ▼                ▼                  │
    [Stock]          [Pick]                 │
    (Owner-aware)        │                  │
        │                ▼                  │
        │           [Pack + Ship]           │
        │                │                  │
        └────────┬───────┘                  │
                 ▼                          │
            [Returns]                       │
                 │                          │
                 ▼                          │
            [Cycle Count]                   │
                 │                          │
                 └──────────────┬───────────┘
                                ▼
                         [Activity Logger]
                                │
                                ▼
                         [Billing Engine]
                                │
                                ▼
                         [Reports/Dashboards]
```

---

## 🗄️ Schema Quick Map

```
Master DB:
└── system tables (Tenants, UserMap, SuperAdmins)

Tenant DB:
├── master    [layout, entities, products, config]
├── inventory [stock, lots, pallets, ATP]
├── inbound   [POs, receivings, putaway, container ops]
├── outbound  [orders, waves, picks, packs, packages]
├── marketplace [webhooks, review queue]
├── returns   [RMAs, inspections, history]
├── counts    [cycle count batches, tasks]
├── security  [users, roles, functions, audit]
├── vmi       [pending settlements, batches]
├── billing   [rate cards, activities, invoices]
├── forecast  [demand forecasts]
└── analytics [velocity, summaries, performance]
```

---

## 🎯 Key Configuration Patterns

### Strategy Resolution (most specific wins)

```
Priority Order:
1. Product-specific
2. Customer-specific
3. Category-specific
4. Channel-specific
5. Warehouse-specific
6. System default

Used for:
- Allocation strategies
- Rotation methods (FIFO/FEFO/LIFO)
- Pricing
- Replenishment rules
```

### Permission Resolution (OR aggregation)

```
User has multiple roles → permissions OR'd together
+ Direct grants/denies override
+ Time-bounded validity
+ Context constraints (warehouse, approval limit)
```

### Approval Workflow (authority routing)

```
Adjustment value:
- < 1,000: auto-approve
- 1,000 - 10,000: supervisor
- 10,000 - 100,000: manager
- > 100,000: GM + audit
```

---

## 🚀 Phasing Cheatsheet

```
PHASE 1 (Months 1-3): MVP B2B
├── Foundation + Master Data
├── Receiving + Putaway
├── Stock with Owner
├── Order Management
├── Pick + Pack + Ship
├── Returns
└── Manual carrier (no API)

PHASE 2 (Month 4): B2C Launch
├── Marketplace adapters
├── Carrier APIs
├── Pack video
├── Wave engine optimization
├── Customer portal (basic)
└── B2C return workflow

PHASE 3 (Month 5): 3PL Ready
├── VMI workflow
├── Billing engine (activity-based)
├── Aging + Category rates
├── ATP + Strategic allocation
├── Customer billing portal
└── Reports + Dashboards

PHASE 4 (Post-launch): Enterprise
├── ERP integration
├── Demand forecasting
├── Supplier portal
├── BI integration
├── Multi-warehouse network
├── 3D Warehouse Monitor
│   ├── 4a: 2D SVG floor plan (5 days)
│   ├── 4b: Three.js 3D viewer (2 weeks)
│   └── 4c: Live operations twin (3+ weeks)
└── AI/ML features
```

---

## 🚨 Critical Edge Cases (Don't Forget!)

```
1. ✋ Race conditions: 2 pickers same item
2. ✋ Lot commingle: configurable, not auto-allow
3. ✋ Owner segregation: VMI can't be sold as Owned
4. ✋ Concurrent putaway: capacity reservation
5. ✋ B2C cancel: race with pick start
6. ✋ Carrier API failure: auto-fallback to deferred
7. ✋ Marketplace SKU mismatch: review queue
8. ✋ Cycle count variance: mandatory recount different counter
9. ✋ Rate change mid-period: snapshot at receipt
10. ✋ Aging across rate updates: locked-in
11. ✋ Returns: preserve original owner
12. ✋ Stock movements during count: blackout window
13. ✋ Pack video webcam fail: don't block pack
14. ✋ JIT allocation race: safety buffer
15. ✋ Multi-zone order: consolidation needed
```

---

## 📋 Open Concerns Checklist

```
□ Concurrency stress testing (5K orders/day)
□ Time zones (multi-region 3PL)
□ Multi-currency support
□ Table partitioning (StockMovements 18M+/year)
□ B2B pack workflow (BOL, pallet build)
□ Holiday calendar (SLA calc)
□ PDPA compliance (PII handling)
□ Plugin lifecycle management
□ Notification system (unified)
□ SignalR scaling (Redis backplane)
□ Deployment strategy (blue-green)
□ Disaster recovery plan
□ Data warehouse / BI integration
```

---

## 🎯 Success Criteria

### Technical
- 5,000 orders/day at 99.5% uptime
- Pick accuracy ≥ 99%
- Pack accuracy ≥ 99.5%
- Inventory accuracy ≥ 99%
- Mobile UI: 1-handed picker workflow

### Business  
- B2B + B2C launched simultaneously
- 3PL feature ready for first external customer (month 6)
- Customer self-service for billing
- No revenue leakage (every activity billed)
- SLA performance ≥ 95%

### Operational
- New tenant onboarding ≤ 1 week
- New user training ≤ 4 hours
- Standard reports ≤ 2 second load
- Critical alerts ≤ 1 minute response

---

## 📞 Reference Docs

- **01_WMS_Master_Design.md** — Comprehensive overview
- **02_WMS_Database_Schema.md** — Complete schema reference
- **03_WMS_Implementation_Roadmap.md** — Week-by-week plan
- **04_WMS_Quick_Reference.md** — This file

### External References Studied:
1. Business Central allocation strategies (keytogoodcode.com)
2. TrueCommerce inventory allocation best practices
3. Symphony WMS Billing (boonsoftware.com)
4. SAP EWM 3D Warehouse Monitor (community.sap.com)

---

**End of Quick Reference**

*This document represents 100+ design decisions across 5 system layers, ~700 functions, ~83 tables, distilled from extensive design conversation. Includes 3D Warehouse Monitor (Phase 4 deferred) with schema foundation in Phase 1.*
