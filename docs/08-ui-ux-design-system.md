# 08 — UI/UX Design System (GitLab Pajamas look on Blueprint, Arabic-first)

## 1. Goal

Logistico screens are **dense, fast and keyboard-friendly**, like the DevExpress VCL desktop apps the business is used to, and **clean and calm** like GitLab. Two design systems share the work:

- **GitLab Pajamas** ([design.gitlab.com](https://design.gitlab.com)) sets the **look**: colours, type scale, spacing, corner radii, and how buttons, inputs, badges, alerts, tabs, cards, tables, dialogs, drawers and toasts appear.
- **Blueprint 6** (Palantir, Apache-2.0) supplies the **mechanics** it is built for: dense tables, menus, popovers, dialogs and drawers, form controls, the omnibar, icons and hotkeys.

Arabic is the default language, so every screen works **right-to-left** first.

**How Blueprint is used:** Blueprint ships React components, and Logistico is .NET. We use Blueprint's official **stylesheets and icon font** and write our own **Blazor components** that output Blueprint markup. There is no React and no paid licence.

| Asset | Version | Notes |
|---|---|---|
| `@blueprintjs/core` CSS | 6.21 | `blueprint.css` (LTR) and `blueprint.rtl.css` |
| `@blueprintjs/select` CSS | 6.4.0 | Omnibar styles for the command palette (`blueprint-select[.rtl].css`) |
| `@blueprintjs/icons` | 6.14.1 | Icon font, 700+ icons |
| Almarai | `@fontsource/almarai` 5.3.0 (weights 300/400/700/800) | Arabic + Latin in one family, SIL OFL |
| GitLab Pajamas tokens | `@gitlab/ui` 138 design tokens (MIT) | Values copied into `app.css` §1 (`--gl-color-*`); nothing loaded at runtime |

`scripts/vendor-ui-assets.sh` downloads the pinned versions into `wwwroot/lib`, generates the **RTL builds with RTLCSS 4.3.0** (a build-time tool; nothing runs in the browser) and wraps every Blueprint stylesheet in the **`blueprint` cascade layer**. `App.razor` loads the `.rtl.css` files for Arabic and the originals for English, so Blueprint's own margins, carets, menus and input icons mirror correctly. Because `app.css` is not layered, its rules always win over Blueprint's, whatever the selector: that is how the Pajamas look is applied to Blueprint's markup.

## 2. Principles

1. **Power users first.** Staff work in this all day, so density, keyboard speed and scanner input come before decoration.
2. **Every list is a tool.** Search, filter, sort, page and export on the server, with the filters kept in the URL so views can be shared.
3. **Arabic-first, RTL-correct.** Layout mirrors properly; numbers, tracking codes and phone numbers stay left-to-right inside Arabic text (§7).
4. **Nothing overlaps, nothing pushes the page sideways.** Rows that can be too wide (tables, tab strips, segmented controls) scroll inside themselves; the page itself never scrolls horizontally, down to 390 px phones.
5. **Readable.** Body text is 14 px (15 px in Arabic) on a 1.5 line height, and controls are 32 px tall, as in Pajamas; touch screens (courier app, hub station) use the 40 px large size.
6. **Status is consistent everywhere.** The same colour, icon and wording for a status on the dashboard, list, map, timeline, label and public page.
7. **Hardware-friendly.** Scan inputs keep focus, accept Enter from keyboard-wedge scanners, and give sound and visual feedback.

## 3. App shell

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ ◆ لوجستيكو  [🔍 رقم التتبع، الهاتف، الزبون أو المتجر…  Ctrl K]   ● رادار المراكز 8  🔔49  [+ طلب جديد]  English  ☾  👤 ▾ │
├──────────────────────────────────────────────────────────┬───────────────────────────┤
│  Page header: icon · title · subtitle · actions          │ العمليات                   │
│  KPI cards (click → filtered list)                       │   لوحة التحكم · خريطة العراق │
│  Section / tabs / filters bar                            │   الشحنات · طلب توصيل جديد  │
│  Data table (sortable, row click → details)              │ المراكز والنقل             │
│  Pager (x–y of n, rows per page)                         │   محطة المركز · الشاحنات (5) │
│                                                          │   المرتجعات (3) · البوليصات │
│                                                          │ خدمة العملاء               │
│                                                          │   الطلبات (11) · الأتمتة (49)│
└──────────────────────────────────────────────────────────┴───────────────────────────┘
```

- **Paneled layout (GitLab):** the top bar and the side menu sit on the grey *application chrome*; the page is a white panel with 12 px corners beside them. Public pages, the courier app and the seller portal are white with a hairline under the top bar.
- **Top bar:** search trigger (opens the command palette), live hub radar (scans in the last minutes), alert bell, new order, language switch, dark mode, user menu.
- **Side menu:** grouped by job (operations, intelligence, hubs and trucks, customer care, sellers and money). Counters for trucks on the road, returns waiting, open requests and open alerts come from `OpsState`, polled by the layout. The menu can collapse to icons.
- **Phones (≤ 860 px):** one-row icon top bar; the side menu becomes a slide-in drawer over a dimmed backdrop. Two KPI cards per row, forms in one column.

## 4. Keyboard and scanner

| Input | Action |
|---|---|
| `Ctrl/⌘ + K` or `/` | Command palette (Blueprint omnibar): tracking numbers, sellers, pages and actions |
| `?` | Shortcut sheet |
| `g` then a letter | Go to a page (`g d` dashboard, `g i` map, `g s` shipments, `g w` hub station, `g t` trucks, `g r` returns, `g q` requests, `g a` automation …; letters are in `AppNavigation`) |
| `n` | New delivery order |
| Barcode scanned outside an input | Opens that shipment, or the palette with the code. Scan boxes on the hub station and truck pages keep focus and run their own scan. |
| Action barcodes | Printed sheet (hub station → quick commands) switches scan mode, hub or shelf with the scanner |

## 5. Component kit (`src/Logistico.Web.Client/Ui`)

| Component | Blueprint markup | Used for |
|---|---|---|
| `Button`, `LinkButton` | `bp6-button` + intent / minimal / small / large / fill / loading | All actions |
| `Icon` | Blueprint icon font | Buttons, menus, KPIs, timeline (a test fails on unknown icon names) |
| `Tag`, `StatusTag` | `bp6-tag` | Status chips (one colour + icon per status) |
| `Callout` | `bp6-callout` | Errors, notices on details pages |
| `Section` | `bp6-section` | Titled panels with header actions |
| `Tabs` / `Tab` | `bp6-tabs` | Page tabs with counts; the strip scrolls when it does not fit |
| `SegmentedControl` | `bp6-segmented-control` | View switches and quick filters |
| `HtmlSelect`, `Switch`, `SearchInput`, `FormField` | `bp6-html-select`, `bp6-control`, `bp6-input-group`, `bp6-form-group` | Forms and filter bars |
| `StatusFilter` | `bp6-popover` + `bp6-menu` | Multi-select status filter |
| `DataTable<T>`, `Th`, `Pager` | `bp6-html-table` (striped, interactive) | Lists with server-side sort and paging |
| `ShipmentTable`, `TransferTable` | `DataTable` | The one table for parcels and the one for trucks |
| `Dialog`, `Drawer`, `Popover`, `ConfirmHost` | `bp6-overlay`, `bp6-dialog`, `bp6-drawer`, `bp6-popover` | Editors, side panels, menus, confirmations |
| `Toaster` / `ToastHost` | `bp6-toast` | Short confirmations |
| `KpiCard`, `ProgressBar`, `Skeleton`, `Spinner`, `NonIdealState` | `bp6-card`, `bp6-progress-bar`, `bp6-skeleton` | Numbers, loading and empty states |
| `TrackingStages`, `HubJourney`, `HubRoute`, `Timeline` | custom on Blueprint tokens | Customer progress, seller → hub → hub → customer route, journey events |
| `IraqMap` | SVG | Network map (§6) |
| `Mixed`, `PhoneText`, `CopyButton`, `PageHeader` | — | Bidi-safe user text, phone numbers, copy, page furniture |

Popovers are positioned with fixed placement (`lx.place` in `wwwroot/js/lx.js`) so they are never clipped by a scrolling table and flip at the screen edge in both directions.

## 6. Iraq network map

The map is built into the app; there is no map service, API key or tile server.

- `scripts/generate-iraq-map.py` turns the geoBoundaries **IRQ ADM1** boundaries (CC0) into SVG paths for the 18 governorates, simplified with Douglas–Peucker, and writes `Ui/Map/IraqMapShapes.g.cs` (shapes, label points and the five hub positions). Re-run it only if the boundaries or hubs change.
- Governorates are shaded by the chosen measure (active parcels, at hub, with couriers, late, failed attempts, returns). Hubs are circles (red when they have open alerts); trucks on the road are dashed lanes between hubs.
- Clicking a governorate opens its numbers; clicking a hub opens that hub's shelf list. Labels that would sit under a hub marker or a lane are offset in `IraqMap.LabelAt`.

## 7. Arabic, English and mixed text

| Topic | Rule |
|---|---|
| Languages | `ar-IQ` (default) and `en-US`; switch from the top bar (standard culture cookie) |
| Texts | `Resources/UiText[.ar].resx` (client), `ShipmentTexts`, `NotificationTexts`, `CommonText`, `ServerText`. Tests fail if a key is missing in either language or a used key does not exist. |
| Font | Almarai for everything; monospace for tracking numbers, codes, counters and rates |
| CSS direction | Logical properties only (`margin-inline-start`, `inset-inline-end`, `text-align: start`…). A test rejects `left`/`right` in `app.css` and `account.css`. |
| Latin inside Arabic | `<Mixed Text="…">` splits user text with `Bidi.Split` and isolates Latin runs (`علي (Baghdad-03)`); numbers sit in `<bdi>`. Arabic resx strings wrap fixed Latin runs in U+2066…U+2069. |
| Arabic inside English | `<Mixed>` renders `<bdi>` (direction from the text), so Arabic names and addresses keep their order in the English UI. |
| Flex rows | Text pieces are never separate flex items (a gap would split `من` from its number): `Mixed` always renders one wrapper, and the pager builds its sentence from flex pieces. |
| Codes and routes | Hub routes are shown as a left-to-right unit (`BGW → NJF`), like flight routes; place-to-place arrows inside sentences follow the reading direction (`بغداد ← النجف`, `Baghdad → Najaf`). |
| Counts | `L.Count("ParcelsN", n)` picks the plural form: Arabic `طرد واحد` / `طردان` / `3 طرود` / `11 طردًا` / `100 طرد`, English `1 parcel` / `2 parcels`. Variants are resx keys with `_One`, `_Two`, `_Few`, `_Many`; English only needs `_One`. |
| Numbers | Western digits with `.` for decimals and `,` for thousands in both languages (`AppCultures.Create`) |
| Money | `75,000 د.ع` / `75,000 IQD`; dashboards use compact `131.9 مليون د.ع` / `131.9M IQD` with the exact value on hover |
| Dates | Gregorian, Iraqi month names in Arabic (تشرين الأول), `Asia/Baghdad` display |
| Phones | Stored as `+9647XXXXXXXXX`, shown as `0770 123 4567`, always LTR |

## 8. Theme: GitLab Pajamas on Blueprint

The look follows GitLab's Pajamas design system; Blueprint's components are restyled to it, not replaced.

- **Light is the default**, as in GitLab; dark is one click away (top bar or user menu) and remembered per browser
  (`bp6-dark` on `<body>`). The browser bar colour follows the chrome. In dark mode, card and table borders are
  faint white hairlines. The card shadow is redefined there, because a shadow computed on the light theme would
  otherwise keep a bright grey outline.
- **Colour**: Pajamas' warm neutrals for surfaces and text, blue for links, focus rings and "info", green, orange and
  red for status only. Every value is a token in `app.css` §1: `--gl-color-*` are Pajamas primitives, `--lx-*` are
  the semantic tokens the rules use (`--lx-surface`, `--lx-text-strong`, `--lx-badge-info-bg`…). Section 14 redefines
  only the semantic tokens for dark mode. Blueprint's `--bp-*` variables point at the same values.
- **Shape**: 4 px small details, 8 px controls and alerts, 12 px cards and sections, 16 px dialogs and drawers; cards
  have a 1 px border and no drop shadow; overlays use Pajamas' soft shadows.
- **Components (§3b)**:
  - Buttons are Pajamas buttons: default (white with a border), **primary = near-black** (the "confirm" action, light
    in dark mode), danger red, minimal = tertiary. Success and warning keep their colour where it carries meaning
    (delivered, scan actions). Use **one primary action per view**; row actions in tables are default buttons.
  - Blueprint tags are **badges** (pills tinted by meaning; no intent = neutral, minimal = muted).
  - Callouts are **alerts** (tinted box, border in the same hue, coloured icon).
  - Segmented controls are a quiet track with a raised white choice; tabs have a 2 px dark indicator.
  - Inputs are 32 px with a visible border; focus = dark border plus a blue ring.
  - Toasts are dark, at the bottom start corner; only the icon carries the intent.
- **Type**: Almarai for Arabic and Latin; headings, labels and emphasis are bold (700), everything else regular.
  Labels and headers are sentence case in both languages (no upper-case micro-labels).
- **Data visualisation**: the map and heat grid use Pajamas' data blue ramp (`--lx-map-0…6`), rates a red → green
  diverging ramp (`--lx-map-d0…d6`), product categories the Pajamas data palette.
- The file is organised in numbered sections (tokens, base, utilities, components, shell, page furniture, overlays,
  tables, forms, tracking, operations, map, labels, public pages, dark theme, small screens, print).

## 9. Accessibility

Keyboard-reachable rows and controls, visible focus rings, `aria-sort` on sortable headers, `role="tab"`/`aria-selected` with arrow-key movement in tab strips (mirrored in Arabic), `role="status"` on scan results and toasts, `aria-current` on the active tracking stage, and labelled icon-only buttons.

## 10. Guard tests (`tests/Logistico.ArchitectureTests`)

| Test | Catches |
|---|---|
| `Stylesheets_use_logical_properties_so_arabic_mirrors_correctly` | Physical `left`/`right` CSS that breaks RTL |
| `Every_icon_used_by_the_ui_exists_in_blueprint` | Icon names that render as blank squares |
| `Every_resource_file_has_the_same_keys_in_english_and_arabic` | Missing translations (Arabic-only plural forms allowed) |
| `Every_text_key_used_by_a_page_exists` | Raw keys on screen |
| `Every_domain_value_has_a_display_text_in_both_languages` | Enum values without a label |
| `Blueprint_checkboxes_and_switches_hide_the_native_input` | Double check boxes (Blueprint 6 needs `bp6-control-input`) |
| `Counts_pick_the_plural_form_of_the_language` | Wrong Arabic/English plural category |
