# 09 — Client Applications

## 1. Overview

| App | Platforms | Users | Technology | Status |
|---|---|---|---|---|
| **Logistico Web** | Current Chrome, Edge, Firefox, Safari on Windows, macOS, Linux, tablets | All office roles, partners, managers | Blazor WebAssembly + Blueprint 6, Arabic + English | ✅ Running |
| **Public tracking** | Any browser | Customers | Anonymous pages of the same app (`/track`) | ✅ Running |
| **Desktop app** | Windows, macOS | Power users who need offline cache, multi-window or local hardware | See §4: three options | ⏳ Open for discussion |
| **Driver app** | Android, iOS | Drivers | Proposed: .NET MAUI Blazor Hybrid (reuses the web components) | ⏳ Phase 2 |
| **Warehouse app** | Android (Zebra/Honeywell rugged first) | Warehouse staff | Proposed: .NET MAUI Blazor Hybrid (reuses the web components) | ⏳ Phase 2 |

## 2. Web app (as built)

| Concern | Implementation |
|---|---|
| Hosting model | Blazor Web App with **global WebAssembly interactivity, prerendering off**. The server only serves files, the account pages and the API. |
| UI library | **Blueprint 6** CSS (LTR + RTLCSS-generated RTL build) and icon font, **Almarai** font, all vendored (no CDN), with Logistico's own Blazor components in `Ui/` ([08](08-ui-ux-design-system.md)) |
| Languages | **Arabic (ar-IQ) by default**, English; full RTL; switch in the top bar (culture cookie). Texts in `Resources/UiText[.ar].resx`; number and date formats from `AppCultures`. |
| Shell | `MainLayout`: top bar (command palette, hub radar, alert bell, new order, language, dark mode, user menu) · side menu with live counters (drawer on phones) · content · toasts |
| Routing & auth | `AuthorizeRouteView`. All pages under `Pages/` require sign-in; `Pages/Public/` is anonymous with its own layout. Staff only for now; sellers get a portal later. |
| Who is signed in | `ServerAuthenticationStateProvider` calls `GET /api/v1/account/me`. The cookie is HttpOnly and never readable by browser code. |
| Sign-in / account pages | ASP.NET Core Identity Razor pages, server-rendered (login, 2FA, passkeys, password reset, account management) |
| API access | Typed clients `ShipmentsApi`, `OperationsApi` (trucks, returns, requests, automation, network), `MerchantsApi`, `AccountApi` (same-origin `HttpClient`, sends `Accept-Language`). Problem Details become readable messages for forms. |
| Lists | Server-side search, filters, sorting and paging (shipments, trucks, requests, alerts, automation log, customs queue, warehouse stock, sellers); filters kept in the URL; CSV export |
| Keyboard & scanners | Command palette (`Ctrl/⌘+K` or `/`), shortcut sheet (`?`), `g`+letter navigation, `n` new order; a barcode scanned outside an input opens that shipment; hub station and truck pages keep their scan box focused |
| Shared rules | `Logistico.Modules.Shipments.Contracts`: request validation, state machines, tracking stages, delivery promise, hubs, automation policy, request rules, Code-128, duties. The same code runs in the browser for instant feedback and on the server for enforcement. |
| Pages | Dashboard · Iraq network map · Shipments (list/board) · New delivery order · New international shipment · Shipment details (stages, hub route, timeline, requests, WhatsApp message, actions) · Hub station · Trucks between hubs (list, manifest) · Returns (by seller, return manifests) · Customer requests · Automation & alerts (alerts, customer messages, activity log, rules) · Print labels · Sellers · Customs workbench · Fleet & couriers · Finance & COD · Public tracking (with self-service requests) |

### Next for the web app

1. **Roles and permissions** in the UI (hide actions the user cannot run; the server enforces).
2. **Seller portal** (sellers sign in and create their own orders).
3. **Kurdish (Sorani, `ckb`)** as a third language.
4. **PWA install** (offline shell, desktop icon) as the zero-cost desktop option.
5. **Push updates** (SignalR) instead of the 5-second poll for the radar and side-menu counters.
6. **Saved list views** per user.

## 3. Hardware from the browser

Browsers cannot reliably reach USB/serial devices, and Safari has no WebUSB or WebSerial. The proposed **LX Device Service** ([06](06-device-integration.md)) is a small .NET service the web app talks to over `wss://localhost`. With it, the web app can print labels and receipts and read scales on any OS without a separate desktop app.

