# WMS Master Design Document

**ระบบ Warehouse Management System สำหรับ B2B + B2C Marketplace + 3PL/SaaS**

---

## 📋 Project Overview

### Business Context
- **เดิม**: WMS ระบบเก่า .NET MVC + Dapper + Kendo (N-Tier) — ใช้งาน B2B ปกติ
- **ใหม่**: Rebuild เพื่อ scale up + B2C marketplace + 3PL/SaaS multi-tenant
- **Volume target**: B2B ปกติ + B2C 5,000+ orders/day + อนาคตเป็น 3PL provider

### Tech Stack
- **Backend**: .NET Core MVC + Dapper + SQL Server
- **Frontend Office**: Telerik UI for ASP.NET MVC + Bootstrap 5 + htmx + Alpine.js
- **Frontend Mobile**: Bootstrap 5 + Alpine.js + htmx + PWA (no Kendo)
- **Reporting**: FastReport.WEB
- **Pattern**: Multi-page Razor (MPA)
- **Architecture**: Enhanced N-Tier (Web/BLL/Plugins/Jobs/DAL) + Hangfire
- **Real-time**: SignalR (with Redis backplane for scale)

### Team & Timeline
- 1-3 developers + Claude Code as accelerator
- **4-5 months** realistic timeline
- Build everything in-house

---

## 🏗️ Architecture: 5 Layers

```
═══════════════════════════════════════════
1. STRATEGIC LAYER
   ATP, Item Stratification, Customer Tier
   Demand Forecasting, Multi-location
   "Where to position inventory"
═══════════════════════════════════════════

═══════════════════════════════════════════
2. TACTICAL LAYER  
   Allocation strategies (Push/Pull/JIT)
   Reservation, Wave engine
   "How to commit inventory to demand"
═══════════════════════════════════════════

═══════════════════════════════════════════
3. EXECUTION LAYER
   Receive, Putaway (Template + Scoring)
   Pick (4-tier scan), Pack (Dual mode), Ship
   Returns, Cycle count
   "Physical work in warehouse"
═══════════════════════════════════════════

═══════════════════════════════════════════
4. COMMERCIAL LAYER
   Rate cards (Aging + Tier + Category)
   Activity logging, Billing engine
   Invoice generation, ERP integration
   "How to monetize the work"
═══════════════════════════════════════════

═══════════════════════════════════════════
5. ANALYTICAL LAYER
   Reports (~30), Dashboards (4 audiences)
   KPIs, Profitability analysis
   "How to measure and improve"
═══════════════════════════════════════════
```

---

## 🎯 Major Architectural Decisions

### 1. Multi-Tenant: DB per Tenant
- Master DB + Tenant DB per customer
- 1 SQL server initially → multi-server later
- Encrypted connection strings
- 3-step Login (auth → tenant → warehouse) with smart skip
- Pre-auth token (5-min) between auth and tenant selection

### 2. Mobile Strategy: Hybrid PWA
- 3 Workflows: Receiver + Picker + Packer
- Hardware: Zebra/Honeywell scanners (keyboard wedge mode)
- WiFi excellent throughout warehouse → simple service worker
- 1 codebase, 3 manifests, 3 home icons, shared session
- NOT offline-first (WiFi reliable)

### 3. Owner Concept (VMI/3PL Critical)
- master.Owners table: Self/Supplier/VMI/Customer3PL types
- master.ProductOwners many-to-many (same SKU multi-source)
- Stock PK includes OwnerId
- Lots scoped by Owner
- VMI settlement workflow

### 4. Order Management: 3 Dimensions
- **OrderType**: Sales/Transfer/Return/InternalUse
- **OrderSource**: NORMAL/EDI/WEB/PORTAL/PHONE/EMAIL/IMPORT/API
- **Channel**: Manual/Shopee/Lazada/TikTok/B2B-Portal
- Hybrid table inheritance (Orders + type-specific details)
- 11+ state machine with split + backorder paths

### 5. Wave Engine: Hybrid B2B + B2C
- B2B: Discrete (1 order = 1 trip)
- B2C: Batch (30 orders/wave)
- Container-based (Pallet/Box-L/M/S/Tote)
- Pick path: Configurable (Sort/Snake/TSP) strategy pattern
- Time + Trigger based generation (Hangfire)
- Express priority queue

