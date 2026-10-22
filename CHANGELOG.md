# Changelog

All notable changes to Logistico are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed
- **New look: GitLab's Pajamas design system on Blueprint.** Navigation sits on a quiet grey frame and each page is a
  white panel; buttons, inputs, badges, alerts, tabs, tables, dialogs, drawers and toasts follow Pajamas (32 px
  controls, 8 px corners, a near-black main action, blue links and focus rings, colour kept for status). Blueprint
  still does the work underneath (tables, menus, popovers, dialogs, icons, hotkeys); it now loads in a CSS cascade
  layer so the Pajamas rules always apply. **Light is the default**; dark mode follows Pajamas' dark palette and is one
  click away. Arabic stays first-class (Almarai, right-to-left).

### Fixed
- Status badges are as wide as their text in lists, instead of stretching across the cell.

### Added
- **Courier app** (`/courier`): couriers sign in on their phone and see only the parcels out for delivery with them,
  with the customer, address and nearest landmark, the cash to collect, one-tap call and WhatsApp, and Delivered or
  Not delivered (with the reason). Their day in numbers: to deliver, delivered and cash collected. Deliveries go through
  the same scan logic as the hub station, so cash collection, customer messages and automation rules apply unchanged.
- **Courier role**: administrators give an account the Courier role on the Team page and link it to a courier from the
  Fleet page. Couriers see no other company data. `Seed:DemoTeam` adds `courier@logistico.local` for the demo courier.
- **Seller portal** (`/seller`): online shops sign in and book their own orders (paste the customer's message and the
  form fills itself; the fee comes from the company's prices, with the shop's discount), follow them with filters for
  in progress, delivered and returns, cancel an order before pickup, and see what they are owed, the cash still to
  collect, their delivery rate and every payout with its CSV statement. The figures are the Finance page's own.
  Booking goes through the same code as the office's order screen (`OrderDesk`), so numbering, routing and
  confirmation calls apply unchanged.
- **Seller role**: administrators give an account the Seller role on the Team page and link it to one shop. Sellers
  see no other company data. `Seed:DemoTeam` adds `seller@logistico.local` for the demo shop "بوتيك لمار".
- **Install on a phone**: a web app manifest and icons, so Logistico can be added to the home screen.

## [3.0.0] - 2026-10-21

First public, open-source release (Apache-2.0). It brings together the work on the main branch and the 2.1 hardening
that had not been published; see [docs/17-release-audits.md](docs/17-release-audits.md) for how they were reconciled
and what was checked.

### Added
- **Staff roles** (Administrator, Operations, Customer support, Finance, Viewer) enforced by the API: every module endpoint
  needs a staff role (reads `staff`, changes `operate`), customer-desk endpoints ask for `care`, money and settings
  endpoints for `finance` or `administer`, and a test fails if an endpoint is left without a policy. Accounts without a role see no company data. The browser hides pages
  and buttons a role cannot use and shows settings read-only. See `docs/10-security-and-compliance.md` §2.
- **Team & access** (`/team`, `g x`, administrators): add members with a role and a generated first password, change
  roles, take access away, lock and unlock, and set a new password for someone who forgot theirs; the last active
  administrator is protected, also against two administrators acting at the same moment. Role changes reach open
  sessions within a minute.
- **Content-Security-Policy** with a per-response nonce (scripts only from this origin, no framing or plugins), plus
  `Cross-Origin-Opener-Policy`.
- Sign-in and account forms are rate limited per client address (30 a minute); every 429 explains itself in the
  user's language with `Retry-After`.
- API answers are compressed with Brotli or gzip (lists and CSV exports shrink several times on mobile data).
- `Seed:DemoTeam` (Development): `ops@`, `support@`, `finance@` and `viewer@logistico.local` accounts to try each role.
- **Demand intelligence** (`/insights`, `g e`): demand per governorate with growth, product categories recognised from
  Arabic/English item descriptions, weekday × hour ordering heat grid (Baghdad time), basket size vs refusals, trade
  lanes, first-time vs repeat customers, and plain-language key findings. See `docs/16-data-and-insights.md`.
- **Open-data export** (`GET /api/v1/insights/open-data.csv`): anonymised aggregates for publishing; groups under
  `Insights:OpenDataMinimumOrders` (default 10) are left out.
- `ProductCatalog`: Arabic normalisation and keyword/phrase classification of item descriptions into 11 categories.
- **Operations-console theme**: dark by default, token-based colours for both themes, intent rails on KPI tiles,
  page eyebrows, dense tables, diverging and sequential map ramps.
