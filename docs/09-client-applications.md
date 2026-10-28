# 09 — Client Applications

## 1. Overview

| App | Platforms | Users | Technology | Status |
|---|---|---|---|---|
| **Logistico Web** | Current Chrome, Edge, Firefox, Safari on Windows, macOS, Linux, tablets | All office roles, partners, managers | Blazor WebAssembly + Blueprint 6, Arabic + English | ✅ Running |
| **Public tracking** | Any browser | Customers | Anonymous pages of the same app (`/track`) | ✅ Running |
| **Desktop app** | Windows, macOS | Power users who need offline cache, multi-window or local hardware | See §4: three options | ⏳ Open for discussion |
| **Logistico mobile** | Android (phones and rugged handhelds), iOS | Senders (shops), couriers, managers and hub staff — one app, screens by role | **Flutter** (Riverpod, go_router, dio), decided 28 Oct 2026 ([ADR-031](14-architecture-decisions.md)) | 🔨 In progress (`mobile/`) |

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

## 5. Mobile app (Flutter, in progress)

**One app, not three.** The account that signs in decides what the app shows, and the company decides, from the web
console, which parts each kind of account gets — a small CMS for the app ([ADR-031](14-architecture-decisions.md)).

| Account (role) | What the app shows |
|---|---|
| **Sender** (Seller role: a shop the company created) | Home with what the shop is owed and what is on its way; orders and their tracking; booking at the company's prices; returns; payouts with statements |
| **Courier** | Today's run (address, landmark, cash to collect), call/WhatsApp, delivered with proof (who took it, signature, location) or not delivered with the reason, cash to hand in; scan a label to open its stop |
| **Manager** (Admin, Operations, Finance, Viewer…) | Today's figures and the period's trend, alerts, order search; for hub roles a scan station (receive, dispatch) and shelf checks |

| Concern | Decision |
|---|---|
| Framework | **Flutter** stable, one codebase for Android and iOS; Material 3 styled with the Pajamas tokens of the web console; Almarai font; Arabic first (RTL) and English |
| State, navigation, HTTP | **Riverpod**, **go_router** (routes rebuilt from the account's role and the company's app settings), **dio** |
| Sign-in | **ASP.NET Core Identity bearer tokens**: `POST /api/v1/auth/token` (e-mail, password; lockout as on the web), short-lived access token and a refresh token kept in the platform keystore (`flutter_secure_storage`); a refresh is refused once an administrator locks the account or changes its role (security stamp) |
| What each role sees | `GET /api/v1/app/config`: the account (name, role, courier or shop), the modules switched on for its role, scanner settings, branding, a message to show, and the minimum app version. Administrators edit it on the **Mobile app** page of the web console; changes reach phones at the next start or refresh, and the audit log records them |
| Scanning | Three sources behind one stream of codes: **built-in scanners of Android handhelds** in intent mode (a configurable broadcast action and extra, e.g. Zebra DataWedge, Honeywell, Urovo, Sunmi, Chainway) through a small Android plugin; **keyboard mode** of handhelds and **Bluetooth/USB scanner guns** (fast key bursts ended by Enter, told apart from typing by speed); the **camera** (`mobile_scanner`) as a fallback |
| Location for proof | The phone's position at each attempt (already accepted by the API), asked only while delivering |
| Performance | Release builds with tree-shaking; lists are lazy and paged; images none by default; the run and the config are cached for a quick start; tokens refreshed once for concurrent requests |
| Distribution | Android first (APK/AAB for handhelds and Play), iOS from the same code |

Senders are shops the company creates (Team page, Seller role); the app has no public sign-up.

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
- The mobile app sends its version (`X-App-Version`); the company's app settings can require a minimum version. The API supports **at least the two latest released mobile versions** because store review takes time.