### 6. Picker: 4-tier Scan
- Location (always) → Pallet (if PalletId) → SKU (always) → Lot (if Lot/LotAndSerial)
- 8-step UI flow with sequential validation
- Smart auto re-allocation if same product+lot found on alternate pallet
- Always-focused hidden input pattern, vibration feedback, real-time SignalR

### 7. Packer: Dual Mode + Pack Video
- Pack station = desktop/tablet (not handheld)
- ScanEach (high-value) vs Scan+Qty (bulk FMCG), per-product config
- Box suggestion algorithm (smallest fit + 20% buffer)
- Scale integration for weight verification
- Pack video: 2-level control (per-station + per-channel), 10-day retention

### 8. Carrier: Plugin + Deferred Default
- Plugin pattern (Flash/Kerry/J&T/ThaiPost)
- 2-mode: Eager (API at pack) vs Deferred (label later) — **Deferred default**
- Adaptive UI: Hide carrier selection if no carriers configured
- Manifest workflow: Build → Seal (bulk API) → Handover (driver sign)

### 9. Putaway: Hybrid (Template + Scoring) [BC pattern]
- Hard constraints + Putaway Template (cascading rules) + Soft scoring
- Bin capacity (volume + weight, 4 policies)
- Bin Rank field (numeric, lower = higher priority)
- BinContents table: Fixed/Floating bins
- Per-bin per-product Min/Max
- Lot Commingle Configurable: System default ALLOW, Zone/Product override

### 10. Strategic Allocation [TrueCommerce pattern]
- Push/Pull/JIT/Hybrid (configurable per product/customer/category/channel)
- Resolution hierarchy: Product > Customer > Category > Channel > Default
- Configurable rotation (FIFO/FEFO/LIFO)
- ATP timeline-based with SupplyPipeline + DemandPipeline
- Item Stratification: multi-axis (Velocity/Margin/Strategic/LeadTime/ShelfLife/Variability)
- Customer Tier system (Tier1-4 with allocation priority)
- Shelf life-aware allocation

### 11. Returns / RMA: 10-state Workflow
- Requested → Approved → InTransit → Received → Inspecting → Restocked → Refunded → Completed
- 3 types: Refund/Exchange/StoreCredit
- 4 return methods: CustomerShip/CarrierPickup/DropOff/CarrierLabel
- 5 dispositions: Restock/Quarantine/Scrap/RTV/Repair
- Marketplace refund webhook + push integration

### 12. Cycle Count: Web-driven + Approval
- ABC strategy (different frequency by velocity)
- 4 modes: Scheduled/Triggered/Targeted/Spot
- Blind count + mandatory recount on variance (different counter)
- Generation: WEB UI by supervisor (NOT auto Hangfire)
- Adjustment: Manual approval workflow
- 4 decisions: Approve/Recount/Reject/Investigate
- Authority routing by adjustment value

### 13. Permissions: Function-CRUD Matrix
- RBAC + Context constraints
- View/Add/Edit/Delete/Approve per function (matrix)
- Special actions per function (configurable JSON)
- Multi-role per user (OR aggregation)
- Time-bounded role validity
- Approval limit by user
- Direct grant/deny override
- Permission caching (15-min sliding)

### 14. 3PL Billing: Activity-based + Symphony patterns
- Aligned to WMS execution
- 6 categories: Storage/Inbound/Outbound/ValueAdded/Returns/Account
- Pricing models: Flat/Tiered/Step/Block
- **Aging Brackets**: rate escalation by age (NEW)
- **Category-based rates**: per product category (NEW)
- Grace Period (free storage entitlement)
- Pricing Determinants (multi-dimensional)
- Container Operations (stuffing/un-stuffing)
- Consolidated Billing + grouping (Service/Product/Order/ASN/Day)
- Dual mode: Direct invoice (SME) OR Push to ERP (Enterprise)
- Calculation/Rounding policies
- Customer self-service billing portal
- Dispute workflow with credit notes

