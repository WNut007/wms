# Phase 20: Mobile Putaway — UI Specification

**Status**: Draft, ready for CC implementation
**Tag**: `v2.6.0-mobile-putaway`
**Branch**: `feat/mobile-putaway-pwa`
**Estimated time**: ~3-4 hours
**Pattern reuse**: ~75% from Phase 16 + Phase 18

> **READ FIRST**: `mobile-design-system.md` (foundation document)

---

## 📋 Scope

### In Scope (MVP)
- `/putaway/` PWA route
- `/putaway` queue: items waiting for putaway (post-receipt staging)
- `/putaway/{itemId}` task page: scan item → suggested location → confirm
- `POST /putaway/submit/{itemId}` (calls existing putaway service)
- Sidebar entry: "Putaway (mobile)" under Inbound

### Deferred as TDs
- Multi-location split (one item to multiple bins)
- Override location with reason capture
- Putaway batches (bulk move scenario)
- Smart routing (closest available bin algorithm)
- Service worker offline caching
- PWA icons

---

## 🏗️ Pattern Reuse

**From Phase 16 picker**:
- ✅ PWA scaffolding pattern
- ✅ Big location display (hero element)
- ✅ Sticky-bottom submit
- ✅ Bounce-to-queue UX

**From Phase 18 receive**:
- ✅ Item card layout
- ✅ Status badges
- ✅ Filter chips

**New for Phase 20**:
- 🆕 Suggested location card (semantic green)
- 🆕 Item-then-location 2-step flow

---

## 📱 Screen Specifications

### Screen 1: Putaway Queue (`/putaway`)

#### Layout

```
┌────────────────────────────┐
│ Status bar                 │
├────────────────────────────┤
│ ← [Putaway] {N} items      │
├────────────────────────────┤
│ [All N] [Today N] [Aged N] │ chip row
├────────────────────────────┤
│ ┌────────────────────────┐ │
│ │ 📦 PROD-A001           │ │
│ │ Premium Widget A · 50ea│ │
│ │ From PO-2025-001       │ │
│ │ STAGE-01 (inbound)     │ │
│ │ 2 hours waiting        │ │
│ │              [→]       │ │
│ └────────────────────────┘ │
│  ...more cards              │
├────────────────────────────┤
│ [Scan item to start]       │ ← action
├────────────────────────────┤
│ Tab bar                    │
└────────────────────────────┘
```

#### Header
- Back button: 30x30px
- Title: "Putaway"
- Subtitle: "{N} items awaiting putaway"

#### Filter Chips (no scrollbar)
- All N (default active)
- Today N (received today)
- Aged N (>24h waiting, urgent flag)

#### Item Cards
- Background: white
- Border: 0.5px solid `#e5e7eb`
- Border-left: 3px solid `#EF9F27` if aged (>24h)
- Border-radius: 11px
- Padding: 11px 13px
- Margin: 8px

**Content**:
- Icon: package-fill (purple if active)
- Product code (12px monospace)
- Product name + qty (10px)
- Origin: "From {PO/Source}" (9px)
- Current location: "STAGE-01" (10px font-mono)
- Wait time: "{hours}h waiting" (9px, amber if aged)
- Right: chevron-right

#### Bottom Action
"Scan item to start" → opens scanner modal/camera

---

### Screen 2: Putaway Task (`/putaway/{itemId}`)

#### Layout

```
┌────────────────────────────┐
│ ← Putaway › PROD-A001      │
├────────────────────────────┤
│ ┌────────────────────────┐ │
│ │ Item card              │ │
│ │ 📦 PROD-A001           │ │
│ │ Premium Widget A       │ │
│ │ Qty: 50 ea             │ │
│ │ From: STAGE-01         │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ Suggested location:        │
│ ┌────────────────────────┐ │
│ │ ✓ A-03-15-B            │ │ ← BIG location
│ │   Aisle A · Rack 03    │ │   green accent
│ │   Bin 15B · 85% full   │ │
│ │                        │ │
│ │ Why this location:     │ │
│ │ • Same product nearby  │ │
│ │ • FEFO compliant       │ │
│ │ • Capacity available   │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ Or scan a different bin:   │
│ ┌────────────────────────┐ │
│ │ Scan area (dashed)     │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ [Confirm putaway]          │ ← purple primary
│ [Override location]        │ ← secondary
└────────────────────────────┘
```

#### Item Card (top)
Standard item card pattern:
- Background: white
- Border-left: 3px solid `#534AB7`
- Show: product code, name, qty, source location

