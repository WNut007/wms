# Mobile PWA Design System

**Foundation document for all mobile phase specifications (Phase 18-22+).**

This document defines the shared design language, components, and patterns. Each phase spec references this file for consistency.

---

## 🎨 Brand Colors

```css
/* Primary palette */
--mobile-primary: #534AB7;
--mobile-primary-light: #EEEDFE;
--mobile-primary-dark: #3D3593;
--mobile-gradient: linear-gradient(135deg, #534AB7 0%, #7F77DD 100%);

/* Semantic colors */
--mobile-success: #1D9E75;
--mobile-success-light: #E1F5EE;
--mobile-success-dark: #0F6E56;
--mobile-success-darkest: #04342C;

--mobile-warning: #EF9F27;
--mobile-warning-light: #FAEEDA;
--mobile-warning-dark: #854F0B;
--mobile-warning-darkest: #501313;

--mobile-error: #E24B4A;
--mobile-error-light: #FCEBEB;
--mobile-error-dark: #A32D2D;

--mobile-info: #185FA5;
--mobile-info-light: #E6F1FB;
--mobile-info-dark: #042C53;

/* Grays */
--mobile-text-primary: #1f2937;
--mobile-text-secondary: #6b7280;
--mobile-text-tertiary: #9ca3af;
--mobile-bg-page: #f9fafb;
--mobile-bg-card: #ffffff;
--mobile-bg-subtle: #f3f4f6;
--mobile-border: #e5e7eb;
--mobile-border-strong: #d1d5db;
```

---

## 📐 Sizing & Spacing

### Touch targets
- Minimum: **38px** (small elements)
- Standard: **44px** (Apple HIG recommended)
- Primary actions: **46-48px**

### Border radius
- Small components: **6-7px**
- Medium components: **8-10px**
- Large cards: **11-12px**
- Pill (chips, badges): **100px**

### Spacing scale
- xs: 4px
- sm: 6-8px
- md: 10-12px
- lg: 14-16px
- xl: 18-22px

### Phone frame target
- Width: 320px design / 375-414px production
- Status bar: 24-28px
- Bottom safe area: 12-16px

---

## 🔤 Typography

```css
--font-family: 'Inter', system-ui, -apple-system, sans-serif;
--font-mono: 'JetBrains Mono', ui-monospace, monospace;

/* Scale */
--text-xs: 9px;    /* meta labels */
--text-sm: 10-11px; /* secondary text */
--text-base: 12-13px; /* body */
--text-lg: 14-16px; /* headings */
--text-xl: 18-22px; /* large numbers */
--text-2xl: 30-38px; /* hero numbers */
```

### Weights
- Regular: 400 (body text)
- Medium: 500 (labels, buttons, small headings)
- Semibold: 600 (emphasis, big numbers)

---

## 🧩 Shared Components

### Status Bar
Visual mock at top of screen (display only):
```html
<div class="status-bar">
  <span>9:41</span>
  <div class="indicators">
    <i class="ti ti-wifi"></i>
    <i class="ti ti-battery-3"></i>
  </div>
</div>
```
- Height: 24-28px
- Background: white OR purple gradient (auth screens)
- Padding: 5-6px 16-18px

### App Header
```html
<div class="app-header">
  <button class="back-btn">←</button>
  <div class="title-block">
    <div class="title">Page Name</div>
    <div class="subtitle">Context info</div>
  </div>
  <button class="action-btn">+</button>
</div>
```
- Padding: 12-14px 16-18px
- Border-bottom: 0.5px solid var(--mobile-border)
- Back button: 30x30px, bg-subtle, border 0.5px
- Title: 13px font-weight 500
- Subtitle: 9-10px text-secondary
- Action button: 30x30px, primary-light bg if accent action

### Breadcrumb (alternative header style)
```html
<div class="breadcrumb-row">
  <button class="back-btn-small">←</button>
  <span>Section</span>
  <i class="ti ti-chevron-right"></i>
  <span class="current">ITEM-CODE</span>
</div>
```
- Font size: 9px
- Back button: 22x22px (smaller than main header)
- Current item: monospace, font-weight 500, primary color

### Filter Chips Row
```html
<div class="chip-row no-scrollbar">
  <button class="chip active">All N</button>
  <button class="chip">Open N</button>
  <button class="chip">Closed N</button>
</div>
```
- Active: bg primary, text white, no border
- Inactive: bg white, border 0.5px var(--mobile-border), text-secondary
- Height: 26-28px
- Padding: 4px 10px
- Border-radius: 100px (pill)
- Font-size: 10-11px font-weight 500
- **MUST hide horizontal scrollbar** (.no-scrollbar utility)

