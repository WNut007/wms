# Phase 22: Mobile Locate — UI Specification

**Status**: Draft, ready for CC implementation
**Tag**: `v2.8.0-mobile-locate`
**Branch**: `feat/mobile-locate-pwa`
**Estimated time**: ~3-4 hours
**Pattern reuse**: ~70% from Phase 16 + earlier mobile phases

> **READ FIRST**: `mobile-design-system.md` (foundation document)

---

## 📋 Scope

### In Scope (MVP)
- `/locate/` PWA route (general lookup utility, not workflow)
- `/locate` search entry: scan or type product/location code
- `/locate/item/{productId}` → multi-location view for product
- `/locate/loc/{locationId}` → contents of location
- `GET /locate/search?q=...` → smart search endpoint (auto-detect type)
- Sidebar entry: "Locate (mobile)" under Inventory or as utility

### Deferred as TDs
- Movement history view (audit trail per item/location)
- Trigger ad-hoc count from location (link to Phase 21 count)
- Favorites/saved searches per user
- Recently viewed (per-user history)
- Photo of location (for visual confirmation)
- Service worker offline caching
- PWA icons

---

## 🏗️ Pattern Reuse

**From Phase 16 picker**:
- ✅ PWA scaffolding pattern
- ✅ Big location display
- ✅ Sticky-bottom action

**From Phase 18 receive**:
- ✅ Item card layout
- ✅ Status badges

**From all phases**:
- ✅ Scan area pattern
- ✅ Filter chips (no scrollbar)

**New for Phase 22**:
- 🆕 Multi-purpose search (item OR location auto-detect)
- 🆕 Multi-location view per product
- 🆕 Recent + Favorites lists

---

## 📱 Screen Specifications

### Screen 1: Search Entry (`/locate`)

#### Layout

```
┌────────────────────────────┐
│ Status bar                 │
├────────────────────────────┤
│ ← [Locate]                 │
│ Find any item or location  │
├────────────────────────────┤
│ 🔍 [Product code, name,    │
│      location...        ]  │
├────────────────────────────┤
│ ┌────────────────────────┐ │
│ │ Big scan area (dashed) │ │
│ │                        │ │
│ │   📷 Scan barcode      │ │
│ │   Product, location,   │ │
│ │   lot, or pallet       │ │
│ │                        │ │
│ │   [Open camera]        │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ Recent searches:            │
│ 🕐 PROD-A001 · 15 min ago  │
│ 🕐 A-03-15-B · 1h ago      │
│ 🕐 LAPTOP-X1 · Yesterday   │
│ Frequently looked up:       │
│ ⭐ PROD-B002                │
├────────────────────────────┤
│ Tab bar                    │
└────────────────────────────┘
```

#### Header
- Back button: 30x30px
- Title: "Locate"
- Subtitle: "Find any item or location"
- No right action (could add filter for advanced search)

#### Search Bar
- Position: top of content area
- Height: 44px
- Padding: 0 14px 0 38px (left padding for icon)
- Icon: search icon, position absolute left 12px
- Background: `#f9fafb`
- Border: 0.5px solid `#e5e7eb`
- Border-radius: 11px
- Placeholder: "Product code, name, location..."
- Font-size: 13px

#### Big Scan Area (HERO)
- Background: `#f9fafb`
- Border: 2px dashed `#534AB7`
- Border-radius: 12px
- Padding: 26px 14px

**Inside**:
- Big icon circle: 56x56px, `#EEEDFE` bg, scan icon 30px purple
- Title: "Scan barcode" (13px font-weight 500)
- Subtitle: "Product, location, lot, or pallet" (10px text-secondary)
- "Open camera" button (full width, 38px, primary purple)

#### Recent Searches Section
- Heading: "Recent searches" (9px uppercase font-weight 500)

Each item:
- White bg, 0.5px border, 9px border-radius
- Padding: 10px 12px
- Margin: 5px between
- Layout: history-icon + content + arrow-up-left (re-search)

**Content**:
- Code (11px font-weight 500, monospace)
- Type + time: "Product · 15 min ago" (9px text-secondary)

Recent items stored client-side or per-user.

#### Frequently Looked Up Section
- Heading: "Frequently looked up" (9px uppercase)
- Same card structure as recent
- Star icon (amber `#EF9F27`) instead of history
- Auto-populated based on lookup frequency

---

### Screen 2: Item Result (`/locate/item/{productId}`)

#### Layout

