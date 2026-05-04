# WMS Implementation Roadmap

**ระยะเวลา**: 4-5 เดือน (16-20 สัปดาห์)
**Team**: 1-3 developers + Claude Code
**Goal**: Launch B2B + B2C simultaneously, 3PL/Billing in Phase 3

---

## 🎯 Strategic Approach

### Build Philosophy
1. **Foundation First** — Master data + Auth ก่อนทุกอย่าง
2. **Vertical Slices** — End-to-end workflow แต่ละ module
3. **B2B Before B2C** — B2B simpler, B2C complexity สูงกว่า
4. **Defer Complexity** — Carrier APIs, marketplace integration ไป Phase 2
5. **Billing Last** — เพราะต้องมี data ก่อน
6. **Test Continuously** — Integration tests for critical paths

### Risk Mitigation
- Week 1-2: Spike on tech stack (Telerik, htmx, SignalR)
- Always have working B2B path before adding B2C
- Use feature flags for incomplete features
- Daily/weekly demos to surface issues early

---

## 📅 Month 1: Foundation (Weeks 1-4)

### Week 1: Project Setup & Architecture

```
✅ Project structure (.NET Core MVC + Dapper)
   ├── Web project (Razor Pages)
   ├── BLL project (services)
   ├── DAL project (Dapper repositories)
   ├── Plugins project (carrier, marketplace adapters)
   ├── Jobs project (Hangfire)
   └── Common/Domain project

✅ Database setup
   ├── Master DB schema
   ├── Tenant DB template
   ├── Migration framework (FluentMigrator or DbUp)
   └── Seed data scripts

✅ Authentication
   ├── ASP.NET Core Identity (or custom)
   ├── 3-step login flow
   ├── Pre-auth tokens
   └── Tenant routing middleware

✅ Layouts (per WMS_Frontend_Action_Plan)
   ├── _OfficeLayout.cshtml (Telerik + BS5 + htmx)
   ├── _MobileLayout.cshtml (BS5 + Alpine + htmx)
   └── PWA manifests (3 variants)

Deliverables:
- Working login flow
- Tenant routing
- Base layouts
- "Hello World" pages on all 3 mobile workflows
```

### Week 2: User Management + Permissions

```
✅ User CRUD
✅ Role CRUD
✅ Function master + RoleFunctionPermissions matrix
✅ Multi-role support
✅ User-Tenant routing
✅ Permission caching (15-min sliding)
✅ Audit log foundation
✅ Mobile PIN login

Deliverables:
- Login → tenant select → warehouse select
- Admin can manage users, roles, permissions
- Permission enforcement middleware
```

### Week 3: Master Data (Layer 1-2)

```
✅ Warehouses + Docks
✅ Zones + Locations (with X/Y/Z coords + Aisle/Bay/Level)
✅ Pattern-based location generator (auto-fill coords)
✅ Pack stations
✅ Container types + Box types
✅ System settings
✅ Holiday calendar

✅ Bulk import framework (CSV/Excel)
✅ Soft delete pattern
✅ Master data audit log
✅ Effective dating (where needed)

Deliverables:
- Tenant onboarding wizard
- All physical layout entities CRUD
- Import/export working
```

### Week 4: Master Data (Layer 3-4)

```
✅ Owners (Self/Supplier/VMI/Customer3PL)
✅ Customers (B2B + B2C placeholder)
✅ Customer Tier system
✅ Channels + Order sources
✅ Carriers (manual setup, no API)
✅ Marketplace placeholders

✅ Product Categories (hierarchical)
✅ Units of Measure + Conversions
✅ Products + Barcodes + Packing configs
✅ ProductOwners many-to-many
✅ Item Stratification (basic ABC)

Deliverables:
- Complete master data ready
- Sample tenant with test data
- Demo: end-to-end "create tenant" → "ready to operate"
```

---

## 📅 Month 2: Core Operations (Weeks 5-8)

### Week 5: Receiving + Putaway

