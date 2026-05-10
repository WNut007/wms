# Phase 18: Mobile Receive — UI Specification

**Status**: Draft, ready for CC implementation  
**Tag**: `v2.4.0-mobile-receive`  
**Branch**: `feat/mobile-receive-pwa`  
**Estimated time**: ~4 hours  
**Pattern reuse**: ~80% from Phase 16 picker

> **READ FIRST**: `mobile-design-system.md` (foundation document)

---

## 📋 Scope

### In Scope (MVP)
- `/receive/` PWA route (manifest + scope + standalone)
- `/receive` queue: POs with Open|Receiving status
- `/receive/{poId}` task page: per-line entry cards
- `POST /receive/submit/{poId}` (reuses `PostReceivingAsync`)
- `POST /receive/cancel/{poId}` (clears in-progress draft)
- Sidebar entry: "Receive (mobile)" under Inbound
- Serial tracking support (if product flagged)

### Deferred as TDs
- Always-focused hidden barcode input
- navigator.vibrate feedback on scan
- Service worker offline caching
- PWA icons (manifest's `icons:[]` empty)
- Per-line wizard (one-line-at-a-time)
- 4-tier scan flow (Location → Pallet → SKU → Lot)
- Multi-receipt session (combining multiple POs)

---

## 🏗️ Pattern Reuse from Phase 16

**Direct reuse**:
- ✅ PWA scaffolding pattern (Controller + manifest + queue + sidebar)
- ✅ Per-line cards + Alpine + Cancel modal
- ✅ Sticky-bottom submit button
- ✅ Native `window.prompt()` for cancel reason
- ✅ Bounce-to-queue UX on submit
- ✅ BaseController DI pattern (constructor injection)

---

## 📱 Screen Specifications

### Screen 1: Receive Queue (`/receive`)

#### Layout (top to bottom)

```
┌────────────────────────────┐
│ Status bar (24px)          │
├────────────────────────────┤
│ App header                 │
│ ← [Receive] {N} pending    │
├────────────────────────────┤
│ [All N] [Open N] [Recv N]  │ ← chip row, no scrollbar
├────────────────────────────┤
│ ┌────────────────────────┐ │
│ │ PO-2025-001 [Open]     │ │ ← PO card
│ │ Acme Supplier · 3/5    │ │
│ │ Expected: 50 ea        │ │
│ └────────────────────────┘ │
│ ┌────────────────────────┐ │
│ │ PO-2025-002 [Receiving]│ │
│ │ ...                    │ │
│ └────────────────────────┘ │
│  (vertical scroll, hidden) │
├────────────────────────────┤
│ Tab bar (Home·Activity·    │
│   FAB·Tasks·Me)            │
└────────────────────────────┘
```

#### Header
- Back button (30x30px, `#f3f4f6` bg, 0.5px border)
- Title: "Receive" (13px, font-weight 500, `#1f2937`)
- Subtitle: "{N} pending receipts" (9px, `#6b7280`)
- Optional right action: filter icon (30x30px, primary-light bg)

#### Filter Chips
**MUST hide horizontal scrollbar via `.no-scrollbar`**

- All N (default active): bg `#534AB7`, text white
- Open N: count of POs in Open status
- Receiving N: count of POs in Receiving status
- Inactive chips: bg white, border 0.5px `#e5e7eb`, text `#6b7280`
- All pills: height 26px, padding 4px 10px, border-radius 100px
- Font: 11px, font-weight 500

#### PO Card
- Background: white
- Border: 0.5px solid `#e5e7eb`
- Optional border-left: 3px solid color (urgency)
- Border-radius: 11px
- Padding: 11px 13px
- Margin between cards: 8px

**Content layout**:
```
[Header row]
  PO-{number} [status badge]              [chevron-right]
  
[Subtitle] (10px, #6b7280)
  Vendor name · Expected: {date}

[Stats row] (10px)
  📦 {expected} ea  ·  📋 {lineCount} lines  ·  ⏰ {hoursLeft}h
```

#### Status Badges
- **Open**: bg `#EEEDFE`, text `#534AB7`
- **Receiving**: bg `#FAEEDA`, text `#854F0B`
- Pill: 9px text, padding 1px 6px

#### Behavior
- Tap card → `/receive/{poId}` (task page)
- Filter chips toggle visible POs
- Sort: urgent first (overdue, then today, then future)
- Empty state: "No pending receipts. All caught up!"

---

### Screen 2: Receive Task (`/receive/{poId}`)

#### Layout

```
┌────────────────────────────┐
│ Status bar                 │
├────────────────────────────┤
│ Breadcrumb header          │
│ ← Receive › PO-2025-001    │
│ Receive items   [Receiving]│
├────────────────────────────┤
│ Vendor info bar            │
│ Acme Supplier · 5 lines    │
├────────────────────────────┤
│ ┌────────────────────────┐ │
│ │ Line 1                 │ │ ← line card
│ │ PROD-A001              │ │
│ │ Premium Widget A       │ │
│ │                        │ │
│ │ Expected: 50 ea        │ │
│ │ Received: [____ ea]    │ │ ← qty input
│ │                        │ │
│ │ Lot: [optional______]  │ │
│ │ Pallet: [optional____] │ │
│ └────────────────────────┘ │
│  ...more line cards         │
├────────────────────────────┤
│ Sticky bottom              │
│ [Submit receipt]           │
│ [Cancel]                   │
└────────────────────────────┘
```

#### Breadcrumb
- Small back button: 22x22px
- Path: "Receive" › `{PO-NUMBER}` (monospace, font-weight 500)
- Font: 9px, `#6b7280`

#### Title Block
- Title: "Receive items" (13px, font-weight 500)
- Status badge: "Receiving" (amber)

#### Vendor Info Bar
- Background: `#f9fafb`
- Padding: 8px 16px
- Font: 10-11px
- Content: vendor name · line count · expected delivery date

#### Line Cards
- Background: white
- Border: 0.5px solid `#e5e7eb`
- Border-left: 3px solid `#534AB7` (active state)
- Border-radius: 11px
- Padding: 12px 14px
- Margin between: 8-10px

**Inside each card**:
- Line number (small, top): "Line {n}"
- Product code (12px, monospace, font-weight 500)
- Product name (10px, `#6b7280`)
- Stats grid (2-col):
  - Expected: 22px font-weight 500, `#6b7280` (gray = system)
  - Received input: 22px font-weight 500, `#534AB7` (purple = user input)
- Optional fields (collapsed by default, expand on tap):
  - Lot input
  - Pallet input
  - Notes input

#### Quick Adjust Buttons
Below received qty input:
```
[−10] [−1] [+1] [+10]
```
- Height: 32px
- Background: white, border 0.5px `#e5e7eb`
- Border-radius: 7px
- Font: 11px font-weight 500

#### Variance Indicator
If received != expected, show inline:
- **Match**: green check + "Matches expected"
- **Under**: amber arrow-down + "{n} under expected"
- **Over**: red arrow-up + "{n} over expected"

#### Serial-tracked Sub-state
**If `product.TrackingMethod == 'LotAndSerial'`** (per Phase 18 implementation):

> **NOTE (Implementation reality)**: Phase 18 shipped with this as a "use desktop" banner (TD-040). The full serial entry UI below is the design goal but currently deferred. Mobile receive shows amber banner + disables qty input + blocks submit for serial-tracked products.

- Show amber badge: "🔢 Serial-tracked · {N} serials needed"
- Replace single qty input with serial entry mode:

```
┌────────────────────────┐
│ Quantity: 3 ea         │
│                        │
│ Scan/enter each serial:│
│ ┌────────────────────┐ │
│ │ Scan area          │ │
│ │ (camera + manual)  │ │
│ └────────────────────┘ │
│                        │
│ Captured (2 of 3):     │
│ ✓ SN-A1B2C-X9Y8Z       │
│ ✓ SN-A1B2C-X1Y2Z       │
│ ⏳ Awaiting serial #3  │
└────────────────────────┘
```

- Validation: serial unique within tenant
- Edit/remove allowed pre-submit
- Cannot submit until all serials captured

#### Sticky Bottom Actions
- **Primary**: "Submit receipt" 
  - 46px, bg `#534AB7`, white text, font-weight 500
  - Disabled state: bg `#d1d5db`, message "Capture all required fields"
- **Secondary**: "Cancel"
  - 36px, white bg, `#6b7280` text, border `#e5e7eb`
  - Tap → `window.prompt('Cancellation reason')`
  - Submit → POST `/receive/cancel/{poId}` with reason

---

## 🔌 Backend Integration

### Routes
- `GET /receive` → queue page
- `GET /receive/{poId}` → task page
- `POST /receive/submit/{poId}` → call `IReceivingHeaderService.PostReceivingAsync`
- `POST /receive/cancel/{poId}` → revert in-progress receipt to nothing

### Reuses Existing
- `IReceivingHeaderService` (no new service code)
- `PostReceivingAsync` (TX-wrapped, atomic)
- Existing receipt entity & state machine

### Manifest
```json
{
  "name": "WMS Receive",
  "short_name": "Receive",
  "start_url": "/receive/",
  "scope": "/receive/",
  "display": "standalone",
  "background_color": "#f9fafb",
  "theme_color": "#534AB7",
  "orientation": "portrait",
  "icons": []
}
```

---

## ⚠️ Audit & Pause Conditions

### Schema Audit Required Before T1
Check existence:
- ~~`master.Products.IsSerialTracked` (bit, nullable OK)~~
- **CORRECTED (Phase 18 actual)**: `master.Products.TrackingMethod` enum exists already
  - Values: `None`, `LotOnly`, `LotAndSerial`
  - For serial-tracked: `TrackingMethod == 'LotAndSerial'`
  - No new column needed

If column **missing**:
- **PAUSE** → user decision needed
- Option A: Add column in this phase (small migration)
- Option B: Defer serial tracking to Phase 18.5 (cleaner MVP)

### Pause If
- IReceivingHeaderService signature changed since Phase 9
- Mobile mockup deviation needs clarification
- Build breaks
- Reaching ~6h mark
- Sidebar nav requires structural changes

---

## 📦 Chunk Plan

### T1: PWA Scaffolding (~1h)
- ReceiveController.cs
- /receive/manifest.json
- Queue view (Razor)
- Sidebar entry under Inbound module
- Filter chip CSS (reuse Phase 16's chip styles)

### T2: Receipt Task Page (~1.5h)
- Per-line card components (Razor partial)
- Alpine state management
- Sticky-bottom submit
- Cancel modal (native prompt)
- Variance indicators
- Serial-tracked branch (conditional rendering)

### T3: Submit + Cancel Endpoints (~1h)
- POST /receive/submit/{poId}
  - Build PostReceivingAsync request from form data
  - Handle serials if any
  - Bounce to queue on success
- POST /receive/cancel/{poId}
  - Clear any in-progress draft
  - Idempotent (cancel twice = OK)

### T4: Tests + CLAUDE.md + Tag (~30 min)
- Controller tests (~6):
  - Queue page renders
  - Task page renders
  - Submit happy path
  - Submit serial-tracked
  - Cancel
  - 404 for invalid PO
- Update CLAUDE.md log
- Tag `v2.4.0-mobile-receive`

---

## ✅ Acceptance Criteria

- [ ] /receive route accessible from sidebar
- [ ] Queue shows POs with Open|Receiving status
- [ ] Filter chips work, NO horizontal scrollbar visible
- [ ] PO cards display per spec (colors, fonts, spacing)
- [ ] Tap card → task page
- [ ] Per-line qty entry with quick-adjust buttons
- [ ] Variance indicator updates in real-time
- [ ] Serial-tracked products show serial entry mode
- [ ] Submit posts receipt + bounces to queue
- [ ] Cancel reverts state via prompt
- [ ] Hidden scrollbars throughout (.no-scrollbar)
- [ ] Touch targets all ≥ 38px
- [ ] PWA installable (manifest works)
- [ ] Standalone display mode

---

## 🎬 Smoke Test Scenarios

After CC ships:

1. **Sidebar entry**: Click "Receive (mobile)" → /receive opens
2. **Queue display**: See POs with status chips, filter works
3. **No scrollbars**: Verify NO horizontal scroll bars visible anywhere
4. **Tap PO**: Goes to task page with line cards
5. **Quantity entry**: Default = expected, change works, variance shows
6. **Quick adjust**: -1/+1/-10/+10 buttons work
7. **Serial product**: If any product has TrackingMethod=='LotAndSerial', verify amber "use desktop" banner shows (TD-040 — full serial mode deferred)
8. **Submit happy**: Receipt posted, bounce to queue
9. **Cancel**: Native prompt asks reason, cancel works
10. **PWA install**: "Add to home screen" works, app opens standalone

---

## 🔗 Reference

- **Foundation**: `mobile-design-system.md`
- **Pattern source**: Phase 16 mobile picker (PicksController + /pick/)
- **Desktop equivalent**: `/Receiving` (existing)
- **Service**: `IReceivingHeaderService.PostReceivingAsync`