```
┌────────────────────────────┐
│ ← Locate › PROD-A001       │
│ Item found · 4 locations   │
│                       [⭐]  │ ← favorite toggle
├────────────────────────────┤
│ Product card:              │
│ ┌────────────────────────┐ │
│ │ 📦 PROD-A001           │ │
│ │ Premium Widget A       │ │
│ │ ──────────────────     │ │
│ │ [Total][Avail][Alloc]  │ │ ← stat tiles
│ │  125    98     27      │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ Locations (4):              │
│ ┌────────────────────────┐ │
│ │ 📍 A-03-15-B [Bin]     │ │ ← active location
│ │ Aisle A · Lot L20251002│ │   border-left purple
│ │ 12 days old            │ │
│ │              50 ea     │ │
│ └────────────────────────┘ │
│ ┌────────────────────────┐ │
│ │ 📍 A-04-08-A [Bin]     │ │
│ │ Aisle A · Lot L20250915│ │
│ │ 27 days old            │ │
│ │              35 ea     │ │
│ └────────────────────────┘ │
│ ┌────────────────────────┐ │
│ │ 📍 B-02-04-C [Allocate│ │ ← amber accent
│ │ For SO-12345           │ │
│ │ Picking pending        │ │
│ │              27 ea     │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ [View movement history]    │ ← optional action
└────────────────────────────┘
```

#### Header
- Breadcrumb: "Locate" › product code (font-mono)
- Title: "Item found"
- Subtitle: "In {N} locations"
- Right: Star icon (toggle favorite, 30x30px primary-light bg)

#### Product Card
- White bg, 0.5px border, 11px radius
- Padding: 12px

**Top row**:
- Icon (44x44px, primary-light, package-fill icon 22px purple)
- Product code (12px font-weight 500 monospace)
- Product name (10px text-secondary)

**Stat grid (3-col)**:
- Total (gray): "125 ea"
- Available (green): "98 ea"
- Allocated (amber): "27 ea"
- Each tile: bg `#f9fafb`, 6px radius, padding 5px 7px
- Label: 8px uppercase
- Value: 12px font-weight 500

#### Location List
- Heading: "Locations ({N})" (9px uppercase)

Each location:
- White bg, 0.5px border
- Border-left: 3px (color by status):
  - Purple `#534AB7`: pickable bin (default)
  - Amber `#EF9F27`: allocated (reserved)
  - Blue `#185FA5`: staging
  - Gray: inactive/special
- Border-radius: 11px
- Padding: 11-12px

**Layout** (icon + content + trailing):
- Map-pin icon (36x36px circle, color by status)
- Content:
  - Location code + type badge (12px monospace)
  - Sub-line: aisle + lot info (9px)
  - Lot age: "12 days old" (FEFO awareness)
- Trailing: qty + label

**Type badges**:
- Bin: primary-light bg
- Allocated: amber-light bg
- Staging: info-light bg
- Hold: gray bg

#### Bottom Action
- "View movement history" button (purple primary, 42px)
- Future TD: actual implementation

---

### Screen 3: Location Detail (`/locate/loc/{locationId}`)

#### Layout

```
┌────────────────────────────┐
│ ← Locate › A-03-15-B       │
│ Location detail [Active]   │
│ {N} items in this bin      │
├────────────────────────────┤
│ Location card:             │
│ ┌────────────────────────┐ │
│ │ 📍 A-03-15-B           │ │ ← BIG (hero)
│ │ Aisle A · Rack 03      │ │
│ │ Bay 15 · Bin B         │ │
│ │ ──────────────────     │ │
│ │ [Type: Pickable bin]   │ │ ← stat tiles (2-col)
│ │ [Capacity: 85% full]   │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ Items at this location:    │
│ ┌────────────────────────┐ │
│ │ 📦 PROD-A001           │ │
│ │ Premium Widget A       │ │
│ │ Lot L20251002          │ │
│ │              50 ea     │ │
│ └────────────────────────┘ │
│ ┌────────────────────────┐ │
│ │ 📦 PROD-X042           │ │
│ │ Standard Component X   │ │
│ │ Lot L20250920          │ │
│ │              22 ea     │ │
│ └────────────────────────┘ │
├────────────────────────────┤
│ [Start cycle count here]   │ ← purple primary
│ [View activity]            │ ← secondary
└────────────────────────────┘
```

#### Header
- Breadcrumb: "Locate" › location code
- Title: "Location detail"
- Subtitle: "{N} items in this bin"
- Right: Status badge ("Active" green)

#### Location Card (HERO)
- White bg
- Border: 0.5px solid `#534AB7`
- Border-radius: 11px
- Padding: 12px

**Top row**:
- Map-pin icon (44x44px, primary-light bg)
- Location code (16-22px font-mono, primary purple)
- Hierarchy: "Aisle A · Rack 03 · Bay 15 · Bin B" (10px)

