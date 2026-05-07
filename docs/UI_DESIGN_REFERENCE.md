# WMS UI Design System Reference

**Version**: 1.0
**Status**: Approved (13 mockups locked)
**Last updated**: Day 5 design phase
**Owner**: Solo dev (รับช่วง Claude Code implementation)

---

## Design Philosophy

```
WMS = B2B SaaS for warehouse operations
Audience = Office workers (admin) + Warehouse workers (mobile)

Design Goals:
─────────────────────────────────
✅ Modern SaaS aesthetic (not legacy ERP)
✅ Density-balanced (not cramped, not airy)
✅ Touch-friendly on mobile (44px+ targets)
✅ Information-rich (status, counts, dates visible)
✅ Action-oriented (Quick actions, primary CTAs prominent)
✅ Brand consistent (purple primary throughout)
✅ Accessible (good contrast, labels, semantic HTML)
```

---

## 1. Color Tokens

### 1.1 Primary Brand Colors

```css
/* Purple gradient (sidebar, primary buttons, accents) */
--wms-primary:        #5D4FA0;  /* Base purple */
--wms-primary-light:  #7B5DBF;  /* Lighter purple (gradient end) */
--wms-primary-hover:  #4F46E5;  /* Hover/active button (Indigo) */
--wms-primary-tint:   rgba(93, 79, 160, 0.08);  /* Subtle bg */
--wms-primary-ring:   rgba(93, 79, 160, 0.25);  /* Focus ring */

/* Indigo dark (Login hero, marketing surfaces) */
--wms-hero-bg:        #312E81;  /* Indigo dark hero */
--wms-hero-accent:    #6366F1;  /* Indigo accent */
--wms-hero-text:      #E0E7FF;  /* Indigo light text */
```

### 1.2 Backgrounds

```css
--wms-bg-page:        #FAFAFB;  /* Page background (off-white) */
--wms-bg-card:        #FFFFFF;  /* Card surfaces */
--wms-bg-secondary:   #F5F4F8;  /* Subtle bg (filter rows, etc.) */
--wms-bg-tertiary:    #F3F4F6;  /* Even subtler (table headers) */
```

### 1.3 Status Colors

```css
/* Success (green) */
--wms-success:        #15803D;
--wms-success-bg:     #DCFCE7;
--wms-success-border: rgba(21, 128, 61, 0.2);

/* Warning (amber) */
--wms-warning:        #92400E;
--wms-warning-bg:     rgba(245, 158, 11, 0.1);
--wms-warning-border: rgba(245, 158, 11, 0.3);
--wms-warning-spin:   #F59E0B;  /* Spinning ring */

/* Danger (red) */
--wms-danger:         #DC2626;
--wms-danger-bg:      #FEE2E2;
--wms-danger-soft:    rgba(220, 38, 38, 0.08);

/* Info (blue) */
--wms-info:           #1E40AF;
--wms-info-bg:        #DBEAFE;
--wms-info-soft:      rgba(30, 64, 175, 0.08);

/* Neutral (gray) */
--wms-neutral:        #4B5563;
--wms-neutral-bg:     #F3F4F6;

/* Notification (pink) */
--wms-notification:   #EC4899;
--wms-notification-2: #BE185D;

/* Highlight (yellow dot active) */
--wms-highlight:      #F7B731;
```

### 1.4 Text Colors

```css
--wms-text-primary:    #111827;  /* Headings, main */
--wms-text-secondary:  #6B7280;  /* Secondary, descriptions */
--wms-text-tertiary:   #9CA3AF;  /* Hints, captions */
--wms-text-disabled:   #D1D5DB;  /* Disabled state */
```

### 1.5 Borders

```css
--wms-border:          rgba(0, 0, 0, 0.08);     /* 0.5px subtle */
--wms-border-secondary: rgba(0, 0, 0, 0.15);    /* Hover */
--wms-border-strong:    rgba(0, 0, 0, 0.25);    /* Active */
```

### 1.6 Color Semantics Table