```
✅ Purchase Orders (with Owner)
✅ Receiving Headers + Lines
✅ Pallet creation + numbering
✅ Lot creation (Hybrid: supplier/auto)
✅ Mobile receiver workflow (4-phase)
✅ Continuous Putaway pattern
✅ Active Pallet UI

✅ Putaway Templates (header + lines)
✅ BinContents (Fixed/Floating)
✅ Putaway algorithm (Hybrid: Template + Scoring)
✅ Bin capacity check (volume + weight)
✅ Bin rank prioritization
✅ Lot commingle rules

Deliverables:
- Complete inbound flow B2B
- Mobile receiver app working
- Putaway suggestions accurate
```

### Week 6: Inventory + Stock

```
✅ Stock CRUD with Owner concept
✅ Stock movements (full audit)
✅ Stock reservations (Hard/Soft)
✅ Pallet movements
✅ Lot tracking
✅ Serial tracking
✅ Catch weight handling

✅ Inventory snapshots
✅ Stock by Owner separation
✅ ATP foundation (SupplyPipeline + DemandPipeline)
✅ Multi-location stock view

Deliverables:
- Stock state correct after all operations
- Reservations working B2B + B2C
- Audit trail complete
```

### Week 7: Order Management (B2B)

```
✅ Order CRUD (3 dimensions: Type/Source/Channel)
✅ Order Lines
✅ SalesOrderDetails (B2B specific)
✅ Order state machine (11+ states)
✅ Manual order entry UI
✅ Order import (B2B portal)
✅ Order status history

✅ Allocation logic (with strategy resolver)
✅ Allocation approach: Push/Pull/JIT/Hybrid
✅ Rotation method: FIFO/FEFO/LIFO (configurable)
✅ Backorder handling
✅ Cancel order flow

Deliverables:
- B2B order entry working
- Auto allocation working
- Status transitions tested
```

### Week 8: Wave Engine + Pick (B2B Discrete)

```
✅ Wave creation (Discrete for B2B)
✅ Wave assignment (manual + auto)
✅ Pick path generator
✅ Pick task creation

✅ Mobile picker UI (8-step flow)
✅ 4-tier scan validation
✅ Always-focused hidden input
✅ SignalR push notifications
✅ Smart re-allocation
✅ Short pick handling
✅ Pick completion → ready for pack

Deliverables:
- Discrete pick workflow B2B
- Mobile picker app working
- SignalR real-time updates
```

---

## 📅 Month 3: B2C + Marketplace (Weeks 9-12)

### Week 9: Pack + Ship (B2B + Basic Carrier)

```
✅ Pack station UI (desktop)
✅ Container scan
✅ Pack workflow (Dual mode: ScanEach + Scan+Qty)
✅ Box suggestion algorithm
✅ Scale integration (weight verification)

✅ Pack video (basic, MediaRecorder)
✅ Per-station + per-channel policy
✅ 10-day retention default
✅ PDPA audit log

✅ Manual carrier (no API)
✅ Manual tracking entry
✅ Manifest workflow (Build → Seal → Handover)
✅ Driver signature capture

Deliverables:
- Complete B2B fulfillment end-to-end
- Pack videos recording
- Manual ship workflow working
```

### Week 10: B2C Foundations + Marketplace

```
✅ B2C order management
✅ Marketplace adapter pattern (plugin)
✅ Webhook receiver framework
✅ Shopee adapter (full)
✅ Order state machine for B2C

✅ Stock sync to marketplace
✅ Safety stock buffer (10%)
✅ MarketplaceSkuMappings
✅ Review queue (mismatch resolution)

✅ Wave engine (Batch for B2C)
✅ Wave assignment B2C (auto, time-based)

Deliverables:
- Shopee orders flowing in
- B2C wave creation working
- Stock sync working
```

### Week 11: Lazada + TikTok + Carriers

