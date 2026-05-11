# Customer Onboarding Playbook

> Audience: anyone running a sales-led onboarding (you, future sales hires).
> Purpose: a repeatable process from first qualifying call through 90-day expansion.
> Companion to the SuperAdmin tenant-provisioning surface shipped in Phase 27.

---

## 1. Pre-Sales Discovery

### Qualifying questions

Run these in the first 30-minute call. Goal: decide go / no-go within 1-2 conversations.

**Operational profile**
- Company size (warehouse count, headcount, IT staff)
- Current WMS — what does it do well? What hurts every day?
- Order volume — daily B2B + B2C transactions; peak vs. average ratio
- Number of warehouses + locations per warehouse (rough)
- Inventory complexity — single-SKU, lot-tracked, serial-tracked? Lot expiry?
- Mobile usage — barcode scanners today, or paper-and-pen?

**Technical fit**
- Cloud / on-prem preference (we deploy Windows Server today; cloud-managed = TD)
- Tech stack alignment — SQL Server already in use? .NET shops have an easier integration story
- Integrations — ERP system (SAP / Oracle / Microsoft Dynamics)? E-commerce (Shopify / WooCommerce)? Carriers?
- Mobile devices — Android / iOS / both? Modern browsers on each (Chromium for PWA install)?

**Compliance + decision**
- Compliance constraints — SOC2, ISO 27001, GDPR, PDPA (Thailand), industry-specific (FDA / FSSAI / etc.)
- Data residency — on-shore requirement?
- Decision timeline — when do they need this live?
- Budget signal — annual contract value range?

### Technical fit matrix

Honest about what we have today (v3.0.0 chapter set):

| Capability | Status today | Notes |
|---|---|---|
| Multi-tenant isolation | ✅ DB-per-tenant (ADR-001) | Enterprise-grade isolation |
| Inbound (PO + receiving + putaway) | ✅ Desktop + mobile | Phases 9 + 18 + 20 |
| Outbound (SO + pick + pack + ship) | ✅ End-to-end MVP | Phases 14A-E |
| Inventory (stock, transfers, adjustments, cycle count) | ✅ Desktop + mobile | Phases 11-13 + 21 |
| Mobile PWA suite | ✅ 6 of 6 workflows | Pick / Receive / Pack / Putaway / Count / Locate |
| Reports + Excel export | ✅ Inventory + Orders + KPIs | Phase 23 |
| Tenant admin (users + roles + audit) | ✅ Day-1 ready | Phase 24 |
| Security (password policy, 2FA, rate limit) | 🟡 Password + rate-limit ✅; 2FA = v3.1 | TD-055 |
| Production deployment | ✅ IIS + health endpoints + Serilog file logs | Phase 26 |
| SuperAdmin tenant CRUD | ✅ Provision / suspend / reactivate | Phase 27 |
| Lot expiry tracking | 🟡 Lot schema present, FEFO strategy = future | TD-040 family |
| Serial number tracking | ❌ Schema not yet built | TD-040 + TD-042 + TD-043 (Phase 19.5 bundle) |
| ERP integration (SAP/Oracle/Dynamics) | ❌ | Custom per customer; TD-092 (API) |
| E-commerce integration | ❌ | Phase 31+ |
| Carrier label printing | ❌ Free-text tracking only | TD-038 |
| Pack video / retention | ✅ MediaRecorder + 10-day retention | Phase 17 |
| Cycle count scheduled / recurring | ❌ Manual only | TD-032 |
| Hard delete tenants | ❌ Suspend only | TD-079 |

Use this as the answer to "does it do X?" — if the customer's must-haves all fall under ✅, they're a fit. If even one must-have is ❌, be honest about timeline.

### Disqualifiers (walk away politely)

