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