```
✅ Lazada adapter
✅ TikTok adapter
✅ Carrier adapters (Plugin)
   ├── Flash Express
   ├── Kerry Express
   ├── J&T Express
   └── ThaiPost
✅ Carrier integration (Deferred mode default)
✅ Carrier health checks
✅ Auto-fallback

✅ Carrier coverage rules
✅ Carrier rates
✅ Adaptive carrier UI

Deliverables:
- All 3 marketplaces working
- All 4 carriers working
- Bulk shipping label generation
```

### Week 12: Returns + Polish

```
✅ Returns / RMA workflow (10 states)
✅ Marketplace refund webhook
✅ Inspection workflow with photos
✅ 5 disposition types
✅ Restock with original lot/serial
✅ Marketplace refund push

✅ Customer notifications (email)
✅ Performance optimization
✅ Bug fixes
✅ B2C launch readiness review

Deliverables:
- B2C ready for launch
- Returns end-to-end
- All tests passing
```

---

## 📅 Month 4: 3PL/Billing Features (Weeks 13-16)

### Week 13: Billing Foundation

```
✅ Rate Cards master + lines
✅ RateCardTiers
✅ AgingBrackets (NEW)
✅ RateCardCategoryRates (NEW)
✅ RateCardCategoryTiers (NEW)
✅ PricingConditions (multi-dimensional)
✅ CalculationPolicies

✅ Billable activity model
✅ BillableActivities table
✅ Activity logger framework

Deliverables:
- Rate card admin UI
- All rate types configurable
```

### Week 14: Activity Logging + Storage Snapshots

```
✅ Auto-logging hooks in operations:
   ├── Receiving → log inbound activities
   ├── Putaway → log putaway activities
   ├── Picking → log handling activities
   ├── Packing → log pack activities
   ├── Shipping → log shipping activities
   ├── Returns → log return activities
   └── Cycle count → log count activities

✅ Daily storage snapshot job (Hangfire)
✅ Grace period handling
✅ Aging-aware billing calculation
✅ Container operations (Stuffing/Un-stuffing)

Deliverables:
- Every operation generates billing events
- Storage snapshot running daily
- Aging calculation accurate
```

### Week 15: Invoice Generation + Customer Portal

```
✅ Invoice generation engine
✅ Period-based aggregation
✅ Invoice grouping (Service/Product/ASN/Order/Day)
✅ Consolidated billing
✅ Min charge enforcement
✅ Tier calculation (Marginal + Step)
✅ Pro-rata calculation
✅ Surcharge engine

✅ Invoice PDF generation (FastReport)
✅ Email distribution
✅ Customer billing dashboard
✅ Real-time activity log drill-down
✅ Invoice payment tracking

Deliverables:
- First invoice generated successfully
- Customer can see real-time charges
- PDF + email working
```

### Week 16: Strategic Allocation + ATP

```
✅ ATP calculation engine
✅ ATP timeline visualization
✅ CanPromise() API
✅ Item stratification (multi-axis)
✅ Stratification recalculation job

✅ Customer tier allocation priority
✅ Shelf life-aware allocation
✅ Inter-warehouse transfer orders
✅ Replenishment rules + tasks
✅ Cross-dock rules

Deliverables:
- ATP API working
- Strategic allocation overrides applied
- Replenishment automation
```

---

## 📅 Month 5: Cycle Count + Reports + Launch (Weeks 17-20)

### Week 17: Cycle Count

```
✅ Cycle count batches
✅ Count tasks (mobile)
✅ Count details
✅ Blind count UX
✅ Recount on variance
✅ Count adjustments

✅ Approval workflow (4 decisions)
✅ Authority routing by value
✅ Variance rules + alerts
✅ ABC strategy frequency

Deliverables:
- Cycle count complete
- Inventory accuracy reports
```

### Week 18: Reports + Dashboards

