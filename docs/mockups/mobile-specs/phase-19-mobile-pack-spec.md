# Phase 19: Mobile Pack — UI Specification

**Status**: Draft, ready for CC implementation  
**Tag**: `v2.5.0-mobile-pack`  
**Branch**: `feat/mobile-pack-pwa`  
**Estimated time**: ~5 hours (more complex due to smart scan)  
**Pattern reuse**: ~70% from Phase 16 picker + Phase 18 receive

> **READ FIRST**: `mobile-design-system.md` (foundation document)

---

## 📋 Scope

### In Scope (MVP)
- `/pack/` PWA route
- `/pack` queue: PackTasks in Pending|Packing status
- `/pack/{taskId}` task page: per-line entry with carton context
- **Smart scan detection**: Product code OR Serial number auto-detect
- `POST /pack/submit/{taskId}` (reuses PackTaskService.SubmitAsync)
- `POST /pack/cancel/{taskId}` (mirror Phase 16 picker)
- Sidebar entry: "Pack (mobile)" under Outbound

### Deferred as TDs
- Multi-carton support (single-carton MVP from Phase 14D)
- Weight entry on close carton (Phase 14D MVP defer)
- Print label/manifest on close
- Connected scale integration (Bluetooth/USB)
- Pack workflow config (toggle steps)
- Always-focused barcode input
- Service worker offline caching
- PWA icons

---

## 🏗️ Pattern Reuse

**From Phase 16 picker**:
- ✅ PWA scaffolding pattern
- ✅ Per-line cards + Alpine
- ✅ Sticky-bottom submit
- ✅ Native prompt() for cancel
- ✅ Bounce-to-queue UX

**From Phase 18 receive (similar structure)**:
- ✅ Card layouts
- ✅ Variance indicators
- ✅ Serial entry sub-state

**New for Phase 19**:
- 🆕 Smart scan detection (product OR serial auto-detect)
- 🆕 Carton context card (gradient, real-time weight estimate)
- 🆕 Validation chain UI (multi-check feedback)

---

## 📱 Screen Specifications

### Screen 1: Pack Queue (`/pack`)

#### Layout

```
┌────────────────────────────┐
│ Status bar                 │
├────────────────────────────┤
│ ← [Pack tasks]             │
│ {N} active · {N} ready     │
├────────────────────────────┤
│ [All N] [Ready N] [Pack N] │ chip row
├────────────────────────────┤
│ 🔴 URGENT · ship today     │
│ ┌────────────────────────┐ │
│ │ PACK-2025-018 [Packing]│ │
│ │ Customer XYZ · SO-X    │ │
│ │ ▓▓▓▓░░ 3 of 5 packed   │ │ ← progress
│ │ 📦1 carton · ⏰2h left │ │
│ │              [Resume]  │ │
│ └────────────────────────┘ │
│ ⚪ TOMORROW · 3 tasks      │
│ ...compact cards            │
├────────────────────────────┤
│ [Scan SO/carton to start]  │ ← action
├────────────────────────────┤
│ Tab bar                    │
└────────────────────────────┘
```

#### Header
- Back button: 32x32px
- Title: "Pack tasks"
- Subtitle: "{N} active · {N} ready to pack"
- Right: Filter icon button

#### Urgency Sections (grouped)
Group tasks by urgency with visual separators:
- 🔴 Urgent (ship today): red dot + "URGENT · ship today" label
- 🟡 Today (24h): amber dot + "TODAY · ship in 24h"
- ⚪ Tomorrow: gray dot + "TOMORROW · {N} tasks"
- ⚫ Later: gray dot + "LATER · {N} tasks"

Section header style:
- Padding: 14px 0 8px
- Dot: 6x6px
- Label: 9px uppercase font-weight 500-600

#### Task Cards (urgent variant)
- Border-left: 3px solid `#ef4444` (urgent) or `#f59e0b` (today)
- Show progress bar (if Packing status)
- Show stats row (cartons, time remaining)
- Resume hint if InProgress
- Serial-tracked badge if applicable

#### Task Cards (compact, future)
- Smaller padding (10px 13px)
- No progress bar
- Single-line meta

#### Bottom Action
"Scan SO/carton to start" button (purple primary)
- Allows operator to scan barcode → jump to task

---

### Screen 2: Pack Task (`/pack/{taskId}`)

#### Layout

