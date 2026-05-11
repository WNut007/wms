# Local smoke checklist (Phase 30A M3)

End-to-end manual smoke for a deployed WMS instance. Complements
`scripts\smoke\Smoke-Local.ps1` (which covers request-shape +
headers); this doc covers UI flows + business logic.

**Run this after** `Test-Local-Deploy.ps1` succeeds + automated
smoke is green.

**Capture results** in `docs/deployment/phase-30a-test-results.md`
(copy the template, fill in PASS/FAIL/notes per scenario).

---

## How to read this

Each scenario has:
- **Goal**: what's being validated
- **Pre**: state required before running
- **Steps**: numbered ordered actions
- **Expected**: what should happen
- **Failures to log**: gotchas that have bitten this codebase

---

## S1 — SuperAdmin bootstrap login

**Goal**: First-run SuperAdmin seed works; `MustChangePassword`
forces rotation; lands on dashboard after change.

**Pre**: `InitialSuperAdmin` configured in `appsettings.json`
(Email + TempPassword). Kestrel running.

**Steps**:
1. Open `http://localhost:5500/SuperAdmin/Auth/Login`.
2. Enter SuperAdmin email + the configured TempPassword.
3. Login should redirect to `/Account/ChangePassword` (forced).
4. Enter new password (8+ chars, mixed case, ≥1 digit).
5. Confirm.

**Expected**:
- Redirected to `/SuperAdmin/Dashboard` after change.
- 4 tile counts render (Total / Active / Suspended / Inactive).
- Recent audit feed shows your `LOGIN_SUCCESS` event + the
  `PASSWORD_CHANGE_SELF` event.

**Failures to log**:
- If the seed didn't run, the login form will reject your
  TempPassword as "invalid email or password". Check Kestrel logs
  for `InitialSuperAdmin` seed messages on startup.
- An empty `master.Tenants` table previously caused the dashboard
  tile aggregates to throw `InvalidCastException` (Phase 29 fix
  P1-3 wrapped SUM in ISNULL).

---

## S2 — Provision a tenant

**Goal**: SuperAdmin can create a new tenant end-to-end: master row
+ DB + migrations + bootstrap ADMIN seed + audit + email (if
configured).

**Pre**: S1 complete, logged in as SuperAdmin.

**Steps**:
1. Click "Tenants" in the topbar.
2. Click "New tenant".
3. Fill: Code = `SMOKE01` (alphanumeric, 2-20 chars), Name = `Smoke Test Co`,
   AdminEmail = `admin@smoke.example`, AdminFullName = `Smoke Admin`.
4. Submit.

**Expected**:
- Redirect to `/SuperAdmin/Tenants/Created`.
- Temp password displayed ONCE in a monospace block.
- Sidebar callouts: tenant code + DB name + admin email.
- "Back to tenants" link works; new row visible in list.
- `WMS_Tenant_SMOKE01` DB exists in SSMS (Object Explorer → Databases).
- `WMS_Tenant_SMOKE01.security.Users` has 1 row with `MustChangePassword=1`.
- `WMS_Master.master.Tenants` has new row with `Status='Active'`.
- `WMS_Master.master.SystemAuditLog` has `TENANT_CREATED` event.
- If `Email__TestMode=false`: email arrives at `admin@smoke.example` (use a real address you control). Subject: "Your WMS workspace 'Smoke Test Co' is ready".
- If `Email__TestMode=true`: log line in Kestrel: `[Email TestMode] Would send to admin@smoke.example: ...`.

**Failures to log**:
- If provisioning fails partway, Phase 27 rollback should DROP
  `WMS_Tenant_SMOKE01` + DELETE the `master.Tenants` row. Verify
  both in SSMS — partial state = bug.
- If the email step fails, provisioning should STILL succeed
  (best-effort by design). Check for a Warning log line.

---

## S3 — Tenant first login + forced password change

**Goal**: Bootstrap admin lands on `/Account/ChangePassword` directly
(skip warehouse picker), `MustChangePassword` claim cleared after
change.

**Pre**: S2 complete. You have `admin@smoke.example` + the temp
password from S2.

**Steps**:
1. Open `http://localhost:5500/Auth/Login` in an **incognito**
   window (avoid SuperAdmin cookie collision).
2. Enter `admin@smoke.example` + the temp password.
3. Only one tenant exists for this email → tenant select skipped.
4. **Critical**: should land DIRECTLY on `/Account/ChangePassword`
   (NOT `/Auth/SelectWarehouse` — bootstrap admin has no
   warehouses assigned yet).
5. Enter new password (current = temp, new + confirm = your new one).

**Expected**:
- After change, redirect to `/` (which redirects to dashboard).
- Sidebar shows tenant name in the workspace card.
- `MustChangePassword` claim is gone — refresh `/`, no redirect to
  ChangePassword.