```
✅ 4 dashboards:
   ├── Operational (Picker mobile, Pack station)
   ├── Manager (real-time KPIs)
   ├── Business owner
   └── Customer portal

✅ 30 standard reports:
   ├── Inventory (stock, movement, aging, slow-mover)
   ├── Order fulfillment (daily summary, SLA, cancel)
   ├── Picker/Wave (productivity, completion)
   ├── Pack/Ship (productivity, carrier perf)
   ├── Receiving (accuracy, putaway)
   ├── Returns (rate, reasons)
   ├── Financial (VMI settlement, billing, profitability)
   ├── Audit (user activity, master changes)
   ├── Customer (top, repeat, geographic)
   └── Performance (KPI, trends)

✅ FastReport.WEB templates
✅ Export PDF/Excel
✅ Schedule report email
✅ Read replica setup
✅ Materialized views

Deliverables:
- All dashboards live
- Critical reports working
```

### Week 19: VMI + Polish

```
✅ VMI pending settlements
✅ Settlement batches
✅ Settlement invoice generation
✅ Returns preserve owner
✅ Owner-segmented reports
✅ Supplier portal (Phase 1)

✅ Bug fixes
✅ Performance tuning
✅ Load testing (5K orders/day target)
✅ Security audit
✅ Documentation
✅ Training materials

Deliverables:
- VMI workflow complete
- Performance targets met
- Documentation ready
```

### Week 20: UAT + Launch

```
✅ User acceptance testing
✅ Stakeholder demo
✅ Production deployment prep
✅ Data migration scripts
✅ Rollback plan
✅ Monitoring + alerting setup
✅ Support runbook

✅ Soft launch (1 customer)
✅ Issue resolution
✅ Hard launch B2B + B2C
✅ Hypercare

Deliverables:
- System live in production
- Active support
- Phase 2 planning
```

---

## 📅 Phase 4: Post-Launch Enhancements (Months 6+)

After successful production launch, these features add differentiation:

### 4a: ERP/BI Integration (Month 6)

```
✅ ERP push integration (CSV/XML/EDI810)
✅ Accounting software integration
✅ Power BI / Tableau connectors
✅ Data warehouse setup
✅ Materialized analytics views
```

### 4b: 3D Warehouse Monitor (Month 7-8)

```
✅ 4b.1: 2D SVG Floor Plan (5 days)
   ├── Top-down view using X/Y coordinates
   ├── 5 visualization modes (occupancy/velocity/aging/heatmap/live)
   ├── Click-to-inspect bins
   ├── Real-time SignalR updates
   └── Picker position markers

✅ 4b.2: Three.js 3D Renderer (2 weeks)
   ├── Interactive 3D scene with orbit controls
   ├── InstancedMesh for 5000+ bins
   ├── Lighting + shading
   ├── Rack levels (vertical visualization)
   ├── Static elements (walls, columns, doors)
   ├── Bin contents inspector panel
   └── Coordinate setup wizard

✅ 4b.3: Live Operations Twin (3+ weeks)
   ├── Picker avatars moving in real-time
   ├── Animated pick paths (glowing lines)
   ├── Activity heatmap overlay
   ├── Time-lapse replay (last 24 hours)
   ├── Multi-warehouse view
   └── Slotting optimization recommendations
```

### 4c: AI/ML Features (Month 9+)

```
✅ Demand forecasting (ML models)
✅ Smart slotting recommendations
✅ Anomaly detection (theft/damage)
✅ Predictive maintenance (equipment)
✅ Auto-replenishment optimization
```

### 4d: Advanced 3PL (Ongoing)

```
✅ Supplier portal (VMI self-service)
✅ Customer-specific custom reports
✅ White-label customer portal
✅ Multi-warehouse network optimization
✅ Multi-currency support
✅ International shipping
```

---

## 📋 Critical Path Items

These cannot be parallelized — must be done in order:

```
1. Auth + Tenant → everything else
2. Master data → operations
3. Stock model → all inventory operations
4. Order model → wave/pick/pack
5. Pick → Pack (data flow)
6. Activity logging → billing
7. Daily snapshot → invoice
```

---

## 🚦 Phase Gates

