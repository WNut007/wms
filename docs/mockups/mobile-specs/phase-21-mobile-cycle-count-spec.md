# Phase 21: Mobile Cycle Count — UI Specification

**Status**: Draft, ready for CC implementation
**Tag**: `v2.7.0-mobile-count`
**Branch**: `feat/mobile-count-pwa`
**Estimated time**: ~4 hours
**Pattern reuse**: ~80% from Phase 16 + Phase 18

> **READ FIRST**: `mobile-design-system.md` (foundation document)

---

## 📋 Scope

### In Scope (MVP)
- `/count/` PWA route
- `/count` queue: active count sessions + pending review
- `/count/{sessionId}` count entry: per-location qty entry
- `/count/{sessionId}/review` variance summary + submit
- `POST /count/save/{sessionId}` (save line, auto-flip status)
- `POST /count/submit/{sessionId}` (submit for approval)
- Sidebar entry: "Cycle Count (mobile)" under Inventory

### Deferred as TDs
- Apply variance via mobile (desktop approval only for MVP)
- Re-count flow (count line again with reason)
- Photo capture per variance
- Multi-counter sessions (collaborative counting)
- Always-focused barcode input
- Service worker offline caching
- PWA icons

---

## 🏗️ Pattern Reuse

**From Phase 16 picker**:
- ✅ PWA scaffolding pattern
- ✅ Per-location cards
- ✅ Sticky-bottom navigation
- ✅ Bounce-to-queue UX

**From Phase 18 receive**:
- ✅ Filter chips
- ✅ Status badges

**From desktop Phase 12 cycle count**:
- ✅ Auto-status-flip logic (Pending → Counted on qty entry)
- ✅ Snapshot stability via 6-tuple denormalisation
- ✅ Variance categories (Match/Short/Over)

**New for Phase 21**:
- 🆕 Side-by-side qty comparison (Expected | Counted)
- 🆕 Variance auto-flag with color coding
- 🆕 Quick-adjust buttons for touch
- 🆕 Stat tiles for variance summary

---

## 📱 Screen Specifications

### Screen 1: Session List (`/count`)

#### Layout

```
┌────────────────────────────┐
│ Status bar                 │
├────────────────────────────┤
│ ← [Cycle counts] {N} active│
│                       [+]  │ ← new session
├────────────────────────────┤
│ [All N] [Counting N] [Rev N]│
├────────────────────────────┤
│ Active sessions:           │
│ ┌────────────────────────┐ │
│ │ CYC-20251002-01[Counting│
│ │ Aisle A · 24 locations │ │
│ │ ▓▓▓▓░░ 15 of 24 (62%)  │ │
│ │ You · 15 min ago        │ │
│ │              [Resume]  │ │
│ └────────────────────────┘ │
│                            │
│ Pending approval:          │
│ ┌────────────────────────┐ │
│ │ CYC-20251001-04 [Review]│
│ │ Aisle B · 18 locations │ │
│ │ [2 short] [1 over]     │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ [+ Start new count session]│ ← action
└────────────────────────────┘
```

#### Header
- Back button
- Title: "Cycle counts"
- Subtitle: "{N} active sessions"
- Right: + button (purple, 30x30px)

#### Filter Chips
- All N
- Counting N (in-progress)
- Review N (pending approval)
- Done N (completed)

#### Section Headings
- "Active sessions" (9px uppercase)
- "Pending approval" (9px uppercase)
- Margin: 12px 4px 6px

#### Session Cards
- White bg, 0.5px border
- Border-left: 3px solid (color by status):
  - `#534AB7` for Counting (active)
  - `#EF9F27` for Review
  - `#1D9E75` for Applied
- Border-radius: 11px
- Padding: 11-12px 13px

**Content**:
- CYC number (11px monospace)
- Status badge (right side)
- Scope description: "Aisle A · 24 locations" (10px)
- Progress bar (if Counting): "15 of 24 counted · 62%"
- Started by + time ago
- For Review: variance summary chips (red short, amber over)
- Resume hint if active

---

### Screen 2: Count Entry (`/count/{sessionId}`)

#### Layout