| Use case | Color | When |
|----------|-------|------|
| Sidebar | Purple gradient | Always |
| Primary button | `--wms-primary-hover` (#4F46E5) | CTAs |
| Login hero | `--wms-hero-bg` (#312E81) | Login only |
| Active sidebar item | White bg + Yellow dot | Current page |
| Selected row | Purple tint bg | Bulk select |
| Status: Active | Green | Healthy state |
| Status: Maintenance | Amber | Warning state |
| Status: Inactive | Gray | Disabled |
| Status: Posted/Done | Green | Success |
| Status: In progress | Amber + spinning | Live |
| Status: Error/Failed | Red | Critical |
| Notification badge | Pink (#EC4899) | Alerts |
| Active dot indicator | Yellow (#F7B731) | Live indicator |

---

## 2. Typography

### 2.1 Font Stack

```css
font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI',
             Roboto, sans-serif;
font-family-mono: 'JetBrains Mono', 'Fira Code', Consolas, monospace;
```

**Use mono for**: Codes (SKU, WH-XX, RC-2026-XXXX), tracking numbers, technical IDs

### 2.2 Font Weights

```
400 = Regular (body text)
500 = Medium (most emphasis, buttons, labels)
600 = Semibold (page titles, important headings)
```

**Avoid 700+** (too heavy against modern UI)

### 2.3 Font Sizes

| Token | Size | Use |
|-------|------|-----|
| `--text-hero` | 22px | Page titles |
| `--text-section` | 18-20px | Section headers |
| `--text-body` | 13px | Default body text |
| `--text-small` | 12px | Secondary info |
| `--text-tiny` | 11px | Captions, helpers |
| `--text-eyebrow` | 10-11px | Uppercase labels |

### 2.4 Letter Spacing

```css
/* Page titles, hero text */
letter-spacing: -0.4px;

/* Eyebrow labels (uppercase) */
letter-spacing: 0.4px;
text-transform: uppercase;
font-weight: 500;

/* Mono codes */
letter-spacing: 0;
```

### 2.5 Line Heights

```css
/* Headings */
line-height: 1.2-1.3;

/* Body text */
line-height: 1.55-1.6;

/* Compact (badges, chips) */
line-height: 1;
```

---

## 3. Spacing Scale

```css
--space-1: 4px;   /* Tight gaps inside components */
--space-2: 8px;   /* Default between adjacent elements */
--space-3: 12px;  /* Between related items */
--space-4: 16px;  /* Between sections */
--space-5: 20px;  /* Section padding */
--space-6: 24px;  /* Page padding */
--space-7: 28px;  /* Generous separation */
--space-8: 32px;  /* Major section breaks */
--space-9: 40px;  /* Hero padding */
```

---

## 4. Border Radius

```css
--radius-sm: 4px;    /* Small elements (badges) */
--radius-md: 6px;    /* Default (buttons, inputs) */
--radius-lg: 8px;    /* Cards */
--radius-xl: 12px;   /* Large cards */
--radius-pill: 100px; /* Pills, chips */
```

---

## 5. Shadows

```css
--shadow-sm: 0 1px 2px rgba(0, 0, 0, 0.04);
--shadow-md: 0 4px 12px rgba(0, 0, 0, 0.06);
--shadow-lg: 0 12px 32px rgba(0, 0, 0, 0.08);

/* Hover shadow (purple tint) */
--shadow-hover: 0 4px 12px rgba(93, 79, 160, 0.08);

/* Focus ring */
--ring-focus: 0 0 0 3px rgba(93, 79, 160, 0.15);
--ring-error: 0 0 0 3px rgba(220, 38, 38, 0.15);
```

---

## 6. Component Specs

### 6.1 Buttons

```
Sizes:
  small:    height 28px, padding 4px 10px, font 11px
  default:  height 32px, padding 7px 14px, font 12px
  large:    height 40px, padding 10px 18px, font 13px
  hero:     height 52px, padding 14px 24px, font 15px (mobile/login)

Variants:
  primary:    bg=#4F46E5, color=white, hover=#4338CA
  secondary:  bg=white, border=tertiary, color=primary, hover=bg-secondary
  ghost:      bg=transparent, hover=bg-secondary
  danger:     color=#DC2626, bg=transparent, hover=danger-soft
  primary-purple: bg=#5D4FA0 (sidebar context only)

States:
  hover: lighten or shadow
  focus: ring-focus (purple ring)
  active: scale(0.98) + translateY(1px)
  disabled: opacity 0.5, cursor not-allowed
```

### 6.2 Inputs

```
Default state:
  height: 34px (form), 36px-44px (mobile)
  padding: 0 12px
  border: 0.5px solid border
  border-radius: var(--radius-md)
  background: white
  font-size: 12-13px (form), 14-15px (mobile)

Hover:
  border-color: var(--wms-border-secondary)

Focus:
  border-color: var(--wms-primary)
  box-shadow: var(--ring-focus)

Error:
  border-color: var(--wms-danger)
  box-shadow: var(--ring-error)

Required indicator:
  Asterisk (*) in --wms-danger color after label

Helper text:
  font-size: 11px
  color: var(--wms-text-tertiary)
  margin-top: 4px

Error text:
  font-size: 11px
  color: var(--wms-danger)
  with alert-circle icon (12px)
```

### 6.3 Cards

```
Default:
  background: white
  border: 0.5px solid var(--wms-border)
  border-radius: var(--radius-lg) (8px) or (12px for major cards)
  padding: 14-18px

Header:
  padding-bottom: 12px
  border-bottom: 0.5px solid var(--wms-border)
  font-size: 13px, weight 500

Hover (interactive cards):
  border-color: rgba(93, 79, 160, 0.5)
  transform: translateY(-1px)
  box-shadow: var(--shadow-hover)
  transition: 180ms ease
```

### 6.4 Badges (Status)

```
Structure:
  display: inline-flex
  align-items: center
  gap: 4px
  padding: 2px 8px
  border-radius: 100px (pill)
  font-size: 11px
  font-weight: 500
  with optional dot indicator (5px)

Variants:
  success: bg=#DCFCE7, color=#15803D, dot=#15803D
  warning: bg=#FEF3C7, color=#92400E, dot=#92400E
  danger:  bg=#FEE2E2, color=#DC2626, dot=#DC2626
  info:    bg=#DBEAFE, color=#1E40AF, dot=#1E40AF
  neutral: bg=#F3F4F6, color=#4B5563, dot=#6B7280
  3PL:     bg=#FEF3C7, color=#92400E (special: warehouse type)
  Internal: bg=#DBEAFE, color=#1E40AF
```

### 6.5 Tabs

```
Multi-step Form Tabs (with status):

Container:
  display: flex (no horizontal scroll)
  flex: 1 distribution per tab
  border-bottom: 0.5px solid border

Tab item:
  padding: 14px 16px
  display: flex, gap: 12px
  flex: 1, min-width: 0
  cursor: pointer
  border-bottom: 2px solid transparent

Tab number circle (28px):
  border-radius: 50%
  font-size: 11px, weight 600
  
  States:
    untouched: dashed circle, gray bg
    progress:  amber + spinning ring (animated)
    complete:  green bg + check icon
    error:     red border + red text

Tab label:
  flex-direction: column
  step:  10px uppercase tracked
  name:  13px, weight 500

Active tab:
  border-bottom-color: var(--wms-primary)
  color: text-primary
  
Defer: Tab visual polish, current pattern OK
```

### 6.6 Tables

```
Header row:
  background: var(--wms-bg-tertiary)
  font-size: 11px
  font-weight: 500
  text-transform: uppercase
  letter-spacing: 0.4px
  color: text-secondary
  padding: 10px 14px
  border-bottom: 0.5px solid border

Data row:
  font-size: 12px
  padding: 10px 14px
  border-bottom: 0.5px solid border-tertiary
  hover: background var(--wms-primary-tint)

Sortable headers:
  cursor: pointer
  with sort icon (ti-arrows-sort, ti-arrow-up, ti-arrow-down)

Numeric columns:
  text-align: right
  font-variant-numeric: tabular-nums
  font-weight: 500

Code columns:
  font-family: var(--font-mono)
  font-size: 11px
  color: text-secondary

Selected row:
  background: var(--wms-primary-tint)

Action menu:
  3-dot button on right
  shows on hover for cleaner UI
```

### 6.7 Sidebar (Office)

```
Expanded (220px width):
  background: linear-gradient(180deg, #5D4FA0 0%, #7B5DBF 100%)
  color: white
  padding: 18px 0

Logo section:
  padding: 0 16px 18px
  border-bottom: subtle (rgba white 0.1)
  Logo + version badge (v1.0)

Search:
  margin: 14px 16px
  background: rgba(255,255,255,0.08)
  border-radius: 6px
  padding: 7px 10px

Section header:
  padding: 4px 16px
  margin-top: 14px
  font-size: 10px
  text-transform: uppercase
  letter-spacing: 0.8px
  color: rgba(255,255,255,0.4)

Menu item:
  display: flex
  align-items: center
  gap: 10px
  padding: 8px 16px
  font-size: 13px
  color: rgba(255,255,255,0.85)
  
Active item:
  background: rgba(255,255,255,0.1)
  border-left: 3px solid white
  padding-left: 13px
  + yellow dot (6px) on right

Sub-menu (expanded):
  background: rgba(0,0,0,0.1)
  border-left: 2px solid rgba(white, 0.15)
  margin-left: 30px
  padding: 2px 0
  Sub-item: padding 6px 14px, font 12px

Notification badge:
  background: var(--wms-notification)
  color: white
  border-radius: 50%
  font-size: 10px

Collapsed (64px width):
  Same gradient, icons centered
  Hover: background rgba(white, 0.12)
  Tooltips on hover (HTML title)
  Active: rounded 8px white bg + yellow dot
  Badge dots/numbers visible
```

### 6.8 Topbar

```
Background: white
Border-bottom: 0.5px solid border
Padding: 12px 24px
Height: ~56px

Left:
  Toggle button (30x30, border, chevron icon)
  Search bar (max-width: 360px, with search icon)

Right:
  Action icons (settings, dark mode, notifications)
  Notification badge: pink with count
  Divider line (0.5px, 22px tall)
  User avatar (32x32, purple bg, initial letter)
```

### 6.9 Mobile Bottom Nav

```
Container:
  background: white
  border-top: 0.5px solid border
  display: grid, 4 equal columns
  padding: 6px 0 8px

Tab:
  display: flex, flex-direction: column
  align-items: center, gap: 3px
  padding: 8px 4px
  cursor: pointer

Icon: 22px Tabler icon
Label: 11px

Active state:
  color: var(--wms-primary)
  Top indicator bar:
    position: absolute, top: 0
    left: 50%, transform: translateX(-50%)
    width: 30px, height: 3px
    background: var(--wms-primary)
    border-radius: 0 0 4px 4px
  Label font-weight: 500

Notification badge:
  position: absolute, top: 4px, right: ~18px
  pink bg (#EC4899)
  white text, font-size: 9px
  border-radius: 100px
  min-width: 16px, height: 16px
```

---

## 7. Page Templates

### 7.1 Login Page (Desktop)

```
Layout: 50/50 split-screen

LEFT (Hero):
  Background: #312E81 (Indigo dark)
  Decorative gradient glows (radial)
  
  Top: WMS logo + "Built for modern logistics" pill
  Middle: Tagline "Run your warehouse like a SaaS company"
  Bottom: 3 stats (99.9% Uptime, 5K+ Orders/day, 3PL+ Multi-tenant)

RIGHT (Form):
  Background: white
  Centered, max-width: 400px
  
  "Welcome back" heading (22px, weight 500)
  
  Email field:
    Label "Email" (uppercase, 11px tracked)
    Input (44px tall)
  
  Password field:
    Label + "Forgot?" link (right-aligned)
    Input with eye toggle
  
  Submit button:
    Width: 100%
    Height: 52px
    Background: #4F46E5
    Text: "Sign in →"
  
  "Need help? Contact support" link bottom
```

### 7.2 Office Layout (Sidebar + Topbar + Content)

```
Grid: [220px or 64px] | [1fr]

Sidebar (purple gradient)
Topbar (white, 56px)
Content area:
  background: var(--wms-bg-page)
  padding: 22-28px
```

### 7.3 Dashboard

```
1. Page header (title + breadcrumb)
2. Mini stats top-right (sparklines)
3. Main chart card (Live Feeds purple area chart)
4. Right panel: 4 progress bars
5. Bottom: 4 metric cards (donut + sparkline + chips)
6. Action buttons (Generate PDF, Export Excel)
```

### 7.4 List Page

```
1. Page header (title + breadcrumb + actions)
2. Filter bar:
   - Search input (with icon)
   - Filter dropdowns (Status, Region, etc.)
   - Active filter chips (removable with X)
   - View toggle (List/Grid icons, right)
3. Bulk action toolbar (conditional, when rows selected):
   - "X selected" + bulk edit/archive/delete
4. Data table (sortable, with row actions)
5. Pagination (per-page selector + page nav)
```

### 7.5 Grid View

```
3-column card grid + "Add new" placeholder card

Card:
  - Icon (color reflects status)
  - Name + Code (mono)
  - Status badges
  - Footer: divider + key stats
  - 3-dot menu (top right)

Hover effects:
  - Border purple
  - Lift -1px
  - Purple shadow
  - Icon scales 1.05x
  - Quick actions appear (View/Edit/Archive)
  - Arrow → slides in bottom-right
```

### 7.6 Form Page (Multi-step)

```
1. Back link + breadcrumb
2. Page header:
   - Live indicator (purple pulse)
   - Document number (big)
   - Subtitle
   - Page meta block (status + date + author)
3. Progress bar (real-time fields filled)
4. Tab navigation (numbered circles, no scroll)
   - Status indicators (untouched/progress/complete/error)
5. Section content:
   - Section header (Step XX eyebrow + big title + required pill)
   - Dual-block layout (with icons)
   - Form fields
   - Auto-action toggles
6. Tab footer (Step X of N + Previous/Next)
7. Sticky save bar (dark pill, bottom):
   - Yellow pulse + "Unsaved changes"
   - Discard / Save draft / Post receipt
```

### 7.7 Detail Page

```
1. Back nav + breadcrumb
2. Header (document number + status + actions toolbar)
3. 4 summary stats cards
4. 2-column layout:
   LEFT (main):
     - Tab nav (Overview / Line items / Activity / Attachments)
     - Source & Carrier dual cards
     - Dates & References 4-col grid
     - Notes (quoted style)
     - Activity timeline (vertical line + colored icons)
   
   RIGHT (sidebar 280px):
     - Workflow status (vertical timeline)
     - Quick actions
     - Properties panel
```

### 7.8 Mobile Layouts

```
Status bar (purple, top, system-like)

Login:
  Indigo dark hero (top)
  Form (white, bottom)
  Touch ID button option
  iOS home indicator

Tenant/Warehouse Selection:
  Indigo hero with progress dots (3-step)
  Search + cards
  Big primary button (52px)

Home:
  Purple gradient header (greeting + WH context)
  2 KPI mini cards
  Quick actions 2x2 grid (colored icon tiles)
  Active tasks list
  Bottom nav (4 tabs, active indicator)

Receive Workflow:
  App header (hamburger + title + bell)
  Receipt context card (with progress)
  HERO: Big scan input (56px, auto-focused, purple ring)
  Last scanned card
  Manual entry button (52px)
  Recent scans list
  Bottom nav
```

---

## 8. Smart-skip Login Logic

```
Already implemented in Day 3 Auth (8 sub-chunks complete).
Apply to BOTH Desktop + Mobile.

Step 1: Email + Password
   ↓ verify → PreAuthToken
   
Step 2: Choose Tenant
   ↓ IF user.Tenants.Count == 1 → AUTO-SELECT, SKIP
   ↓ ELSE show selection UI
   
Step 3: Choose Warehouse
   ↓ IF tenant.Warehouses.Count == 1 → AUTO-SELECT, SKIP
   ↓ ELSE show selection UI
   
Step 4: Land on Home

Best case (1 user · 1 company · 1 warehouse):
  Email + Password → Home (no selection screens)

Best UX: Don't ask if there's only one option.
```

---

## 9. Animation Guidelines

```css
/* Standard transitions */
transition: all 180ms ease;

/* Hover states */
transition: border-color 180ms ease,
            transform 180ms ease,
            box-shadow 180ms ease;

/* Spinning ring (tab progress indicator) */
@keyframes wms-spin {
  to { transform: rotate(360deg); }
}
.spin-indicator { animation: wms-spin 1.5s linear infinite; }

/* Live pulse (live indicator dot) */
@keyframes wms-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}
.live-dot { animation: wms-pulse 2s ease-in-out infinite; }

/* Subtle entrance (tab content) */
@keyframes wms-fade-in {
  from { opacity: 0; transform: translateY(4px); }
  to { opacity: 1; transform: translateY(0); }
}
```

---

## 10. Accessibility

```
✅ Semantic HTML (button, nav, header, main)
✅ ARIA labels on icon-only buttons
✅ aria-hidden="true" on decorative icons
✅ Focus rings (purple) visible on keyboard nav
✅ Color contrast ratio ≥ 4.5:1 (text)
✅ Touch targets ≥ 44px (mobile)
✅ Form labels properly associated
✅ Required field indicators (visual + aria-required)
✅ Error messages tied to inputs (aria-describedby)
```

---

## 11. Library Dependencies

### Approved
```
✅ Tabler.io (Free, MIT)
   - CSS framework
   - Bootstrap 5 base
   - Modern aesthetic
   - https://tabler.io

✅ Tabler Icons (Free, MIT)
   - 5800+ outline icons
   - <i class="ti ti-NAME"></i>
   - https://tablericons.com

✅ Tabulator (Future, Free)
   - For complex grids (Stock, Orders)
   - Server-side pagination
   - https://tabulator.info

✅ ApexCharts (Tabler integration)
   - Charts and graphs
   
✅ Flatpickr (Date picker)
✅ Choices.js (Select)
✅ HTMX 2.0 (Interactivity)
✅ Alpine 3.14 (Reactive)
```

### Deferred
```
🔄 Kendo UI (Phase 2 only)
   - Cost: $1,499/yr/dev
   - Reason: Tabulator covers 90% of needs
   - Add only if Scheduler/Pivot Grid needed

🔄 SmartAdmin
   - Cost: $1,299
   - Reason: Custom design exceeds defaults
```

---

## 12. CSS File Structure

```
wwwroot/css/
├── site.css              (Tabler + minimal overrides)
├── wms-custom.css        (THIS DESIGN SYSTEM)
└── wms-mobile.css        (Mobile-specific styles)

wms-custom.css sections:
─────────────────────────────────
1. CSS Variables (color tokens)
2. Typography overrides
3. Layout classes (sidebar, topbar, content)
4. Component classes:
   - .wms-btn-* (buttons)
   - .wms-card (cards)
   - .wms-badge-* (status badges)
   - .wms-input-* (form inputs)
   - .wms-tab-* (tabs)
   - .wms-table (data table)
5. Page-specific:
   - .login-page
   - .dashboard-page
   - .form-page
   - .detail-page
6. Hover effects
7. Animations
8. Mobile overrides (PWA)
9. Utilities
```

---

## 13. Color Palette Quick Reference

```
PRIMARY  ─── #5D4FA0 → #7B5DBF (sidebar gradient)
HOVER    ─── #4F46E5 (button primary)
HERO     ─── #312E81 (Indigo dark - login)

STATUS:
  ●  #15803D / #DCFCE7    Active / Posted / Complete
  ●  #92400E / #FEF3C7    Maintenance / Warning / In progress
  ●  #DC2626 / #FEE2E2    Inactive / Error / Failed
  ●  #1E40AF / #DBEAFE    Info / Internal type
  ●  #6B7280 / #F3F4F6    Neutral / Disabled

ACCENTS:
  ●  #EC4899              Notification badge
  ●  #F7B731              Active dot indicator
  ●  #14B8A6              Secondary accent (Putaway)

BACKGROUNDS:
  Page:    #FAFAFB
  Card:    #FFFFFF
  Subtle:  #F5F4F8
```

---

**End of Reference Document.**

For questions: refer to approved mockups in conversation history,
or rebuild via web search for specific patterns.