- Need < 30 days to live and require a missing ❌ feature
- < $20K ACV target (sales-led economics don't work)
- Custom ERP integration with no flexibility on either side (Phase 31+ work)
- Compliance regime we can't credibly meet today (HIPAA, e.g.)

### Use cases worth digging into

For each must-have they listed:

1. Map to a shipped feature
2. Identify gaps (TDs vs Phase 19.5 bundle vs hard NO)
3. Honest about timeline if a gap matters
4. Document outcomes — feeds back into product roadmap prioritisation

---

## 2. Tenant Provisioning

Once contracts are signed, provisioning is a 10-minute task — but precision matters because the temp password displays ONCE.

### Pre-provisioning prep

- [ ] Tenant code chosen — alphanumeric, 2-20 chars, uppercase by convention (`ACME`, `GLOBEX`, `CUSTNAME01`)
- [ ] Display name agreed with customer ("ACME Corporation")
- [ ] Initial admin email confirmed with customer ("admin@acme.com" or specific named contact)
- [ ] Admin full name confirmed ("Jane Operator")
- [ ] Secure delivery channel ready — Signal / Slack DM / encrypted email — for sharing the temp password
- [ ] Recipient identity verified (avoid impersonation at hand-off)

### Step-by-step

1. **Login to /SuperAdmin/Login** — use your SuperAdmin credentials. URL is the platform admin console (separate from the customer-facing /Auth/Login).
2. **Navigate to /SuperAdmin/Tenants** — see all existing tenants.
3. **Click "Provision new tenant"** — opens the form.
4. **Fill in**:
   - Tenant code: `ACME` (used in DB name `WMS_Tenant_ACME`)
   - Display name: `ACME Corporation`
   - Admin email: `jane.admin@acme.com`
   - Admin full name: `Jane Operator`
5. **Submit** — synchronous provisioning takes ~10-30 seconds:
   - INSERT `master.Tenants` row
   - CREATE DATABASE [WMS_Tenant_ACME]
   - Run ~30 tenant schema migrations
   - INSERT bootstrap ADMIN user with temp password + MustChangePassword=true
   - INSERT `master.UserTenantMap` row (IsDefault=true)
   - Emit `TenantCreated` audit event
6. **Success page displays** the temp password ONCE.
7. **CRITICAL — capture the temp password immediately**. Click "Copy to clipboard" or screenshot. It won't be shown again.
8. **Verify** by clicking "View tenant" — confirm Status=Active, user count=1, admin email matches.

### Sending credentials securely

**NEVER** email the temp password in plaintext. Wrong channels: regular email, SMS, Slack public channel.

Right channels:
- Signal DM
- Slack DM (single recipient)
- 1Password / Bitwarden shared item (recipient must have access)
- In-person hand-off (rare but acceptable)
- Encrypted PDF + password sent separately

Message template:

> Subject: Your WMS tenant is ready
> 
> Hi Jane,
> 
> Your WMS workspace is live. Use these credentials to sign in:
> 
> **URL**: https://wms.example.com/Auth/Login  
> **Email**: jane.admin@acme.com  
> **Temporary password**: `<DROP-IN HERE>`
> 
> Important: this is a one-time password. The system will require you to set a permanent password on first login. Make it strong (8+ chars, mixed case, digits).
> 
> After login, you'll land on the dashboard. From there:
> - Add the rest of your team (Security → Users)
> - Configure your warehouse (Master Data → Warehouses)
> - Set up your products (Master Data → Products)
> 
> Our onboarding session is scheduled for `<date>`. Bring questions.

### If something goes wrong

- **Provisioning fails mid-flight**: the rollback drops the DB and removes the tenant row. Re-run with the same code; should succeed once the underlying issue is fixed (transient network, etc.).
- **Customer can't sign in**: verify the email is exact — `Jane.Admin@Acme.com` is the same as `jane.admin@acme.com` (case-insensitive at SQL level), but typos break it.
- **Temp password reads ambiguously** (operator types `0` instead of `O`): generator excludes confusable chars (0/O/1/l/I) but check anyway. Reset via /SuperAdmin/Tenants/{id} → Reset admin password if needed.
- **Customer asks "can you see my password?"** — no, only the BCrypt hash is stored. Even SuperAdmin can't see plaintext after the success page.

---

## 3. Customer Kickoff

### Pre-meeting checklist

- [ ] Tenant provisioned + tested (you signed in as the admin, verified the dashboard renders)
- [ ] Initial admin successfully logged in once (rotated temp password)
- [ ] Demo data plan agreed — do they want a starter dataset, or empty?
- [ ] Training agenda shared with the customer 24h ahead
- [ ] Success criteria defined — what does "Day 30 success" look like for THIS customer?
- [ ] Meeting access — physical room? Zoom link? Recording permission?
- [ ] Customer's tech contact present — IT person who'll own the integration

### Initial setup walkthrough (60-90 min session)

Sequence matters. Don't jump ahead.

1. **Initial password change** (5 min)
   - Customer admin logs in with temp password
   - Forced redirect to /Account/ChangePassword
   - They set a new strong password (8+/mixed/digit policy)
   - Confirm they remember it; password manager recommended

2. **Add company users** (10-15 min)
   - Sidebar → Security → Users
   - For each warehouse staff member: click "New user", fill email + name + roles, save
   - Decide role per person:
     - **PICKER** — handheld scanner, pick tasks only
     - **PACKER** — pack station, pack + ship
     - **MANAGER** — broad operational oversight, can approve adjustments / cycle counts / transfers
     - **ADMIN** — full tenant control + can manage users
   - Each new user starts with a temp password (you'll need to communicate that, similar to admin onboarding)

3. **Configure warehouses** (10-15 min)
   - Sidebar → Master Data → Warehouses
   - Add their first warehouse (already exists from migration seed; rename if needed)
   - Add additional warehouses if multi-site
   - Each warehouse: configure zones (Receiving / Storage / Picking / Packing / Shipping / Staging / Quarantine / Returns)
   - Each zone: configure locations (bins, racks, aisle codes)
   - Convention: use customer's existing aisle/rack codes if any

4. **Master data import** (30+ min, depending on size)
   - **Order matters** because of FK dependencies:
     1. Product Categories (tree structure)
     2. Units of Measure (Each, Box, Pallet, etc.)
     3. Carriers (their shipping providers)
     4. Owners (the customer themselves if 3PL'd, or skip if direct B2C)
     5. Products (SKU master) — usually CSV import via SSMS for >100 SKUs
     6. Customers (their B2B/B2C buyers)
     7. Vendors (suppliers; we treat as Owners with type='Supplier')
   - Mass import: today via SSMS / SQL scripts (TD-052 bulk import UI is on roadmap)
   - Verify by browsing the admin lists after each import

5. **Test workflow** (15 min)
   - Goal: prove the system end-to-end with one PO → receive → stock visible
   - Create a Purchase Order (Inbound → Purchase Orders → New)
   - Switch to mobile PWA on a scanner (/receive route, "Add to home screen")
   - Receive against the PO on mobile
   - Verify stock appears in Reports → Inventory
   - This is the moment they see the value loop. Make it land cleanly.

### Training topics by role

**ADMIN** (1 hour)
- User + role management
- Audit log access + interpretation
- Roles permission matrix (what each role can do)
- Suspending users
- Reports + Excel export
- Coordinating with you (the platform team) when stuck

**MANAGER** (45 min)
- Outbound flow (SO → allocate → pick → pack → ship)
- Cycle count approval workflow
- Adjustment approval workflow
- Transfer between warehouses
- Reading the reports
- Reading the AuditLog when investigating issues

**PICKER + PACKER** (30 min total)
- Mobile PWA "Add to home screen" — show on their actual device
- Picker flow: open Pick task → tap line → enter qty → submit
- Packer flow: open Pack task → enter qty per line → finalise
- What to do when scan fails (manual entry fallback)
- What to do when stock doesn't match (skip / short-pick reason)

---

## 4. Success Criteria

### Week 1

- [ ] All users created + first-login complete (temp passwords rotated)
- [ ] At least 1 PO → receipt → stock visible flow completed
- [ ] At least 1 SO → pick → pack → ship flow completed
- [ ] Mobile PWAs installed on actual warehouse devices (verify with screenshot or in-person check)
- [ ] No P0 incidents (login down, data loss, total inability to use the system)

Check-in call at end of Week 1: 30 min, review the above, capture friction points.

### Day 30 check-in

- [ ] Daily active usage by all roles (verify via `security.AuditLog` activity counts)
- [ ] 80%+ of expected transactions logged in the system (vs. paper / shadow process)
- [ ] Customer satisfaction call — open-ended ask "what's working / what's hurting?"
- [ ] Identify gaps: features missing? bugs? UX friction?
- [ ] Document everything in your CRM or shared doc — feeds the product roadmap
- [ ] Discuss expansion: more users, more warehouses, more features?

### Day 90 expansion

- [ ] **Reference customer call**: ask if you can name them as a customer + record a 5-min testimonial
- [ ] **Case study draft**: numbers worth quoting (order volume processed, time-savings vs. their old WMS)
- [ ] **Expansion conversation**: additional warehouses, additional users, premium features they want
- [ ] **Renewal commitment**: if annual contract, formally commit the next year ASAP

This is where Land+Expand becomes Expand. The first customer's referrals are worth more than your next 10 cold leads.

---

## 5. Common Questions & Answers

**"What does it cost?"**
For v3.0.0 launch: custom pricing per customer, sales-led. Typical anchor:
- Setup: $5K-15K depending on master data complexity
- Annual: $15K-50K depending on order volume + warehouses + user count
- Premium support (24/7 on-call): additional 20-30%
Negotiate, don't list-price.

**"What's your SLA?"**
For v3.0.0 launch: commit to what we can credibly meet. Honest pitch:
- 99.5% uptime (Mon-Sat business hours)
- 99.0% best-effort overnight + weekends
- 8-hour business-hour response, 24-hour resolution for P1 incidents
- 30-day notice for scheduled maintenance windows
Adjust based on customer's actual needs + what we're willing to commit to.

**"Where is our data?"**
v3.0.0 launch: on the Windows Server you (the customer) own OR a server we manage in a region of your choice. Each tenant = isolated SQL Server database. Document storage = local filesystem on the same server. Customer controls the data layer fully.

**"How often do you back up?"**
Customer-controlled today (we ship the schema; you run the SQL Server backup schedule). Recommendations in [runbook.md](./runbook.md) Section 6. Managed backups are a Phase 28+ premium service.

**"Do you integrate with [their ERP / e-commerce]?"**
Not in v3.0.0. We're at "robust core WMS, manual data exchange". Integrations are Phase 31+ on the roadmap. If they're hard-blocked on this, see Disqualifiers.

**"Can we customise [feature X]?"**
Two paths:
1. **Configuration** — settings within the existing tenant admin (timezone, fiscal year, etc.) — TD-083 on roadmap
2. **Customisation** — custom roles, custom fields, custom workflows — case-by-case engineering engagement (priced separately)

Avoid promising broad customisation in v3.0.0 — it scales poorly with the team size.

**"Can we self-sign-up / try a free tier?"**
Not in v3.0.0 (sales-led only, see ADR-015). Self-signup is post-launch (v3.2+).

**"What's your security stance?"**
- Phase 25: password policy (8+/mixed/digit), rate limiting (5/min/IP), lockout (5 fails → 30min), audit log for all auth events
- HTTPS-only with HSTS + CSP + X-Frame-Options (Phase 26)
- 2FA = v3.1 roadmap (TD-055)
- DB-per-tenant isolation (ADR-001 / ADR-016)
- Pen-test report = case-by-case (we'll commission one if a customer requires it as part of procurement)

**"What if we want to leave?"**
- Tenant suspension → data preserved + login blocked (Phase 27)
- Data export = TD-085 (manual SQL dump available today; UI in roadmap)
- Hard delete = TD-079 (manual procedure available today; admin UI in roadmap)
- Standard 30-day data return window after contract end

---

## Related Documents

- [Operations runbook](./runbook.md) — daily ops + incident response
- [Deployment checklist](../deployment/checklist.md) — pre/during/post deploy
- [Tech debt log](../TECH_DEBT.md) — what's missing today vs. roadmap
- [ADRs](../decisions/) — architectural decisions including ADR-015 (Land+Expand strategy)