**Failures to log**:
- If you land on `/Auth/SelectWarehouse` first, Phase 29's
  AuthController bypass didn't fire (P0-1 fix).
- If `/Account/ChangePassword` rejects your temp password as
  current, BCrypt hash mismatch — Phase 27 seeded the user but
  password didn't take.

---

## S4 — Add a team member

**Goal**: ADMIN role can create new users with role assignment.

**Pre**: S3 complete.

**Steps**:
1. Sidebar → SECURITY → Users.
2. "New user" button → fill: Email = `picker1@smoke.example`,
   FullName = `Picker One`, Password = anything valid, Role = `PICKER`.
3. Save.

**Expected**:
- User Detail page renders with status badge + role chip.
- `WMS_Tenant_SMOKE01.security.Users` row exists.
- `WMS_Tenant_SMOKE01.security.AuditLog` has `USER_CREATED` + `ROLE_ASSIGNED` events.
- "Add another role" works without losing the existing one.

**Failures to log**:
- Email collision check: try creating with the same email →
  expected "Email already in use" inline error.

---

## S5 — Create a Warehouse + Location

**Goal**: Master Data CRUD operational (warehouses, locations).

**Pre**: S3 complete.

**Steps**:
1. Sidebar → Master Data → Warehouses → New warehouse.
2. Fill: Code = `WH01`, Name = `Smoke Warehouse`, Type = `Hub`.
3. Save.
4. From the Warehouse Detail page, add a Location: Code = `A01`,
   Zone = `Storage`.

**Expected**:
- Both rows persist; appear in their list pages.
- Audit events fire.
- Warehouse picker on `/Auth/SelectWarehouse` now lists WH01 for
  future logins.

---

## S6 — Create a PO + receive (desktop)

**Goal**: Inbound MVP chain works on desktop. (Mobile receive is S7.)

**Pre**: S5 + at least 1 Supplier (Owner) + 1 Product seeded. Use
the desktop Owners + Products UI if needed.

**Steps**:
1. Sidebar → Inbound → Purchase Orders → New PO.
2. Fill header: Code = `PO-SMOKE-001`, Vendor = your supplier,
   Warehouse = WH01, Expected = today.
3. Add 1 line: Product, UoM, Expected qty = 10.
4. Submit (Status → Open).
5. Sidebar → Inbound → Goods Receipt → New goods receipt.
6. Pick PO `PO-SMOKE-001`; line pre-fills with outstanding qty = 10.
7. Set Location = `A01`, Received = 10. Click "Post receipt".

**Expected**:
- Receiving Detail renders with line table.
- PO status = `Closed` (all qty received).
- `inventory.Stock` has 1 row at (A01, your product, …) qty 10.
- `inventory.StockMovements` has 1 row: `MovementType=Receive`,
  `QuantityDelta=+10`, ReferenceType=`ReceivingLine`.
- Receipts list `/Receiving` shows the new GR with status `Posted`.

**Failures to log**:
- TransactionScope is mandatory here (Phase 10B). If receiving
  fails partway (e.g. service exception during the StockMovements
  insert), you should see ZERO rows persisted. A partial state = TX
  scope broken.

---

## S7 — Mobile receive PWA

**Goal**: Mobile receive replicates the desktop flow with the
PWA shell.

**Pre**: At least one PO with outstanding lines (`Open` or
`PartiallyReceived`). Use a remaining-qty PO from S6 if you set
qty < expected, or create a new one.

**Steps**:
1. Open `http://localhost:5500/receive` on phone / DevTools mobile
   emulator.
2. Queue lists Open + Receiving POs (Receiving first).
3. Tap a PO → per-line cards render.
4. Tap a card; enter Received qty + Location code.
5. "Submit" sticky button at bottom.

**Expected**:
- After submit, bounce back to queue.
- Stock + StockMovements writes parallel to S6.
- Variance indicator (green ✓ / amber ↓ / red ↑) renders per line.

**Failures to log**:
- Mobile receive's serial-tracked banner: if your product is
  `TrackingMethod='LotAndSerial'`, the submit should refuse with
  "use desktop" message (TD-040 family).

---

## S8 — Mobile putaway

**Goal**: Putaway-from-staging flow operational.

**Pre**: A Stock row at a Receiving/Staging zone (S6 creates one).

**Steps**:
1. Open `/putaway` on mobile.
2. Queue shows staging items.
3. Tap a row.
4. "Suggested location" hero card displays (or "no suggestion"
   amber banner).
5. Confirm — submit.

**Expected**:
- Bounce back to queue; the moved item is gone.
- Stock row at staging location decrements to 0 (or row deleted).
- Stock row at destination location has the moved qty.
- 2 StockMovements rows (source -, destination +), both
  MovementType=`Putaway`.

---

## S9 — Outbound chain (desktop SO → Allocate → mobile Pick)

**Goal**: Outbound MVP chain works front-to-mid.