#### Suggested Location Card (HERO)
- Background: `#E1F5EE` (success-light)
- Border: 0.5px solid `#1D9E75`
- Border-left: 3px solid `#1D9E75`
- Border-radius: 12px
- Padding: 12-14px

**Inside**:
- Check icon (success green) + "Suggested location" label (10px uppercase)
- Big location code (16-22px font-mono font-weight 500, primary purple)
- Hierarchy breakdown (10px text-secondary): "Aisle A · Rack 03 · Bin 15B"
- Capacity hint: "{N}% full" (amber if >80%)
- Reasons list (3-4 bullets):
  - "Same product nearby" (FIFO/FEFO)
  - "FEFO compliant" (lot age)
  - "Capacity available" (room for qty)
  - "Pick zone match" (forward picking)

#### Alternative Scan Area
Below suggested:
- Standard scan area (2px dashed primary border)
- Manual location input
- "Or scan a different bin" hint label

#### Sticky Bottom Actions
- **Primary**: "Confirm putaway" (purple, 46px)
  - Submits with suggested location
- **Secondary**: "Override location"
  - Opens scan area focus
  - Or shows location picker (future TD)

---

### Screen 3: Putaway Confirm (modal/overlay)

After tap "Confirm putaway":
- Brief confirmation overlay
- "✅ Moved to A-03-15-B"
- Auto-dismiss after 1.5s
- Bounces to queue

---

## 🔌 Backend Integration

### Routes
- `GET /putaway` → queue
- `GET /putaway/{itemId}` → task page
- `POST /putaway/submit/{itemId}` → confirm putaway
- `GET /putaway/suggest/{itemId}` → suggested location API

### Suggested Location Logic
Existing service or new endpoint that returns:
```json
{
  "locationId": "...",
  "locationCode": "A-03-15-B",
  "hierarchy": "Aisle A · Rack 03 · Bin 15B",
  "capacityPercent": 85,
  "reasons": [
    "Same product nearby",
    "FEFO compliant",
    "Capacity available"
  ]
}
```

If no suggestion available:
- Show "No suggestion. Scan a bin to put away."
- Hide suggestion card
- Focus on scan area

---

## ⚠️ Audit & Pause Conditions

### Audit Required Before T1
Check existence of:
- Putaway service / staging concept
- `IsStaging` flag on locations table
- Receipt → staging item flow

If putaway service **missing or partial**:
- **PAUSE** → user decision needed
- This was noted as TD-004/005 (ADR-004 Putaway header)
- May need full putaway service implementation first
- Or: scope MVP to "move from staging X to bin Y" without batch concept

### Pause If
- ADR-004 Putaway header not implemented yet
- Suggested location algorithm needs design
- Build breaks
- Reaching ~5h mark

---

## 📦 Chunk Plan

### T1: PWA Scaffolding (~45 min)
- PutawayController
- Manifest
- Queue view
- Sidebar entry

### T2: Task Page (~1.5h)
- Item card display
- Suggested location card
- Scan area for override
- Sticky-bottom actions

### T3: Suggested Location API (~1h)
- GET /putaway/suggest/{itemId}
- Logic: same-product, FEFO, capacity
- Return null if no suggestion

### T4: Submit Endpoint + Tests + Tag (~45 min)
- POST /putaway/submit
- ~6 tests
- Tag v2.6.0-mobile-putaway

---

## ✅ Acceptance Criteria

- [ ] /putaway accessible from sidebar
- [ ] Queue shows staging items
- [ ] Filter chips work, no horizontal scrollbar
- [ ] Tap item → task page
- [ ] Suggested location card displays prominently (green)
- [ ] Reasons listed clearly
- [ ] Override scan area available
- [ ] Confirm putaway → success → bounce to queue
- [ ] Hidden scrollbars throughout
- [ ] Touch targets ≥ 38px

---

## 🎬 Smoke Test Scenarios

1. **Queue display**: Items in staging show
2. **Aged badge**: Items >24h have amber flag
3. **Tap item**: Task page with suggested location
4. **Confirm**: Item moves, location updates, bounce
5. **Override**: Scan different bin, confirm
6. **No scrollbars**: Verified throughout

---

## 🔗 Reference

- **Foundation**: `mobile-design-system.md`
- **Pattern source**: Phase 16 picker + Phase 18 receive
- **ADR**: ADR-004 Putaway header (may need implementation)
- **Desktop equivalent**: TBD (may not exist as desktop yet)

---

## 📋 Implementation Notes (Scenario A — shipped 2026-05-11, tag v2.6.0-mobile-putaway)