- `IraqChoropleth` (any value per governorate) and `HeatGrid` UI components.
- `Dockerfile` and `docker-compose.yml` to run the app with demo data on any OS; CI builds the image on every PR.
- Open-source project files: Apache-2.0 `LICENSE`, `NOTICE`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`,
  issue and pull-request templates.
- `ReverseProxy:KnownNetworks` setting to trust `X-Forwarded-For`/`-Proto` from known proxies.

- **Customer support role** for the call desk: confirmation calls, customer requests and messages, customer lookup.
- **Dashboard ratings**: customer rating tile (average of the period, number of ratings on hover), rating per courier,
  and explanations of the delivery KPIs on hover; demo data includes ratings.
- **Translated account messages**: Identity's password and account errors in Arabic and English.
- An exact shipment-reference endpoint (`POST /api/v1/shipments/exact`, up to 200 IDs or tracking numbers) used by batch
  label printing, replacing the latest-100 search.
- `docs/00-overview.md`, `docs/13-roadmap.md`, `docs/15-production-readiness.md` and `docs/17-release-audits.md`,
  written for contributors and operators.

### Changed
- **Sign-in** has a brand panel on wide screens (what Logistico does, an animated outline of the hub network);
  phones keep the compact form.
- **Public tracking** explains where to find the number and what customers can do themselves before a search, and
  lays out the parcel's facts and request options without lone cards.
- Demo data is realistic: shop-specific products and prices, repeat customers, evening ordering peaks, 80 days of history,
  all 17 seeded governorates.
- Insights and the theme are documented in `docs/08`, `docs/14` (ADR-025 to ADR-027) and `docs/16`.

- Team members show a temporary lockout after wrong passwords separately from a lock by an administrator; it no longer
  counts as a locked administrator, and Unlock clears it.
- Staff policies also require a valid company (tenant) claim; a missing or invalid claim is refused instead of failing.
- The tenant context fails closed: an authenticated request without a valid tenant claim gets no default company.
- Sortable table headers and collapsible sections are native buttons with visible keyboard focus.
- Bulk booking validates every nested order (lengths, amounts, missing orders) instead of failing on malformed imports.
- GitHub Actions are pinned to commit SHAs.
- Documentation is written for contributors (no single-owner planning documents).

### Fixed
- Starting a return (or cancelling, or a customer's return request) while a parcel waited on a loading truck left it on
  the manifest: the truck could then never leave (`invalid_transition` on depart) or the parcel ended up on two trucks.
  Parcels now leave a truck whenever their next stop changes (returns, new address, order edits), and a departing truck
  drops lines whose parcels can no longer travel.
- Retiring a warehouse left parcels still heading for it; the next automation sweep failed for the whole company (and,
  in the background worker, for every company after it). Those parcels now go to the warehouse that covers their
  governorate, a parcel that cannot be planned no longer fails the sweep, and each company is swept independently.
- Scanning a parcel onto the wrong truck took it off its correct truck before refusing the scan.
- A return hand-over could include several sellers' parcels on one signed manifest.
- Customs declarations could be overwritten from a stale form (no If-Match); simultaneous first saves returned 500, and
  generated release-order numbers repeated after a few hundred releases.
- Two stations could open two loading trucks on the same route, and a parcel could be added to a truck that was
  leaving at that moment (database-level guard, manifest revision; conflicts answer 412 instead of 500).
- The hub radar, polled every few seconds by every open browser, scanned the company's whole scan history (new index).
- Enum numbers that name no option (for example `"serviceType": 99`) were accepted.
- The container logged a database connection error on start-up: Npgsql tried Kerberos (GSS) encryption, which the
  slim image cannot do. It is now off unless the connection string asks for it.
- **Security:** any signed-in account, including one that registered itself, could read and change all company data,
  export customer lists and publish open data. Access now depends on the staff role.
- **Security:** the tracking page's last-four-digits check could be brute-forced from many client addresses to change a
  parcel's phone or address. Wrong answers are now limited per tracking number (5 per 24 h, then locked even for the
  right digits).
- Behind a reverse proxy every customer shared the proxy's address and therefore one rate limit.
- Very large `page` numbers overflowed into a negative SQL offset and returned HTTP 500 on list endpoints.
- The sidebar's collapse button covered the last menu item on shorter screens.
- Long failure reasons on the dashboard were cut off; they now wrap.
- On phones, section header switches squeezed the section title into a narrow column.
- Documentation named the Cairo font; the app ships Almarai.
- CI: the "migrations match the model" step passed `-c Release` to `dotnet ef`, where `-c` means `--context`, so the
  step could not succeed; it now uses `--configuration Release`.
- Two administrators could lock or demote each other at the same time and leave a company without an active
  administrator; team changes were also saved in several steps, so a failure could leave an account half-changed. Each
  team change now runs in one transaction behind a per-company lock.
- A domestic order could be booked with an origin that differs from its seller's governorate.
- An in-transit parcel could be received at the wrong hub and lose its truck manifest; scans naming an inactive or
  unknown hub were silently moved to another hub.
- The anonymous tracking answer included the seller, COD amount, city and exact hub route.
- External sign-in skipped two-factor verification.
- The hub radar's "simulate scan" demo button wrote real scan events; it is removed.
- In Arabic, currency, weight and percentage units overlapped the number in their inputs.
- Five dashboard health labels were cut off on narrower screens; they wrap now.