```
┌────────────────────────────┐
│ ← Pack › PACK-2025-018     │
│ Pack item     [Packing]    │
├────────────────────────────┤
│ ┌────────────────────────┐ │
│ │ Active carton (gradient│ │ ← session card
│ │ CTN-20251002-001       │ │
│ │ Box M · 3/5 · 2.4 kg   │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ ┌────────────────────────┐ │
│ │ Scan area (dashed)     │ │ ← primary input
│ │                        │ │
│ │  📷 Scan to pack       │ │
│ │     Or tap product     │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ Packed (3):                │
│ ✓ PROD-A001 · 5 ea  [×]   │
│ ✓ PROD-B002 · 12 ea [×]   │
│ ✓ PROD-C003 · 3 ea  [×]   │
│ Pending (2):                │
│ ⏳ PROD-D004 · 2 ea        │
│ ⏳ PROD-E005 · 1 ea        │
├────────────────────────────┤
│ [Close carton & continue]  │
│ [Save as draft]            │
└────────────────────────────┘
```

#### Active Carton Card (gradient, hero)
- Background: `linear-gradient(135deg, #534AB7, #7F77DD)`
- Color: white
- Border-radius: 12px
- Padding: 11-12px 13-14px
- Show:
  - "Active carton" label (9px, 0.7 opacity)
  - CTN number (14px, font-weight 500, monospace)
  - Box-seam icon (right side, 36x36px, rgba(255,255,255,0.2) bg)
- Stats row (3-col grid):
  - Box type: "M (30×30×30)"
  - Items: "3 / 5"
  - Weight: "2.4 kg"
  - Each tile: bg rgba(255,255,255,0.18), padding 4px 7px

#### Scan Area
Standard pattern from design system:
- 2px dashed border `#534AB7`
- Big camera icon (30px) in primary-light circle
- "Scan to pack" prompt
- Manual entry input below (alternative)

#### Packed Items List
- Heading: "Packed ({N})"
- Each item:
  - Green check icon (22x22px circle, success-light bg)
  - Product code + qty (11px font-weight 500)
  - Product name (9px text-secondary)
  - Remove button (X icon, right side)

#### Pending Items List
- Heading: "Pending ({N} lines)"
- Faded style (opacity 0.7)
- Dashed border (vs solid for packed)
- Clock icon instead of check
- No remove button

---

### Screen 3: Smart Scan Detection (Behavior)

When user scans/types a code, system auto-detects type:

#### Scenario A: Scanned Product Code
**Detect**: Code matches `Products.ProductCode`

If `TrackingMethod != 'LotAndSerial'` (i.e. None / LotOnly):
- Add line to packed list with current qty (default = picked qty)
- Show variance flag if qty != picked

If `TrackingMethod = 'LotAndSerial'`:
- Show product card with serial-tracked badge (amber)
- Display "Now scan a specific serial number"
- Show allocated serials list (helpful)
- Wait for serial scan/entry
- Cannot proceed until serial provided

> **NOTE (Phase 18 learning)**: There is no `IsSerialTracked` boolean column. Serial-tracking is determined by the existing `master.Products.TrackingMethod` enum (values: `None`, `LotOnly`, `LotAndSerial`). Use enum equality check.

UI feedback:
```
✅ Detected: Product code
[Product card with badge]
"Scan a specific serial number"
[Allocated serials list]
[Scan area]
[Cancel button]
```

#### Scenario B: Scanned Serial Number
**Detect**: Code matches a serial in serial inventory table (likely `inventory.LotSerials` or similar — verify in audit; spec assumed `master.ProductSerials` which may not exist)

System auto-resolves:
1. Lookup serial → find product
2. Validate: serial allocated to this order?
3. Validate: serial picked successfully?
4. Validate: serial not already packed?

If all pass:
- Show validation chain (4 green checks)
- "Add to carton" button enabled (purple primary)

UI feedback:
```
✅ Detected: Serial number
[Serial card with auto-detected product]
Validation:
✓ Serial exists in stock
✓ Allocated to this order
✓ Picked successfully  
✓ Not yet packed
[Add to carton]
```

#### Scenario C: Validation Failed
Show error card with reasons:
```
⚠️ Wrong serial
SN-OTHER-X9Y8Z

Why:
✗ Serial belongs to different order
✗ Already packed in another carton
✗ Doesn't exist in system
✗ Wrong product entirely

[Scan again]
[Report issue]
```

Background: `#FCEBEB`
Border-left: 3px solid `#A32D2D`
Icon: alert-triangle (red)

---

### Screen 4: Pack Submit Review

After all items packed, show review screen:

```
┌────────────────────────────┐
│ ← Pack › PACK-2025-018     │
│ Review & submit [Complete] │
├────────────────────────────┤
│ Stat tiles:                │
│ [Cartons:1][Items:5][3.8kg]│
├────────────────────────────┤
│ Cartons:                   │
│ ┌────────────────────────┐ │
│ │ 📦 CTN-20251002-001    │ │
│ │ Box M · 3.8 kg         │ │
│ │ • PROD-A001 · 5 ea     │ │
│ │ • PROD-B002 · 12 ea    │ │
│ │ • PROD-C003 · 3 ea     │ │
│ │ • PROD-D004 · 2 ea     │ │
│ │ • PROD-E005 · 1 ea     │ │
│ └────────────────────────┘ │
│ [+ Add another carton]     │ ← MVP single-carton
│                            │   (this is TD for v2.x)
├────────────────────────────┤
│ [Submit pack] (GREEN)      │ ← finalize action
└────────────────────────────┘
```