**Pre**: S5-S8 done; Stock at A01.

**Steps**:
1. Sidebar → Outbound → Sales Orders → New SO.
2. Fill: Customer (use one from S5 territory), Warehouse = WH01,
   line = your product qty 5. Submit (Draft → Open).
3. From SO Detail: "Allocate" Quick Action. FIFO allocation runs.
4. Expected: SO status flips to `Allocated`; new Allocations tab
   on Detail shows 1 row pointing at A01.
5. "Generate pick" Quick Action; redirects to `/PickTasks/Detail/{id}`.
6. Open `/pick` on mobile. Tap the new task.
7. Per-line cards default Picked = Expected. Hit "Submit pick".

**Expected**:
- PickTask status = `Picked`, SO status = `Picked`.
- Stock OnHand at A01 decremented by 5.
- Stock QuantityAllocated released to 0.
- StockMovement row: `MovementType=Pick`, `QuantityDelta=-5`,
  ReferenceType=`PickTaskLine`.
- OrderAllocation row flipped Active → Picked.

**Failures to log**:
- If FIFO allocation finds zero candidates (no stock), SO stays
  `Allocating` with a Shortfall on the allocation tab.

---

## S10 — Outbound chain finish (Pack + Ship)

**Goal**: Outbound MVP chain works end-to-end.

**Pre**: S9 complete; SO in `Picked`.

**Steps**:
1. From SO Detail (`Picked` state): "Generate pack" Quick Action.
2. Redirects to `/PackTasks/Detail/{id}`.
3. Open `/pack` on mobile. Tap the new task.
4. Per-line cards default Packed = Picked. Enter Carton metadata
   (any BoxType + Weight 1 kg).
5. Submit.
6. Bounce back to queue. Open SO Detail: status = `Packed`.
7. "Generate shipment" Quick Action; redirects to
   `/Shipments/Detail/{id}`.
8. From the Shipment Detail: fill Carrier (free-text "Test
   Carrier") + Tracking ("TRK-001"). Submit.

**Expected**:
- Shipment status = `Shipped`; SO status = `Shipped`.
- 1 Carton row exists with the metadata.
- Cartons.ShipmentId stamped on submit.
- Audit trio on Shipment Detail (Generated / Shipped) populated.

---

## S11 — Reports + Excel export

**Goal**: Reports module renders; Excel export produces a
multi-sheet `.xlsx`.

**Pre**: Some inventory + orders activity (S6-S10).

**Steps**:
1. Sidebar → Reports.
2. Open "Inventory" report → 4 stat tiles + 3 charts render.
3. Click "Export to Excel" on each report (Inventory, Orders,
   KPIs).

**Expected**:
- Charts render (ApexCharts loads from CDN, no JS errors in
  console).
- Each export returns a `.xlsx` file with 5 sheets.
- Filenames: `inventory-{yyyyMMdd}.xlsx`, `orders-{range}-{yyyyMMdd}.xlsx`,
  `kpis-{range}-{yyyyMMdd}.xlsx`.
- Open in Excel: headers bold + purple-tinted, tabular numbers.

**Failures to log**:
- If ApexCharts fails (CDN blocked / network issue), charts will
  show "loading…" forever — open browser DevTools console.

---

## S12 — Suspend + reactivate tenant

**Goal**: SuperAdmin Suspend stops tenant logins; Reactivate
restores them.

**Pre**: S2 created the SMOKE01 tenant; S3 verified login works.

**Steps**:
1. Log back into SuperAdmin (`/SuperAdmin/Auth/Login`).
2. Tenants list → click SMOKE01.
3. Suspend with reason "Smoke test suspension".
4. Open `/Auth/Login` in incognito; try logging in as
   `admin@smoke.example`.
5. Back to SuperAdmin → Tenants → SMOKE01 → Reactivate.
6. Try logging in as `admin@smoke.example` again.

**Expected**:
- Step 4: login refused (tenant suspended notice or
  "invalid email or password" — depends on which surface intercepts).
- `master.Tenants.Status` = `Suspended`; audit `TENANT_SUSPENDED`
  fired with your reason in Details.
- Step 6: login succeeds; dashboard renders normally.
- `master.Tenants.Status` flipped back to `Active`; audit
  `TENANT_REACTIVATED` fired.

**Failures to log**:
- If suspend doesn't block login, tenant validation middleware
  isn't reading `Status`. Phase 27 wires this — check the
  `TenantValidationMiddleware` in Program.cs is registered before
  authorization.

---

## Done

All 12 green = Phase 30A local validation complete.

Fill in your results in `phase-30a-test-results.md` and check off
M4's "browser smoke complete" item in the Phase 30B prep checklist.

If any scenario failed: open an issue with the **Failures to log**
section and your reproduction. Don't tag `v2.16.0-deploy-test-ready`
until all 12 pass.