### Status Badges
```html
<span class="badge badge-success">Active</span>
```
- Pill shape (border-radius 100px)
- Font-size: 9px font-weight 500
- Padding: 1-2px 6-7px
- Color combos:
  - **Success**: bg success-light, text success-dark
  - **Warning**: bg warning-light, text warning-dark
  - **Error**: bg error-light, text error-dark
  - **Info**: bg info-light, text info-dark
  - **Primary**: bg primary-light, text primary

### Active Session Card (purple gradient)
For showing current/active workflow context:
```html
<div class="session-card-gradient">
  <div class="header-row">
    <div>
      <div class="label">Active task</div>
      <div class="number">PACK-2025-018</div>
    </div>
    <i class="ti ti-box-seam icon"></i>
  </div>
  <div class="meta">Customer XYZ · 3 of 5 lines</div>
  <div class="progress-bar">
    <div class="fill" style="width:60%"></div>
  </div>
</div>
```
- Background: var(--mobile-gradient)
- Color: white
- Border-radius: 12px
- Padding: 11-12px 13-14px

### Item Card (white)
```html
<div class="item-card">
  <div class="icon-wrap">
    <i class="ti ti-package"></i>
  </div>
  <div class="content">
    <div class="title">PRODUCT-CODE</div>
    <div class="subtitle">Product Name</div>
  </div>
  <div class="trailing">12 ea</div>
</div>
```
- Background: white
- Border: 0.5px solid var(--mobile-border)
- Active state: border-left 3px solid var(--mobile-primary)
- Border-radius: 9-11px
- Padding: 9-12px

### Big Location Display (hero element)
For pick/locate where location is primary info:
```html
<div class="location-hero">
  <div class="label">Go to</div>
  <div class="code">B-03-12</div>
  <div class="hierarchy">Aisle B · Rack 03 · Bin 12</div>
</div>
```
- Code: 16-22px font-weight 500, font-mono, color var(--mobile-primary)
- Label: 9px text-secondary uppercase
- Hierarchy: 9-10px text-secondary

### Scan Area (large, dashed border)
Primary input zone for scanning:
```html
<div class="scan-area">
  <div class="icon-circle">
    <i class="ti ti-scan"></i>
  </div>
  <div class="text-center">
    <div>Scan barcode</div>
    <div class="hint">Or tap below</div>
  </div>
</div>
```
- Background: var(--mobile-bg-page)
- Border: 2px dashed var(--mobile-primary)
- Border-radius: 11-12px
- Padding: 24-26px 14-18px
- Icon circle: 56x56px, primary-light bg

### Sticky Bottom Action
Always-visible primary action:
```html
<div class="action-bar">
  <button class="primary">Continue →</button>
  <button class="secondary">Skip</button>
</div>
```
- Background: white
- Border-top: 0.5px solid var(--mobile-border)
- Padding: 10-14px 14-16px
- Primary button: 44-48px height, bg primary, white text, font-weight 500
- Secondary: 32-36px, white bg, text-secondary, border

### Tab Bar with FAB
Bottom navigation:
```html
<div class="tab-bar">
  <button class="tab active">
    <i class="ti ti-home"></i>
    <span>Home</span>
  </button>
  <button class="tab">...</button>
  <button class="fab">
    <i class="ti ti-scan"></i>
  </button>
  <button class="tab">...</button>
  <button class="tab">...</button>
</div>
```
- Tab: 50-56px wide, icon 20-22px + label 9px
- Active tab: primary color
- Inactive: text-tertiary
- FAB: 46-52px, gradient bg, raised (margin-top -16-18px), white icon

---

## 🛠️ Required CSS Utilities

### Hidden Scrollbar (CRITICAL)
**Apply to ALL scrollable areas (vertical and horizontal):**

```css
.no-scrollbar {
  scrollbar-width: none;
  -ms-overflow-style: none;
}
.no-scrollbar::-webkit-scrollbar {
  display: none;
}
```

### PWA Manifest & Meta
```html
<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no">
<meta name="theme-color" content="#534AB7">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-status-bar-style" content="default">
<link rel="manifest" href="/path/to/manifest.json">
```