#### Stat Tiles
3-col grid:
- Cartons (purple accent border-left)
- Items (purple)
- Weight (green)

#### Carton List
- Each carton expandable
- Show contents as bulleted list (5px dot + product · qty)
- Carton metadata: box type · weight

#### Submit Pack Button (GREEN!)
- Background: `#1D9E75` (success, NOT primary purple)
- Indicates final/successful action
- Different from scan-to-pack (which is purple)
- 46px height

---

## 🔌 Backend Integration

### Routes
- `GET /pack` → queue
- `GET /pack/{taskId}` → task page
- `POST /pack/scan/{taskId}` → smart scan endpoint (returns type + validation)
- `POST /pack/submit/{taskId}` → finalize (calls `PackTaskService.SubmitAsync`)
- `POST /pack/cancel/{taskId}` → cancel pre-submit

### Smart Scan Endpoint
```
POST /pack/scan/{taskId}
Body: { "code": "SN-A1B2C-X9Y8Z" }

Response (if serial):
{
  "type": "serial",
  "productSerialId": "...",
  "productId": "LAPTOP-X1",
  "productName": "Premium Laptop X1",
  "isSerialTracked": true,
  "validations": [
    {"check": "exists", "passed": true},
    {"check": "allocated", "passed": true},
    {"check": "picked", "passed": true},
    {"check": "notPacked", "passed": true}
  ],
  "errors": []
}

Response (if product):
{
  "type": "product",
  "productId": "PROD-A001",
  "productName": "Premium Widget A",
  "isSerialTracked": false,
  "expectedQty": 5,
  "pickedQty": 5
}

Response (error):
{
  "type": "error",
  "code": "SERIAL_WRONG_ORDER",
  "message": "This serial belongs to a different order",
  "scannedCode": "SN-OTHER-X9Y8Z"
}
```

---

## ⚠️ Audit & Pause Conditions

### Schema Audit Required Before T1

**IMPORTANT — Phase 18 Learning Applied**:
- `IsSerialTracked` column does NOT exist
- Use existing `master.Products.TrackingMethod` enum
- Check: `TrackingMethod == 'LotAndSerial'` for serial-tracked products

Check existence:
- `master.Products.TrackingMethod` enum (should exist — confirmed Phase 18)
- Serial inventory table:
  - Spec assumed `master.ProductSerials` (may not exist)
  - Check actual schema — could be `inventory.LotSerials` or similar
  - Or may not exist at all (TD-040 from Phase 18 noted this)

If serial inventory table **missing**:
- **PAUSE** → user decision needed
- Option A: Add full serial schema (~2-3h extra)
  - May overlap with TD-040 (mobile receive serial) work
  - Worth doing once for both phases
- Option B: Defer serial smart scan to Phase 19.5 (cleaner MVP)
  - Phase 19 ships without serial smart scan
  - Pack flow works for non-serial products only
  - "Use desktop for serial-tracked products" banner
- Option C: Simpler product-only smart scan
  - No serial detection in MVP
  - Just product code → qty entry
  - Defer all serial logic to Phase 19.5

### Pause If
- PackTaskService.SubmitAsync signature changed
- Smart scan endpoint complexity higher than expected
- Validation chain needs DB schema additions
- Build breaks
- Reaching ~7h mark

---

## 📦 Chunk Plan

### T1: PWA Scaffolding (~45 min)
- PackController (mobile partial controller)
- /pack/manifest.json
- Queue view with urgency grouping
- Sidebar entry

### T2: Task Page Layout (~1.5h)
- Carton gradient card
- Scan area
- Packed/pending lists
- Sticky bottom actions

### T3: Smart Scan Backend (~1.5h)
- POST /pack/scan endpoint
- Detection logic (serial first, then product)
- Validation chain
- Response shapes per scenario

### T4: Smart Scan Frontend (~1h)
- Alpine state machine for scenarios
- A/B/C UI rendering
- Validation chain visualization
- Manual entry fallback

### T5: Submit + Tests + Tag (~1h)
- POST /pack/submit
- POST /pack/cancel
- Tests (~10):
  - Queue page renders
  - Task page renders
  - Smart scan: product detected
  - Smart scan: serial detected
  - Smart scan: validation fails
  - Smart scan: not found
  - Submit happy
  - Cancel
  - Permission check
  - 404 invalid task
- Tag v2.5.0-mobile-pack

---

## ✅ Acceptance Criteria