```
┌────────────────────────────┐
│ ← Counts › CYC-20251002-01 │
│ Count entry                │
│ Location 16 of 24 [15/24]  │
│ ▓▓▓▓░░ progress             │
├────────────────────────────┤
│ Currently counting:        │
│ ┌────────────────────────┐ │
│ │ 📍 A-03-15-B           │ │ ← BIG location
│ │ Aisle A · Rack 03      │ │
│ │ ────────────────────── │ │
│ │ 📦 PROD-A001           │ │
│ │ Premium Widget A       │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ ┌──────────┬──────────┐    │
│ │ Expected │ Counted  │    │ ← 2-col side by side
│ │   50     │   48     │    │
│ │ ea       │ ea       │    │
│ │ [system] │ [tap]    │    │
│ └──────────┴──────────┘    │
│ ┌────────────────────────┐ │
│ │ ⬇ −2 ea variance · 4%  │ │ ← variance flag
│ │   short                │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ [−1] [+1] [−10] [+10]      │ quick adjust
│ [Skip this location]       │
├────────────────────────────┤
│ [Save & next location →]   │
└────────────────────────────┘
```

#### Breadcrumb Header
- Back button (22x22px)
- "Counts" › "CYC-{number}" (font-mono)
- Title: "Count entry"
- Subtitle: "Location 16 of 24"
- Pill: "15 / 24" (purple-light bg)
- Progress bar (3px height, primary fill)

#### Currently Counting Card
- White bg, border 0.5px solid `#534AB7` (purple = active)
- Border-radius: 11px
- Padding: 11-12px

**Content**:
- Map-pin icon (purple, in primary-light circle 36x36px)
- Big location code (14-16px monospace, primary purple)
- Hierarchy: "Aisle A · Rack 03 · Bin 15B" (9-10px)
- Divider line (0.5px)
- Item info (package icon + product code + name)

#### Side-by-Side Quantity (2-col grid)

**Expected tile** (left, gray):
- Background: `#f9fafb`
- Border: 0.5px solid `#e5e7eb`
- Border-radius: 9px
- Padding: 10px
- Label: "Expected" (9px uppercase, gray)
- Value: 22px font-weight 500, gray (`#6b7280`)
- Unit: "ea" (11px, font-weight 400)
- Hint: "From system" (9px, lighter gray)

**Counted tile** (right, purple highlighted):
- Background: white
- Border: 1.5px solid `#534AB7` (thicker = focus)
- Border-radius: 9px
- Padding: 10px
- Label: "Counted" (9px uppercase, purple)
- Input: 22px font-weight 500, purple
- Unit: "ea" (11px)
- Hint: "Tap to edit" (9px, purple)

#### Variance Indicator
Below qty grid:
- Background by category:
  - Match: `#E1F5EE` border-left `#1D9E75`
  - Short: `#FCEBEB` border-left `#E24B4A`
  - Over: `#FAEEDA` border-left `#EF9F27`
- Border-radius: 8px
- Padding: 9px 11px
- Icon (left, 14px)
- Text: "−2 ea variance · 4% short"

**Auto-flip status**:
- Empty input → status "Pending"
- Type qty → "Counted" (auto-flip via Alpine `@input`)
- Clear field → revert to "Pending"

#### Quick Adjust Buttons
4 buttons in flex row:
- −1, +1, −10, +10
- Each: flex 1, height 32px, white bg, border 0.5px
- Tapping increments/decrements counted qty
- Updates variance immediately

#### Skip Button
- Below quick adjust
- Width 100%, height 32px
- Background `#f9fafb`, border 0.5px
- Text: "Skip this location"
- Tap → mark as skipped, advance

#### Sticky Bottom
- "Save & next location →"
- 46px, primary purple, white text
- Saves current entry + advances to next location
- Last location: button changes to "Save & review"

---

### Screen 3: Variance Review (`/count/{sessionId}/review`)

#### Layout

```
┌────────────────────────────┐
│ ← Counts › CYC-20251002-01 │
│ Submit for review [Complete│
├────────────────────────────┤
│ Stat tiles:                │
│ [Match:19][Short:3][Over:2]│
├────────────────────────────┤
│ Variances (5):              │
│ ┌────────────────────────┐ │
│ │ A-03-15-B   [⬇ −2 ea]  │ │
│ │ PROD-A001 · 50 → 48    │ │
│ └────────────────────────┘ │
│ ┌────────────────────────┐ │
│ │ A-04-08-A   [⬇ −5 ea]  │ │
│ │ PROD-B002 · 30 → 25    │ │
│ └────────────────────────┘ │
│ ┌────────────────────────┐ │
│ │ A-05-11-C   [⬆ +3 ea]  │ │
│ │ PROD-C003 · 15 → 18    │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ Notes (optional):          │
│ [textarea]                 │
├────────────────────────────┤
│ [Submit for review]        │ ← purple primary
│ [Save as draft]            │
└────────────────────────────┘
```