### manifest.json template
```json
{
  "name": "WMS Mobile",
  "short_name": "WMS",
  "description": "Warehouse Management",
  "start_url": "/{module}/",
  "scope": "/{module}/",
  "display": "standalone",
  "orientation": "portrait",
  "background_color": "#f9fafb",
  "theme_color": "#534AB7",
  "icons": []
}
```

---

## 🎯 Behavior Patterns

### Smart Skip (Auth flow)
- If only 1 tenant → skip Step 2 (tenant select)
- If only 1 warehouse → skip Step 3 (warehouse select)
- Always show step indicator "Step X of N"

### Auto Status Flip
For cycle count and similar:
- Empty input → status "Pending"
- Type quantity → status auto-flips to "Counted"
- Clear quantity → revert to "Pending"
- Implemented via Alpine `@input` handler

### Bounce-to-Queue UX (Mobile-specific)
After submit on mobile:
- ✅ Bounce back to queue page (not Detail)
- Operator grabs next task immediately
- Different from desktop (which bounces to Detail)

### Smart Scan Detection
For scanner inputs:
- Try as Serial Number first
- If matches existing serial in stock + allocated → use that
- Else try as Product Code
- If matches product → check `TrackingMethod == 'LotAndSerial'` (NOT `IsSerialTracked` — column doesn't exist; use existing TrackingMethod enum per Phase 18 learning)
- If not matched → error with clarification

### Native Prompts (Mobile-friendly)
- Cancel reason: `window.prompt('Reason for cancellation')`
- Confirmation: `window.confirm('Are you sure?')`
- Avoid CSS modals on small screens (taller dialogs hard to manage)

### Manual Fallback Always Available
- Every scan area = ALSO has manual entry input
- Users may scan OR type
- Fallback covers damaged barcodes, missing scanners

---

## 📦 PWA Foundation (from Phase 16)

The Phase 16 mobile picker established the scaffolding pattern.

**Reuse for all mobile phases:**
- Controller structure (PicksController as reference)
- Manifest pattern
- Standalone display mode
- Scope per module (`/pick/`, `/receive/`, etc.)
- Sidebar entry under appropriate module

**Module-specific routes:**
- `/pick/` → mobile picker (Phase 16) ✅
- `/receive/` → mobile receive (Phase 18)
- `/pack/` → mobile pack (Phase 19)
- `/putaway/` → mobile putaway (Phase 20)
- `/count/` → mobile cycle count (Phase 21)
- `/locate/` → mobile locate (Phase 22)

---

## 🚦 Color Coding Conventions

- **Purple** = Primary action, current/active item
- **Green** = Success, available, confirm/submit final
- **Amber** = Warning, partial, pending review
- **Red** = Error, urgent, variance/short
- **Blue** = Info, staging, neutral status
- **Gray** = Inactive, pending, disabled

### Variance flagging (for quantity comparisons)
- Match: green
- Short: red
- Over: amber

### Status badges
- Pending/Draft: gray
- Counting/Picking/Packing: amber (in-progress)
- Counted/Picked/Packed: purple (intermediate done)
- Applied/Submitted/Shipped: green (terminal success)
- Cancelled: red or gray (terminal failure)

---

## ✅ Quality Standards

Every mobile screen MUST:

- [ ] Hide scrollbars (no horizontal bars visible)
- [ ] Touch targets ≥ 38px
- [ ] Match brand colors (purple primary)
- [ ] Use Inter font + Tabler icons
- [ ] Show clear status (badges, color coding)
- [ ] Have manual fallback for every scan
- [ ] Use sticky bottom for primary actions
- [ ] Apply semantic colors (red=error, etc.)
- [ ] Native prompt() for confirmations on small screens
- [ ] Bounce-to-queue UX on submit
- [ ] Standalone PWA mode (manifest + meta)

---

## 🔗 Cross-References

- **Phase 16 reference**: Mobile picker implementation (`/pick/` route)
- **Source patterns**: Mobile design library (24+ screens designed during Phase 14D wait)
- **Desktop equivalents**: Each mobile phase has desktop counterpart in main UI

---

## 📚 Future Considerations (Deferred TDs)

These features are in the design vision but deferred:
- Service worker offline caching
- PWA icons (manifest's icons:[] currently empty)
- 4-tier scan flow (Location → Pallet → SKU → Lot)
- Always-focused barcode input
- navigator.vibrate feedback
- SignalR real-time updates
- Per-line wizard mode

Apply selectively when individual phases need them.