> Added post-implementation. Documents what was built vs spec.

Pre-implementation audit confirmed **Scenario A** with one apply-silently rename. Putaway service exists, well-defined; staging concept exists in the schema under a different name. Built in ~2h vs 3-4h spec estimate.

### Audit findings & locked decisions

1. **`master.Locations.IsStaging` does not exist.** Reality: `master.Zones.Type` enum has `'Receiving' | 'Storage' | 'Picking' | 'Packing' | 'Shipping' | 'Staging' | 'Quarantine' | 'Returns'`. Filter via `Zone.Type IN ('Receiving','Staging')` — **applied silently**. 3rd consecutive instance of the same audit→rename pattern (Phase 18: `IsSerialTracked` → `TrackingMethod`; Phase 19: `'LotOnly'` → `'Lot'`).
2. **`IPutawayService.PutawayStockAsync` exists** — atomic source→dest move via `IStockRepository.TransferStockAsync` + paired `StockMovements` writes per ADR-014. Reused as-is.
3. **No `PutawayTask` header/lines table.** Queue derived from Stock at staging zones. No migration this phase. ADR-004 putaway header (TD-004) remains future work.
4. **No suggested-location service.** Built inline as `IStockRepository.GetSuggestedPutawayLocationAsync` using existing `BinRank`, `IsPickface`, `ZoneId`, `Status` columns.
5. **Phase 1 PutawayController + form** retired per Phase 18 Decision 3A precedent.

### Suggested-location algorithm (built)

Storage-zone candidates (`Zone.Type='Storage'`, `IsActive=1`, `Status='Active'`) scored by:
1. **SameProductRowCount DESC** — cluster picks (existing same-product Stock at the location → raises pick-face hit rate)
2. **BinRank ASC** — BC pattern: lower fills first
3. **IsPickface ASC** — preserve dedicated pick faces for pulls (avoid putting away to pick faces if alternatives exist)

Returns null when no Storage-zone location qualifies.

### Deviations from spec

| Spec said | Built | Why |
|---|---|---|
| `master.Locations.IsStaging` flag | Filter via `Zone.Type IN ('Receiving','Staging')` | Capability exists under different name (3rd instance) |
| "Pick zone match" reason bullet | "Pick face (last-resort target)" | Putaway should AVOID pick faces (operator pulls, not pushes). Inverted the meaning. |
| "Capacity available" reason bullet | Deferred (TD) | Needs product-volume data not seeded today |
| "Capacity: {N}% full" hint | Deferred (TD) | Same — no per-location current vs max calc |
| Smart routing (closest available bin) | Not built | No location-distance algorithm; nearest-bin would need PositionX/Y/Z calc per candidate |
| GREEN submit button | PURPLE (#534AB7) | Per Phase 19 user direction — green is for the suggestion card |

### Built per spec
- `/putaway/` PWA route + manifest with #534AB7 theme (was #1f2937)
- Queue page (Stock at staging-zone locations, FIFO)
- Filter chips (All / Today / Aged) with counts and `.no-scrollbar`
- Aged badge (>24h waiting flagged amber border-left)
- Per-task page with item card (purple-accent) + suggested-location hero (green-accent) + override scan area + sticky-bottom Submit
- Reasons listed as ✓ green bullets in suggestion card
- Override scan area with dashed-purple input
- Bounce-to-queue UX
- "Putaway (mobile)" sidebar entry under Inbound

### Tests

14 PutawayControllerTests cover queue / task page (4 paths: not-found / empty / not-in-queue / happy with-and-without suggestion) / submit (zero-qty / not-found / no-target-no-suggestion / happy with suggestion / service throws). Submit's suggestion-fallback path IS exercised end-to-end. Override-code path uses inline `ITenantConnectionFactory` (TD-041 family) and is not unit-tested.

### Phase 19.5 / TD candidates (already-tracked + new)
- **TD-004** — ADR-004 putaway header (PutawayTask + PutawayTaskLine schema)
- **Capacity-aware ranking** (no TD logged separately yet; needs product-volume data first)
- **Smart routing** (closest-bin via PositionX/Y/Z fields seeded for ADR-011 3D viz)
- **Override-with-reason audit** (operator-supplied "why I overrode the suggestion"; needs ADR-004 header to land first)
- **Multi-location split** (one Stock split across multiple bins)
- **Reserve-on-tap** (mobile claims a queue row when operator opens it; today's race is "tap → other operator drains it → 404 on submit", which is friction-only since CK rejects insufficient stock cleanly)