#### Header
- Breadcrumb path
- Title: "Submit for review"
- Subtitle: "All locations counted"
- Status badge: "Complete" (green)

#### Stat Tiles (3-col grid)

**Match tile**:
- White bg, border 0.5px
- Border-left: 3px solid `#1D9E75`
- Label: "MATCH" (8px uppercase)
- Number: large (18px, success-dark)

**Short tile**:
- Border-left: 3px solid `#E24B4A`
- Number: error-dark color

**Over tile**:
- Border-left: 3px solid `#EF9F27`
- Number: warning-dark color

#### Variance List
Section heading: "Variances ({N})"

Each variance card:
- White bg, 0.5px border
- Border-left: 3px solid (red short / amber over / hidden if match)
- Border-radius: 9px
- Padding: 9px 11px

**Content**:
- Top row: location code (font-mono, font-weight 500) + variance badge (right)
- Bottom row: product · expected → counted

#### Notes Section
- Textarea (min 50px height)
- Background: `#f9fafb`
- Border: 0.5px
- Padding: 8px 10px
- Placeholder: "Explain variance reasons..."
- Optional but encouraged for variances

#### Sticky Bottom
- **Primary**: "Submit for review" (purple, 46px)
  - Submits to approval workflow (desktop approves apply)
- **Secondary**: "Save as draft" (white, 34px)
  - Saves state, returns to queue

---

## 🔌 Backend Integration

### Routes
- `GET /count` → queue
- `GET /count/{sessionId}` → entry page (next pending location)
- `GET /count/{sessionId}/location/{locationId}` → specific location
- `POST /count/save/{sessionId}` → save line + auto-flip
- `GET /count/{sessionId}/review` → variance summary
- `POST /count/submit/{sessionId}` → submit for approval

### Reuses Existing
- Phase 12 cycle count service
- Auto-flip status logic (already in desktop)
- Variance categorization (Match/Short/Over)
- Snapshot stability (6-tuple denorm)

---

## ⚠️ Audit & Pause Conditions

### Audit Required Before T1
Check existing:
- Phase 12 cycle count service surface
- CycleCountSession + CycleCountLine entities
- Auto-flip status logic location

### Pause If
- Cycle count service signature changed
- New session creation flow needs design
- Build breaks
- Reaching ~5h mark

---

## 📦 Chunk Plan

### T1: PWA Scaffolding (~45 min)
- CountController (mobile partial)
- Manifest
- Queue view
- Sidebar entry under Inventory

### T2: Count Entry Page (~1.5h)
- Currently counting card
- Side-by-side qty grid
- Variance indicator
- Quick adjust buttons
- Skip option
- Sticky bottom navigation

### T3: Variance Review Page (~1h)
- Stat tiles (3-col)
- Variance list
- Notes textarea
- Sticky bottom actions

### T4: Endpoints + Tests + Tag (~45 min)
- POST /count/save (with auto-flip)
- POST /count/submit (review approval)
- ~8 tests
- Tag v2.7.0-mobile-count

---

## ✅ Acceptance Criteria

- [ ] /count accessible from sidebar
- [ ] Queue shows active + review sessions
- [ ] Filter chips work, no horizontal scrollbar
- [ ] Tap session → entry page
- [ ] Side-by-side qty visible (Expected | Counted)
- [ ] Variance auto-flags with color
- [ ] Quick adjust works (−1/+1/−10/+10)
- [ ] Auto-flip status on qty entry
- [ ] Skip option works
- [ ] Variance review shows stat tiles + list
- [ ] Notes captured optional
- [ ] Submit for review works
- [ ] Hidden scrollbars throughout
- [ ] PWA installable

---

## 🎬 Smoke Test Scenarios

1. **Queue**: Active sessions + pending review visible
2. **Entry**: Side-by-side qty, type counted → variance flag
3. **Match**: Expected = counted → green check
4. **Short**: Counted < expected → red badge
5. **Over**: Counted > expected → amber badge
6. **Quick adjust**: −1 button decrements, variance updates
7. **Skip**: Mark skipped, advance
8. **Auto-flip**: Type qty → status "Counted", clear → "Pending"
9. **Review**: Stat tiles match list counts
10. **Submit**: Goes to approval queue (desktop)
11. **No scrollbars**: Verified

---

## 🔗 Reference