**Stat grid (2-col)**:
- Type: "Pickable bin"
- Capacity: "85% full" (amber if >80%)
- Each tile: bg `#f9fafb`, 6px radius
- Label: 8px uppercase, value: 11px font-weight 500

#### Items List
- Heading: "Items at this location" (9px uppercase)
- Each item: standard item card pattern
  - Package icon (32x32px, gray bg)
  - Product code + name
  - Lot info
  - Qty (right side)

#### Bottom Actions
- **Primary**: "Start cycle count here" (purple, 42px)
  - Links to Phase 21 count flow
  - Pre-populates location
- **Secondary**: "View activity" (white, 34px)
  - Future TD: movement history

---

## 🔌 Backend Integration

### Routes
- `GET /locate` → search entry
- `GET /locate/search?q={code}` → smart search (returns type + redirect)
- `GET /locate/item/{productId}` → multi-location view
- `GET /locate/loc/{locationId}` → location detail
- `POST /locate/favorite/{type}/{id}` → toggle favorite

### Smart Search Endpoint
```
GET /locate/search?q=PROD-A001

Response:
{
  "type": "product",
  "id": "...",
  "redirectTo": "/locate/item/..."
}

GET /locate/search?q=A-03-15-B

Response:
{
  "type": "location",
  "id": "...",
  "redirectTo": "/locate/loc/..."
}

GET /locate/search?q=SN-A1B2C-X9Y8Z

Response:
{
  "type": "serial",
  "productId": "...",
  "currentLocationId": "...",
  "redirectTo": "/locate/item/..."
}

GET /locate/search?q=NOTFOUND

Response:
{
  "type": "not_found",
  "scannedCode": "NOTFOUND",
  "suggestions": []
}
```

---

## ⚠️ Audit & Pause Conditions

### Audit Required Before T1
Check existing:
- Search/lookup services
- Stock model (current location, allocated, etc.)
- Lot age calculation logic

### Pause If
- Smart search routing complex (multi-table)
- Recent/Favorites need new schema
- Build breaks
- Reaching ~5h mark

---

## 📦 Chunk Plan

### T1: PWA Scaffolding (~45 min)
- LocateController (mobile partial)
- Manifest
- Search entry view
- Sidebar entry (Inventory or utility)

### T2: Smart Search Endpoint (~1h)
- GET /locate/search
- Auto-detect type (product/location/serial)
- Redirect logic

### T3: Item & Location Detail Pages (~1.5h)
- /locate/item/{id} view
- Multi-location list
- /locate/loc/{id} view
- Items at location list

### T4: Recent + Tests + Tag (~45 min)
- Recent searches (client-side localStorage)
- Favorites toggle
- ~6 tests
- Tag v2.8.0-mobile-locate

---

## ✅ Acceptance Criteria

- [ ] /locate accessible from sidebar
- [ ] Search bar works (type or scan)
- [ ] Big scan area triggers camera
- [ ] Recent + favorites display
- [ ] Smart search detects type (product/location/serial)
- [ ] Item view shows multi-location list with status colors
- [ ] Location view shows items at bin
- [ ] Stat tiles accurate (total, available, allocated)
- [ ] Lot age displayed for FEFO awareness
- [ ] Hidden scrollbars throughout
- [ ] Touch targets ≥ 38px
- [ ] PWA installable

---

## 🎬 Smoke Test Scenarios

1. **Search entry**: Type product code → multi-location view
2. **Scan location**: Auto-detected → location detail
3. **Scan serial**: Auto-detected → item view (shows current location)
4. **Stats accurate**: Total = available + allocated
5. **Status colors**: Allocated locations amber, bins purple, staging blue
6. **Favorites**: Star icon toggles, persists
7. **Recent**: Searches persist across sessions
8. **No scrollbars**: Verified
9. **Action buttons**: "Start cycle count" links to Phase 21

---

## 🔗 Reference

- **Foundation**: `mobile-design-system.md`
- **Pattern source**: All earlier mobile phases
- **Desktop equivalent**: TBD (may not exist as desktop yet)
- **Smart search precedent**: User-designed mockup during Phase 14D wait

---

## 🎉 Phase 22 = Mobile Suite Complete!

After this phase:
- All 6 main mobile operations implemented
- Pick (Phase 16) ✅
- Receive (Phase 18) ✅
- Pack (Phase 19) ✅
- Putaway (Phase 20) ✅
- Cycle Count (Phase 21) ✅
- Locate (Phase 22) ✅

= Full mobile WMS for warehouse staff
= Tag pipeline complete to v2.8.0
= Ready for v3.0.0 SaaS launch features