- [ ] /pack route accessible from sidebar
- [ ] Queue groups by urgency with visual separators
- [ ] Filter chips work, no scrollbars
- [ ] Task cards show progress + stats
- [ ] Tap card → task page
- [ ] Carton gradient card displays correctly
- [ ] Scan area accepts both product codes and serials
- [ ] Smart scan detects type correctly
- [ ] Validation chain visible for serial scans
- [ ] Error feedback clear with reasons
- [ ] Submit pack button is GREEN (not purple)
- [ ] Hidden scrollbars throughout
- [ ] PWA installable

---

## 🎬 Smoke Test Scenarios

1. **Queue display**: Tasks grouped by urgency
2. **Task page**: Carton card + scan area + lists
3. **Scan product**: Auto-detected, qty entry shown
4. **Scan serial-tracked product**: Prompts for serial
5. **Scan serial directly**: Auto-detected + validated
6. **Wrong serial**: Error with clear reason
7. **Submit pack**: Green button, finalizes, bounces to queue
8. **Cancel**: Native prompt, reverts
9. **No scrollbars**: Verified throughout

---

## 🔗 Reference

- **Foundation**: `mobile-design-system.md`
- **Pattern source**: Phase 16 picker + Phase 18 receive
- **Desktop equivalent**: `/PackTasks/Detail/{id}`
- **Service**: `IPackTaskService.SubmitAsync`
- **Smart scan precedent**: User-designed mockup (3 scenarios) during Phase 14D wait

---

## 📋 Implementation Notes (Path D — shipped 2026-05-10, tag v2.5.0-mobile-pack)

> Added post-implementation. Documents what was built vs spec.

Pre-implementation audit caught material spec-vs-backend mismatches; user picked **Path D** (per-line card pattern, no scan UI). Built in ~1.5h vs 5h spec estimate.

### Audit findings & locked decisions

1. **Serial inventory table missing** — no `master.ProductSerials`, no `inventory.LotSerials`. Smart-scan-by-serial cannot exist without ~2-3h schema add. **Decision**: Defer to Phase 19.5 (TD-043). Bundle with TD-040 (mobile receive serial entry) + TD-042 (scan-incremental UX) when serial schema lands.
2. **PackTask is 3-state, not 5-state** — `Pending → Packed | Cancelled` only (no `Packing` intermediate). Spec's queue chip "[Pack {N}]", progress bar, and "Resume" CTA assume an InProgress state that doesn't exist. **Decision**: Drop Packing chip + progress bar + Resume. Queue shows Pending only.
3. **TrackingMethod value is `'Lot'`, not `'LotOnly'`** — applied silently.
4. **Pack workflow is batch-submit, not scan-incremental** — `IPackTaskService.SubmitAsync` takes `IReadOnlyList<PackedLineEntry>` + carton metadata in one shot. **Decision**: Mirror Phase 18 receive's per-line card pattern.
5. **Single-carton MVP** per `UX_Cartons_PackTask` UNIQUE — already noted in spec's deferred section.

### Deviations from spec

| Spec said | Built | Why |
|---|---|---|
| Smart scan endpoint + scenarios A/B/C | Not built | Serial schema missing (TD-043) |
| Carton hero card with gradient | Carton metadata strip (green-accent border) | No per-carton state to highlight pre-Submit |
| Active session card with progress | Not built | No "Packing" state (3-state machine) |
| Scan area as primary input | Not built | Scan-incremental UX requires backend (TD-042) |
| Multi-scenario validation chain | Not built | Belongs with smart scan (TD-043) |
| Packed/Pending items lists | Per-line cards instead | Backend is batch-submit not scan-incremental |
| GREEN submit pack button | PURPLE (#534AB7) | Per user direction — matches Phase 18 mobile shell |
| Urgency grouping in queue | Flat FIFO list | No per-task priority/ship-date data |

### Built per spec
- `/pack/` PWA route + manifest with #534AB7 theme
- Queue page (Pending tasks)
- Per-task page with per-line cards
- Carton metadata section
- Sticky-bottom Submit + Cancel
- Native `window.prompt()` for cancel reason
- `.no-scrollbar` throughout
- Bounce-to-queue UX
- Touch targets ≥ 38px (32px quick-adjust borderline same as Phase 18)
- Serial-tracked products show desktop redirect banner (mirrors Phase 18 TD-040)
- "Pack (mobile)" sidebar entry under Outbound

### Tests

15 PackControllerTests cover queue / task page / submit guards / cancel reason gate. Submit happy path IS exercised end-to-end (PackController has no inline service-locator, unlike Phase 18 ReceiveController per TD-041).

### Phase 19.5 candidates (bundle when serial schema lands)
- **TD-040** — Mobile receive serial entry (needs `inventory.LotSerials`)
- **TD-042** — Mobile pack scan-incremental UX
- **TD-043** — Mobile pack smart-scan with serial detection