### 15. Reports + Dashboards
- 4 audience-specific dashboards (Operational/Manager/Owner/External)
- ~30 standard reports across 10 categories
- 3-tier strategy: Built-in / Builder / Export API
- Real-time vs scheduled split
- Read replica architecture
- Materialized view pattern
- Multi-tenant isolation

### 16. 3D Warehouse Monitor [PHASE 4 - Post-launch]
- **Status**: Schema ready, implementation deferred to Phase 4
- Inspired by SAP EWM 3D Warehouse Monitor concept
- Web-based interactive 3D visualization (no plugin)
- 5 visualization modes:
  - **Stock Level** (color by occupancy %)
  - **ABC Velocity** (color by velocity class)
  - **Aging** (color by storage age)
  - **Activity Heatmap** (today's pick/putaway activity)
  - **Live Operations** (picker avatars + pick paths)
- Click-to-inspect bin (drill down to details)
- Real-time updates via SignalR
- Implementation phases (post-launch):
  - Phase 4a: 2D SVG floor plan (5 days)
  - Phase 4b: Three.js 3D rendering (2 weeks)
  - Phase 4c: Live operations twin (3+ weeks)
- **Why deferred**: Not critical for B2B/B2C/3PL launch — differentiator for sales demos and advanced operations
- **Data foundation**: X/Y/Z coordinates added to Locations master (Phase 1 schema)

### 17. Inter-warehouse Transfer [PHASE 1 - Required]
- **Status**: Required for Phase 1 (B2B with multi-location)
- 9-state workflow: Draft → Submitted → Approved → Picking → Dispatched → InTransit → Receiving → Received → Closed
- Side paths: Cancelled (before InTransit), Lost (after Dispatched without Received)
- Header + Lines pattern (TransferOrders + TransferOrderLines)
- Status history table for audit trail
- In-transit stock = pseudo-location (between warehouses)
- Owner-aware (preserve OwnerId across transfer)
- Lot-aware (preserve LotId)
- Pick task generation from Transfer (uses normal picker workflow)
- Receiving on arrival (uses normal receiving workflow)
- Loss in transit handling: auto-create Adjustment if QtyDispatched ≠ QtyReceived
- Used for: rebalancing stock, satellite warehouses, returns to main, B2B contract fulfillment

### 18. General Stock Adjustment [PHASE 1 - Required]
- **Status**: Required for Phase 1 (real-world ops support)
- Distinct from Cycle Count adjustments (counts.CountAdjustments stays for that)
- Generic AdjustmentReasons master with categories: Damage/Loss/Found/QC/Manual/Reclassify
- Reason codes drive workflow:
  - RequireApproval (yes/no per reason)
  - RequirePhoto (mandatory evidence?)
  - AuthorityLevel (Supervisor/Manager/GM)
  - IsChargeable (3PL: bill customer for adjustment?)
- 4-state workflow: Pending → (Approved/Rejected) → Applied
- Authority routing by reason + value (similar to Cycle Count adjustments)
- StockAdjustments table tracks: before qty, after qty, delta, reason, photos, approval
- Photo evidence for damage claims
- Billing impact: chargeable adjustments → BillableActivities
- Use cases:
  - Damage in warehouse → Decrease + chargeable
  - Loss during pick → Decrease + investigation
  - Found stock (wrong location discovered) → Increase
  - QC rejection → Reclassify to Quarantine
  - Owner change (Self → VMI) → Reclassify
  - System bug recovery → Manual correction
- Why critical: real warehouses adjust daily, not just during cycle counts

---

## 🗄️ Database Schemas (12 schemas)

```
Master DB (system-level):
├── master.Tenants
├── master.UserTenantMap
├── master.SuperAdmins
├── master.SystemAuditLog
├── master.LoginAttempts
└── master.PreAuthTokens

Tenant DB (per company):
├── master      — physical layout, business entities, products, configuration
├── inventory   — stock, lots, pallets, serials, movements, ATP
├── inbound     — POs, receivings, putaway, container ops
├── outbound    — orders, waves, picks, packs, shipments, packages
├── marketplace — webhook events, review queue
├── returns     — RMAs, inspections, status history
├── counts      — cycle count batches, tasks, adjustments
├── security    — users, roles, functions, permissions, audit
├── vmi         — pending settlements, settlement batches
├── billing     — rate cards, activities, invoices, payments
├── forecast    — demand forecasts
└── analytics   — sales velocity, daily summaries, performance
```

**Total: ~80+ tables**

(See **02_WMS_Database_Schema.md** for complete schema)

---

## 📊 Functions Master List

**~735 functions** across modules:

| Module | Function Count | Priority |
|--------|---------------|----------|
| Multi-tenant + Auth | 25 | ⭐⭐⭐⭐⭐ |
| Master Data | 50 | ⭐⭐⭐⭐⭐ |
| Receiving + Putaway | 40 | ⭐⭐⭐⭐⭐ |
| Inventory + Stock | 45 | ⭐⭐⭐⭐⭐ |
| **Inter-warehouse Transfer** | **15** | **⭐⭐⭐⭐⭐** |
| **Stock Adjustment** | **18** | **⭐⭐⭐⭐⭐** |
| Order Management | 50 | ⭐⭐⭐⭐⭐ |
| Wave Engine | 25 | ⭐⭐⭐⭐⭐ |
| Picker (Mobile) | 30 | ⭐⭐⭐⭐⭐ |
| Pack + Carrier | 45 | ⭐⭐⭐⭐⭐ |
| Marketplace Integration | 30 | ⭐⭐⭐⭐ |
| Returns / RMA | 30 | ⭐⭐⭐⭐ |
| Cycle Count | 25 | ⭐⭐⭐⭐ |
| User / Permission | 30 | ⭐⭐⭐⭐⭐ |
| Strategic Allocation | 30 | ⭐⭐⭐⭐ |
| 3PL Billing | 80 | ⭐⭐⭐⭐⭐ |
| Reports + Dashboards | 40 | ⭐⭐⭐⭐ |
| ATP + Network Inventory | 25 | ⭐⭐⭐⭐ |
| VMI / Owner | 20 | ⭐⭐⭐⭐ |
| 3D Warehouse Monitor | ~22 | ⭐⭐⭐ (Phase 4) |
| Misc/Support | 60 | ⭐⭐⭐ |

---

## 🎯 Key Design Patterns

### Pattern 1: "Configurable, not Hard-coded"
Every business rule is configurable through admin UI:
- Lot commingle rules
- Pack workflow modes
- Carrier behavior
- Permissions matrix
- Cycle count generation
- Approval thresholds
- Pricing models
- Allocation strategies

**Why**: 3PL/SaaS = each customer has different needs

### Pattern 2: "Human-controlled by default, Automation as opt-in"
- Cycle count generation: Web UI (not auto-cron)
- Cycle count adjustment: Approval workflow (not auto-apply)
- Wave creation: Hybrid auto + manual
- Allocation: Manual override available

**Why**: Real warehouses have real people making decisions

### Pattern 3: "Hide what's not relevant"
- Carrier dropdown: hidden if no carriers configured
- Tenant list: only show if user has multiple
- Permission options: filtered by user's role

**Why**: Reduce cognitive load, prevent errors

### Pattern 4: "Authenticate first, expose context after"
- 3-step login flow
- Pre-auth token between steps
- Master tables hidden until authenticated

**Why**: Security through information minimization

### Pattern 5: "Real world has multiple states"
- Phase 1 (no carriers) vs Phase 2/3 (carriers configured)
- Single tenant vs multi-tenant
- Owned vs VMI vs Storage stock

**Why**: System must handle all valid states, not just "happy path"

### Pattern 6: "Cascading rules > Single algorithm"
- Putaway: Template lines tried in order
- Strategy resolution: Specificity hierarchy
- Permissions: Multi-role aggregation

**Why**: Configurable, debuggable, predictable

---

## 🚀 Phasing Strategy

### Phase 1: MVP (Months 1-3)
**Goal**: B2B operational

- Multi-tenant + Login
- Master data setup (5-week implementation)
- Receiving + Putaway (basic)
- Stock management
- **Inter-warehouse Transfer** (multi-location support)
- **General Stock Adjustment** (damage/loss/found)
- Order Management (B2B)
- Pick + Pack workflow
- Basic reports
- Manual carrier (no API)

### Phase 2: B2C + Marketplace (Month 4)
**Goal**: B2C marketplace ready

- Marketplace adapters (Shopee/Lazada/TikTok)
- Carrier integrations (Flash/Kerry/J&T)
- Wave engine optimization
- Pack video
- Customer portal (basic)
- Returns / RMA
- Marketplace stock sync

### Phase 3: 3PL Features (Month 5)
**Goal**: 3PL/SaaS provider ready

- Owner concept (VMI/3PL)
- Billing engine + Rate cards
- Aging brackets
- Activity logging hooks
- Customer billing dashboard
- ATP + Strategic allocation
- Item stratification

### Phase 4: Advanced (Phase 2 of 3PL business)
**Goal**: Enterprise-grade

- ERP integration (push billing)
- Demand forecasting
- Supplier portal (VMI)
- Advanced analytics
- BI integration
- Multi-warehouse
- **3D Warehouse Monitor** (interactive visualization)
  - Phase 4a: 2D SVG floor plan with overlays
  - Phase 4b: Three.js basic 3D viewer
  - Phase 4c: Live operations digital twin
- AI/ML features (smart slotting, demand prediction)

---

## 📋 Self-Review Concerns (Open)

These are concerns identified but not fully resolved:

1. **Concurrency stress testing at 5K orders/day**
   - Need load testing strategy
   - Database connection pool tuning
   - SQL Server isolation levels

2. **Stock reconciliation**
   - Periodic check vs operational
   - Backfill process for discrepancies

3. **Time zones** (assumed Thailand UTC+7)
   - Multi-timezone for international 3PL clients?

4. **Currency** (assumed THB)
   - Multi-currency mentioned in billing
   - Exchange rate handling

5. **Table growth** 
   - StockMovements + BillableActivities ~18M+ rows/year
   - Partitioning strategy needed

6. **B2B pack workflow**
   - Pallet build, BOL different from B2C
   - Need separate workflow design

7. **Holiday calendar / SLA calculation**
   - Working days vs calendar days

8. **PDPA compliance**
   - PII handling
   - Data retention
   - Consent management

9. **Plugin lifecycle**
   - Versioning, configuration
   - Failure isolation

10. **Notification system**
    - Unified across modules

11. **SignalR scaling**
    - Redis backplane for multi-instance

12. **Deployment strategy**
    - Blue-green / rolling

13. **Disaster recovery**
    - Backup, replication, failover

14. **Data warehouse / BI integration**
    - Phase 2-3 consideration

---

## 📚 Reference Articles Studied

1. **Business Central Allocation Strategies** (keytogoodcode.com)
   - Putaway templates, Fixed/Floating bins, Bin rank, Capacity policies

2. **TrueCommerce Inventory Allocation Best Practices**
   - ATP, Item Stratification, Customer Tier, Shelf life-aware, Multi-location, Demand Forecasting

3. **Symphony WMS Billing** (boonsoftware.com)
   - Grace Period, Step/Block Pricing, Pricing Determinants, Container Operations, Consolidated Billing, Dual Mode (Direct/ERP), Calculation/Rounding policies

4. **SAP EWM 3D Warehouse Monitor** (community.sap.com - Eric Schulz, SAP Tokyo)
   - Web-based 3D visualization, X/Y/Z bin coordinates, 5 visualization modes, real-time updates
   - Implementation deferred to Phase 4, but schema X/Y/Z added to Locations in Phase 1

---

## 🎯 Success Criteria

### Technical
- [ ] Handle 5,000 orders/day at 99.5% uptime
- [ ] Pick accuracy ≥99%
- [ ] Pack accuracy ≥99.5%
- [ ] Inventory accuracy ≥99% (cycle count results)
- [ ] Mobile UI: 1-handed picker workflow
- [ ] Multi-tenant isolation (security verified)

### Business
- [ ] B2B + B2C launched simultaneously
- [ ] 3PL feature ready for first external customer (month 6)
- [ ] Customer self-service for billing
- [ ] No revenue leakage (every activity billed)
- [ ] SLA performance ≥95%

### Operational
- [ ] New tenant onboarding ≤1 week
- [ ] New user training ≤4 hours
- [ ] Standard reports ≤2 second load
- [ ] Critical alerts ≤1 minute response

---

**End of Master Design Document**