### Gate 1: End of Month 1
**Foundation Complete**
- Tenant onboarding wizard working
- All masters CRUD
- Login flow + permissions
- Sample tenant ready

### Gate 2: End of Month 2
**B2B Operational**
- Receive → Putaway → Pick → Pack → Ship
- Manual carrier OK
- Stock accurate
- Reservations working

### Gate 3: End of Month 3
**B2C Ready**
- Marketplace integration
- Carrier APIs
- Pack video
- Returns workflow
- Ready to onboard B2C customers

### Gate 4: End of Month 4
**3PL Ready**
- Owner concept (VMI/3PL)
- Billing engine
- Customer portal
- ATP + Strategic allocation
- Ready to onboard 3PL clients

### Gate 5: End of Month 5
**Production Live**
- All modules complete
- Reports + dashboards
- Performance verified
- Soft launch successful

---

## 🎯 Resource Allocation

### If 1 developer + Claude Code:
- Add 50% buffer (6-7 months realistic)
- Skip Phase 4 advanced features
- Focus on B2B + B2C + Basic billing

### If 2 developers + Claude Code:
- Stick to 5-month timeline
- Parallel: Backend / Frontend / Mobile
- All phases possible

### If 3 developers + Claude Code:
- Aim for 4 months
- Dedicated specialists
- Architect + 2 implementers
- Plus QA effort

---

## 🚨 Risk Register

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Marketplace API changes | Medium | High | Plugin pattern, version isolation |
| Carrier API instability | High | Medium | Circuit breakers, deferred fallback |
| 5K orders/day not met | Medium | High | Load testing in Week 19 |
| Multi-tenant performance | Medium | High | Read replicas, caching |
| User adoption (mobile) | Low | High | UAT with real users week 18 |
| Data migration issues | Medium | Medium | Migration scripts week 20 |
| PDPA compliance gap | Low | High | Audit checklist month 5 |

---

## 🎓 Learning Tools (Use Claude Code Effectively)

```
✅ Spec → Code: feed design docs to Claude Code
✅ Code review: Claude reviews PRs
✅ Test generation: unit tests via Claude
✅ Bug fixes: paste error → Claude debugs
✅ SQL queries: Claude writes complex queries
✅ Refactoring: Claude suggests improvements

Recommended pairing:
- Senior dev: architecture, hard bugs, integrations
- Claude: scaffolding, CRUD, tests, queries
- Junior dev: testing, UI polish, docs
```

---

## 📋 Definition of Done (Per Feature)

```
Code:
☐ Implementation complete
☐ Code review passed
☐ Unit tests written + passing
☐ Integration test for critical paths

Database:
☐ Migration script written
☐ Rollback script tested
☐ Indexes added
☐ Sample data updated

Frontend:
☐ Desktop UI complete
☐ Mobile UI complete (if applicable)
☐ Loading states
☐ Error handling
☐ Validation messages

Documentation:
☐ User guide section
☐ API documentation
☐ Admin guide (if config)

Quality:
☐ Manual testing passed
☐ Performance acceptable
☐ Security review (if sensitive)
☐ Accessibility check (basic)
```

---

## 🎯 Success Metrics

### Month 1 (Foundation)
- ✅ Tenant onboarding < 1 hour
- ✅ All master CRUD working
- ✅ Login flow < 3 seconds

### Month 2 (B2B Core)
- ✅ End-to-end B2B order < 30 minutes
- ✅ Pick accuracy ≥ 99% (test data)
- ✅ Pack accuracy ≥ 99.5%

### Month 3 (B2C Launch)
- ✅ 100 B2C orders/day handled
- ✅ Marketplace sync delay < 5 minutes
- ✅ Carrier success rate > 95%

### Month 4 (3PL)
- ✅ First invoice generated successfully
- ✅ Customer portal real-time
- ✅ ATP calculation < 2 seconds

### Month 5 (Production)
- ✅ 5,000 orders/day handled
- ✅ 99.5% uptime
- ✅ Zero critical bugs

---

**End of Implementation Roadmap**