- **Foundation**: `mobile-design-system.md`
- **Pattern source**: Phase 16 picker + Phase 18 receive
- **Desktop equivalent**: Phase 12 `/CycleCounts/{id}`
- **Service**: Phase 12 cycle count service (auto-flip logic reused)

---

## 📋 Implementation Notes (Scenario A — shipped 2026-05-11, tag v2.7.0-mobile-count)

> Added post-implementation. Documents what was built vs spec.

Pre-implementation audit confirmed clean **Scenario A** — Phase 12 service surface is well-aligned with the spec. **No spec rename triggered** this phase (4th-instance memory check via `feedback_spec_rename_audit.md` passed clean). Built in ~1.5h vs 4h spec estimate.

### Audit findings & locked decisions

1. ✅ `ICycleCountService` exists with full surface (`CreateAsync`, `SaveCountedQuantitiesAsync`, `SubmitForReviewAsync`, `ApproveAndApplyAsync`, `CancelAsync`, `GetByIdAsync`).
2. ✅ State machine `Counting → Review → Applied | Cancelled` matches spec.
3. ✅ `CountLineUpdate(LineId, CountedQuantity, LineStatus, Notes)` matches per-line save shape exactly.
4. ✅ `CYC-YYYYMMDD-NNNN` number format + LineStatus enum match spec.
5. ✅ `ICycleCountRepository.GetLineRowsByIdAsync` provides JOIN-resolved per-line projection (ProductCode, ProductName, LocationCode, UomCode, OwnerCode, LotNumber, PalletNumber).
6. ❌ No Phase 1 mobile cycle count surface — purely additive (no retirement).
7. ❌ No `/count/` route exists today — clean room.

### One UX deviation from spec

Spec describes a **per-location wizard** ("Location 16 of 24" with `[Save & next location →]`). Built **all-lines-on-one-page** for mental-model consistency with Phase 18-20. Per-location wizard = **TD-044** candidate.

Reasoning: operator typically pre-walks the aisle physically + types quantities at the end, not card-by-card with the device. The all-lines view also makes variance review easier (all visible at once for cross-comparison).

### Deviations from spec

| Spec said | Built | Why |
|---|---|---|
| Per-location wizard with "Location 16 of 24" + Save & next | All-lines-on-one-page | Phase 18-20 mental-model consistency; operator pre-walks aisle + types at end |
| Filter chips (All/Counting/Review/Done) on queue | 2-section layout (Active sessions + Pending approval) | Sufficient for MVP; adding Done/Cancelled would require extra paged calls + chip state |
| Session-level Notes on review page | Per-line Notes only | Operator can use per-line Notes; session-level Notes is a small future addition |
| Apply variance via mobile | Desktop-only (Review → Applied) | Spec MVP itself defers this; separation of duties enforced (counter ≠ approver) |
| GREEN submit button | PURPLE (#534AB7) | Per Phase 19 user direction — matches mobile shell consistently |

### Built per spec

- /count/ PWA route + manifest with #534AB7 theme
- Queue page with Counting + Review sections
- Per-task page with location card + side-by-side qty grid (Expected | Counted)
- Variance auto-flag with color coding (✓ Match / ↓ Short / ↑ Over / ⊘ Skipped)
- Quick-adjust buttons (-1 / +1 / -10 / +10)
- Auto-flip status on qty entry (Phase 12 desktop pattern)
- Skip toggle (with un-skip)
- Stat tiles for Review state (3-col Match/Short/Over)
- Sticky-bottom Submit + Save progress + Cancel
- "Cycle Count (mobile)" sidebar entry under Counts module

### Form architecture

TWO forms in DOM (`count-form` for Save, `submit-form` for Submit), each line's hidden inputs wired to BOTH via the `form="form-id"` attribute. Operator clicks the right button → that form posts. Avoids JS form-mutation hacks.

### Tests

16 CountControllerTests cover queue partition / task 404 paths / Counting + Review render / Save happy + service-throws / Submit happy (saves THEN submits) + idempotent + skip-Save-when-no-lines / Cancel reason-gate + happy + idempotent. Submit happy path IS exercised end-to-end (constructor injection from start).

### TD candidates
- **TD-044** — Per-location wizard with progress bar (alternative UX to all-lines view)
- Filter chips (All/Counting/Review/Done) on queue — extra paged calls + chip state
- Session-level Notes on review page
- Re-count flow with reason capture
- Photo capture per variance (bundle with ADR-009 pack-video infrastructure)
- Multi-counter sessions
- Always-focused barcode input
- Service worker offline caching
- PWA icons