## 4. Desktop app: options (open for discussion)

| Option | What it is | Look & feel | Offline | Hardware | Effort | Recommendation |
|---|---|---|---|---|---|---|
| **A. Installable web app (PWA) + Device Service** | The same web app installed as an app window; Device Service for printers/scales | Identical to web | Shell + cached reads | Via Device Service | **Lowest**: no second UI | **Start here (Phase 1)** |
| **B. .NET MAUI Blazor Hybrid** | Native Windows/macOS app hosting the **same Blazor components** in a WebView, with direct .NET access to the machine | Identical to web | Full local SQLite cache | In-process | Low–medium: packaging, updates | Phase 2 if offline or multi-window needs grow |
| **C. Native Avalonia UI app** | A separate native desktop UI with Avalonia (MIT, runs on Windows and macOS): docking and native grids possible | Different from web | Full | In-process | **Highest**: second UI codebase | Only for a dedicated power-user console. (DevExpress WPF is out while there is no licence.) |

## 5. Mobile apps (proposed, Phase 2)

**Two apps, not one:**
- **Driver**: phones, navigation, POD, COD.
- **Warehouse**: rugged scanners, high scan volume, kiosk-like.

They need different permissions, distribution (stores versus MDM) and UX, but share the C# contracts, API client and validation with the web app.

| Concern | Proposal |
|---|---|
| Framework | **.NET MAUI Blazor Hybrid**: native app shell (camera, Bluetooth, background location) hosting the same Blueprint-styled Blazor components, with layouts made for small screens. Free; no extra UI licence. |
| Offline data | Local SQLite + queued commands with idempotency keys. Sync engine options: CommunityToolkit.Datasync (MIT) or PowerSync (if its .NET SDK is mature); decide in Phase 2. |
| Scanning | Zebra **DataWedge** intents and Honeywell intents (Android platform code); camera scanning on phones |
| Printing | Zebra Link-OS (Bluetooth ZPL); ESC/POS over Bluetooth (raster for Arabic) |
| Maps & navigation | Mapsui (MIT) with offline Iraq tiles; hand-off to Google Maps/Waze for turn-by-turn |
| Driver location | Foreground service while **on duty** only |
| POD | Photo (compressed < 300 KB), signature, OTP, receiver name/ID |
| Distribution | Stores (driver app); Android Enterprise / Zebra StageNow kiosk (warehouse devices) |

### Driver app flows
1. **Start shift**: vehicle check (photos, odometer), accept trip.
2. **Trip list**: ordered stops, map, one-tap call/WhatsApp to consignee, navigation hand-off.
3. **Pickup**: scan pieces, photo, signature, print a receipt.
4. **Delivery**: scan → collect COD → POD (signature/photo/OTP) → receipt → done. Failed attempt → reason code + photo + reschedule.
5. **End shift**: COD cash handover (count, discrepancies), vehicle check-in.

### Warehouse app flows
Receive (against ASN, container devanning) → Putaway (system-directed location) → Pick (wave/batch/zone, scan-confirm) → Pack (cartonise, print label) → Load (scan to trip/container, seal numbers) → Cycle count → Move/relabel → Lookup (scan anything → show what it is).

## 6. Offline & sync rules (all clients)

| Rule | Detail |
|---|---|
| IDs created on the client | UUIDv7, so offline-created records never collide |
| Commands, not row overwrites | Clients queue **commands** (`ConfirmPick`, `RecordDelivery`), which the server validates. Conflicts surface as explicit errors with a resolution UI, not silent last-write-wins. |
| Event time | Every command carries the device's `occurred_at` (and GPS stamp), so history is correct after late sync. The domain already separates `OccurredAt` from `RecordedAt`. |
| Idempotency | Each queued command has an `Idempotency-Key`; replays are safe |
| Concurrency | ETag/If-Match on every state change (implemented) |
| Working-set scoping | Devices sync only what they need (today's trip, the warehouse's open tasks), not the database |

## 7. Version compatibility

- The API keeps backward compatibility within `/api/v1`. Breaking changes go to `/api/v2`.
- Native apps (when they exist) send their version; the API can require a minimum version. The API supports **at least the two latest released mobile versions** because store review takes time.
