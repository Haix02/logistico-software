# Changelog

All notable changes to Logistico are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **A company chooses its country.** Iraq keeps the governorates, the map and the five starting hubs. Any other country starts with no hubs: staff add the regions they deliver to (a code such as JO-AM) and the warehouses that cover them. Phones accept an Iraqi mobile or an international number with a plus. Each company's data stays on its own tenant.
- **Open parcels are indexed** for large lists (`ix_shipments_tenant_open_created`), so the board of parcels that are not delivered, returned or cancelled does not scan the closed history.
- **A QA suite that uses the product as a company does** (`tests/Logistico.Qa`, docs/11 §3). It starts the built server with its own database. Three shops book 36 orders, the hub receives them all at once and loads one truck to Basra, and three couriers deliver, get refused or get no answer, and retry. Customers track and rate, cash is handed in and the sellers are paid. Every parcel and every dinar must be accounted for, and the time of every endpoint goes into `TestResults/qa/delivery-day.md`. Then Chrome opens every menu page as admin, operations, support, finance and viewer, in Arabic and English and at phone width, and fails on script errors, refused calls, untranslated keys and sideways scrolling. GitHub Actions runs it as its own step.
- **Jenkins** (`Jenkinsfile`, `deploy/jenkins/`, `scripts/jenkins.sh`, docs/11 §7): the same pipeline on a local Jenkins, set up entirely from the repository (Configuration as Code, a pinned plugin list). It covers format, build, tests with coverage, migrations, the Flutter app, QA and the Docker image, and on `main` it packages the server tarball and the migration bundles. It listens on 127.0.0.1 only, needs a sign-in, and builds this repository's branches only, never pull requests from forks.
- **Finance can take cash at the cash desk** (new `cash` policy: administrators, operations, finance). The dialog shows any shortage the courier still owes and records its repayment explicitly, capped at what is owed. Only the parcels on the screen are handed in: one the courier delivered meanwhile waits for the next count instead of being added silently.

### Fixed
- **Baghdad on the network map can be selected.** Its hub marker sat on the governorate, so the click opened the hub instead. The marker is shifted off the shape; truck lines still meet at the real hub.
- **The dashboard "right now" strip no longer clips its last figure** when the sidebar is open. The chips scroll inside the strip.
- **A known phone shows the governorate** next to the saved address, so a Basra address is not applied as if it were Baghdad.
- **If the application has not appeared after 45 seconds,** the splash is replaced by the reload bar instead of spinning with an empty page.
- **Parcels scanned at the same moment at one hub** no longer conflict (412) or open two trucks to the same hub: scans at a hub take a turn each. The warehouse station also queues fast scanner input instead of dropping it.
- **A courier's double tap on Delivered** returns a clear "not on your run" instead of a server error.
- **A customer's reschedule from a phone** (a date with +03:00) no longer fails with a server error; promised and event dates from the API are stored as UTC.
- **Arabic-Indic digits** (٠١٢٣…) work in phone numbers, tracking numbers and the last four digits customers type to confirm, rate or ask for a change.
- **A low rating while a complaint is open** adds to that complaint instead of opening a second one.
- **Public tracking** shows a parcel on a truck or on its way back at the right stage, keeps hub codes private, and hides the expected date once the delivery is called off.
- **A truck that arrives with a parcel short** says so on that parcel's history; when the parcel turns up, it is received and the missing alert is cleared. A parcel received off a truck says it came without one.
- **Renaming a courier** updates the name on their open parcels. **A return after the seller was paid** for the parcel is refused.
- **Paste to order** no longer makes up prices or places: a price right after the phone number, prices written 25,000 / ٢٥٬٠٠٠ / 25 ألف, house and alley numbers, names starting with يم, and an unknown place (left for the clerk to choose) are all read correctly. The fee waits until the governorate is known.
- **Orders with an amount that is not a multiple of 250** (from the API, the app or an import) can be edited: money inputs take any whole-dinar amount and show 12504, not 12504.0000.
- **Every refusal has a message in Arabic and English.** Fifteen codes (a repeat rating, region and pricing conflicts, courier and warehouse checks…) reached the screen raw; a test now fails when a new one has no text.
- **A seller sees exactly what they are owed** (1,714,000, not 1.7M), and the cancel dialog's buttons read "Back" and "Cancel order".
- **Changing the language** never redirects to another site (`//host` and `/\host` return addresses go home).
- **Sign-in from a page opened by IP address** no longer logs a browser security error from passkey autofill. The console no longer flashes its pages before it knows who is signed in.

### Security
- **The Blazor debug-proxy package** that NuGet audit flagged (CVE-2026-102870) is on 10.0.14 with the other ASP.NET Core packages.
- **Signing in with an unknown account** takes as long as with a wrong password, so the token endpoint does not reveal which accounts exist.

## [3.1.0] - 2026-11-02


### Added
- **Project website** (GitHub Pages, https://haix02.github.io/logistico-software/): a landing page with screenshots,
  quick start and FAQ, and every document in `docs/` rendered and searchable, with a sitemap and social cards. The
  repository has releases, Discussions, a citation file and a support page.
- **First run: a setup page instead of settings** (ADR-033, docs/19). A new installation writes a one-time setup code
  to its log and every page leads to `/setup`: the code, the company's name and phone, and the first administrator,
  with sample data to learn with if wanted. It runs once, under a database lock, so two people submitting together get
  one administrator; then it closes. Unlike Fleetbase and Traccar, reaching a fresh server is not enough to own it;
  unlike Karrio, there is no default password. `Seed:AdminEmail` still works for unattended installs.
- **Getting started**: a guide on the dashboard and its own page that walks each role through setting up the company
  (profile, warehouses, prices, team, couriers, sellers, sort plan, first order and scan, automation, the mobile app,
  installation health). Each step links to the page that does it and is done when the data says so, not when someone
  ticks it; operations and administrators can mark a step "not for us".
- **Company profile** (Administration → Company): name, phone, head office and address; the name shows in the top bar.
- **Installation health** (Administration): PostgreSQL version, pending migrations, the shelf-order collation, HTTPS,
  the reverse proxy, sign-in keys, email, leftover bootstrap settings, demo data, administrators, two-step sign-in and
  automation, each with what to do about it.
- **Hub sort plans** (Warehouse → Sort plan; ADR-032; ideas from Odoo's putaway rules and parcel sort plans): each
  hub says which parcels go on which shelves — for its couriers by governorate or area, waiting for the truck to a
  given hub, returns for the seller, one seller's own shelf — and how many fit on each. A receive scan without a shelf
  (station, bulk scan, the app, or a milestone recorded by hand) follows the plan: the most specific matching rule
  wins, its shelves fill in order, a full shelf overflows to the next and then to the next matching rule, and when all
  are full the parcel goes on the emptiest and the scan says so. A typed or scanned shelf always wins; a parcel scanned
  again on an allowed shelf stays put. The station and the app show the chosen shelf large. The plan page lists every
  shelf in walking order with its load and capacity, and shows where a parcel would go without scanning it. The demo
  company starts with a plan at every hub.
- **Logistico mobile app** (`mobile/`, Flutter, Android and iOS; ADR-031): one app whose screens follow the account —
  senders (home with what the shop is owed, orders, booking at the company's price, payouts), couriers (the run, scan
  a label to open its stop, delivered with receiver, signature and location or not delivered with the reason, cash to
  hand in) and managers (like-for-like figures, alerts, order search, hub scanning and shelf checks, money). What each
  kind of account sees, the company's colour, name and message come from the console. Barcodes from the built-in
  scanners of Android handhelds (Zebra, Urovo, Sunmi, Newland, iData, Chainway and any configured broadcast), scanner
  guns and the camera. Arabic first (RTL) and English, light and dark, the console's look and Almarai. CI analyzes and
  tests it; an on-device test delivers a parcel against a real server.
- **Mobile app support in the API** (ADR-031): the app signs in with ASP.NET Identity bearer tokens
  (`/api/v1/auth/token`, `/api/v1/auth/refresh`) under the web's rules (confirmed account, lockout, two-factor, its own
  rate limit); a refresh is refused once an administrator locks the account or changes it. Every endpoint accepts the
  cookie or the token with the same policies. `/api/v1/app/config` tells the app what to show for the signed-in
  account: sender, courier or manager, the modules the company switched on (only those the role may use), scanner
  intents for Zebra, Urovo, Sunmi, Newland, iData and Chainway handhelds plus the company's own, branding, a message
  and the minimum app version.
- **Mobile app settings page** (Administration → Mobile app): what senders, couriers and managers see in the app,
  camera scanning, the broadcast of another handheld's scanner, "require the receiver's signature" (enforced by the
  server and shown in the courier app), app name, colour, a message for everyone, support phone and minimum version.
- **Comments on orders** (idea from Fleetbase): a *Comments* tab on each order where staff leave notes for each other —
  what the customer said, what to check at the hub — with who and when. Notes are never edited, only added; the call
  desk, operations and administrators write them, every office role reads them, customers and sellers never see them.
  English notes read left to right inside the Arabic page, and numbers keep their order inside Arabic notes.
- **Shelf checks** (Hubs & trucks → Shelf checks; idea from OpenBoxes cycle counts): count what is really on a shelf, or
  in the whole hub, with a barcode scanner. Each scan says at once whether the parcel belongs there, is on the wrong
  shelf (with where the system has it) or is not expected here; finishing lists what is missing. Parcels on the wrong
  shelf are put right in one tap, a second person starting the same shelf joins the count, and every count is kept
  (SC-… numbers) with who did it.
- **Proof of delivery** (courier app; idea from Fleetbase): marking a parcel delivered asks who took it (the customer, a
  family member, a neighbour, a colleague or someone else, with their name) and offers a finger signature; the phone's
  location is recorded with every delivery and failed attempt, so "the courier never came" can be checked. The order
  page shows it under *Proof of delivery* with a map link. Couriers can always finish a delivery: the signature is
  optional and a missing location never blocks it; an app still open from before keeps working.
- **Saved views and columns** (Shipments): choose which columns the list shows (courier, created, shelf, time in status…)
  and save the current filters, sort and columns under a name; your views appear as chips beside the quick views, open
  with one click and are removed with ×. Views are personal (on your account, so they follow you to another computer)
  and every office role, read-only ones included, can keep them. Your usual columns are remembered on each device.
- **Audit log** (Administration → Audit log, and "History of changes" on each order): who changed what and when — orders,
  customs, payouts and cash hand-ins, delivery prices, automation rules, warehouses, couriers, sellers and staff accounts
  (roles and links included) — with each field before and after, the person and their address. Entries are written in
  the same transaction as the change, cannot be edited or deleted, and never contain passwords. Automation is named as
  such; start-up seeding is left out.
- **Courier cash hand-in** (Fleet page): the office receives a courier's cash with the list of parcels it covers and the
  amount expected, enters the amount counted, and any shortage or extra is recorded (`CH-…` numbers, recent hand-ins
  listed with differences). The courier app reminds couriers how much to hand in. Two people receiving the same cash at
  the same moment cannot count it twice.
- **Return fee** (Delivery prices page): a share of the delivery fee charged to the seller when a parcel goes back to
  them — refused, unreachable, returned by the customer, cancelled after pickup or a wrong address; never for damage in
  the company's care. It is fixed on the parcel when the seller collects it, shown on the order (office and seller
  portal) and deducted from the seller's next payout, listed in the statement CSV. A fee larger than what is left of a
  payout waits for the next one. 0 % (the default) keeps returns free.
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
- **Paid to seller** on an order: once its cash is collected, the order shows the payout (settlement number and date)
  that carried it to the seller, or "Not paid yet".

### Changed
- **Tests reach the whole system.** Every read endpoint is called as every role on the demo company and must never
  fail; the setup page, the guide, the company profile and the health checks have end-to-end tests; a new test project
  renders the console's components with bUnit (the guide card, the health page, role-gated actions, tables, paging,
  mixed Arabic/Latin text) and checks that every menu entry and guide step leads to a page and has its texts in both
  languages. CI reports the console's coverage on its own line.
- **Orderly lists.** Shelves sort the way people walk a hub (A-2 before A-10, B-01-9 before B-01-10) in the order list,
  the station and shelf checks; sorting by status follows the order a parcel lives through them instead of the
  alphabet; every sort has a tiebreaker, so paging never shows an order twice or skips one. The hub station no longer
  pre-fills shelf A-01-1: an empty shelf field means "use the sort plan".
- **Tests cover the demo company and customer ratings; CI shows coverage.** A test company seeded with the 150-order
  demo (staff, couriers, seller, fleet, settlements) must open every screen, sort the order list by every column both
  ways, add up its money and open each demo account's app; seeding twice adds nothing. Customer ratings are tested end
  to end (only after delivery, only once, only with the phone digits; two stars or fewer open a complaint). Each CI run
  lists line coverage per module on its page (86.6 % of the server code) and fails below 85 %.
- **README and docs match the light console.** New screenshots of the dashboard (light and dark), the shipments list, the Iraq map and demand intelligence. Dark-mode borders are documented as hairlines.
- **New look: GitLab's Pajamas design system on Blueprint.** Navigation sits on a quiet grey frame and each page is a
  white panel; buttons, inputs, badges, alerts, tabs, tables, dialogs, drawers and toasts follow Pajamas (32 px
  controls, 8 px corners, a near-black main action, blue links and focus rings, colour kept for status). Blueprint
  still does the work underneath (tables, menus, popovers, dialogs, icons, hotkeys); it now loads in a CSS cascade
  layer so the Pajamas rules always apply. **Light is the default**; dark mode follows Pajamas' dark palette and is one
  click away. Arabic stays first-class (Almarai, right-to-left).

### Fixed
- **A clerk's next click matches the number.** The network's active parcels open the open list, a hub's "ready for couriers" opens parcels already at that destination, and the shipment board's "with couriers" column is only parcels out for delivery. Changing the dashboard period no longer leaves the previous period's figures under the new choice. Booking the next order stays on a cleared form, and a known phone fills the saved address.
- **A clerk's next click matches the number.** The network's active parcels open the open list, a hub's "ready for couriers" opens parcels already at that destination, and the shipment board's "with couriers" column is only parcels out for delivery. Changing the dashboard period no longer leaves the previous period's figures under the new choice. Booking the next order stays on a cleared form, and a known phone fills the saved address.
- **Hubs and trucks.** Each hub on the network map opens its scan station and its trucks. A truck cannot be opened from a hub to itself. Map lanes follow the theme colours. Warehouse list shows an error when the network cannot be loaded.
- **Operations console.** Changing the dashboard period no longer lets a slower answer overwrite the one just chosen. The Iraq map says when the network could not be loaded instead of spinning forever, and its refresh no longer runs off the page. Typing a customer's phone no longer applies an older lookup. An international booking cannot use the same place twice, and the form asks for the goods.
- **Dark mode drew a hard grid.** Cards, tables, the dashboard pulse, shipment rows and the Iraq map used solid mid-grey borders on a near-black surface. Those lines are now faint white hairlines, and status outlines are tinted instead of fully saturated. Inputs stay visible.
- **The installed app still opened on the old dark splash.** Home-screen colour and the browser chrome now follow the light theme (`#ecebea`), and dark mode still switches them. A computer set to dark mode no longer restyles Logistico's controls unless dark mode is chosen in the app.
- **The search hint said Ctrl K on a Mac**, where the shortcut is ⌘K. The command palette still opens with either key.
- **The current page in the account menu disappeared in dark mode** (the highlight was a black tint on a black surface).
- **Link previews and search engines only saw the word "Logistico".** The public page now has a title and description in Arabic and English, Open Graph tags, and a `robots.txt`.
- **Customs value in the right currency.** An import declared in dinars was valued as if the amount were US dollars
  (a 6.55 million IQD parcel was assessed as $6.55 million); dinars are now converted at the official rate, and other
  currencies, or no declared value, are left for the clerk to enter in dollars from the invoice instead of an invented
  $1,000. The customs form shows what the shipper declared.
- **Customs release follows ASYCUDA's order:** a declaration is released only with a customs value and, when duties or
  fees are due, the treasury receipt of their payment. Saving a released declaration again keeps its first release
  date, and a released declaration can no longer be put on hold.
- **Dashboard comparisons are like for like.** "Today" was compared with all of yesterday, so at 10 a.m. bookings looked
  ~60 % down on a normal day; every period is now compared with the same stretch of the period before (until the same
  hour). Demand intelligence does the same.
- Switches in their "off" state were invisible apart from the knob; they now show a grey track (Pajamas toggle), and
  keyboard focus on a switch is visible.
- **Sellers are paid only from cash the company holds.** A payout used to include parcels whose cash was still in a
  courier's pocket; now only handed-in cash is owed and paid, the Finance page shows the cash still with couriers, and
  paying a seller whose cash has not arrived explains why. Existing data counts as handed in.
- The payout history on the Finance page no longer runs off the side of the screen in Arabic: the date sits under the
  payout number and the statement download is an icon.
- "COD with couriers" on the Fleet page reset at midnight, so cash a courier kept overnight disappeared from view; it
  now shows all collected cash not handed in, whatever the day.
- **Search finds what people type.** Arabic letters written either way match (احمد = أحمد, فاطمه = فاطمة,
  الاناقة = الأناقة), as do Arabic-Indic digits and words with harakat. Part of a phone number works with or without the
  leading 0 or +964 (`0770 123`, `770123`). Several words match across fields in any order (customer + area, customer +
  governorate in Arabic or English, `@handle` for a shop). Very short words match the start of a word, so `1` no longer
  matches every number containing a 1. The same rules apply to the global search, the Shipments, Sellers, Requests,
  Trucks, Customs and Order confirmation lists, the courier list and the seller portal.
- The global search (`Ctrl K`) also finds truck (`TR-…`), request (`RQ-…`) and return manifest (`RM-…`) numbers, and
  matches page names with Arabic letter variants.
- Search-as-you-type lists no longer show an older, slower result after a newer one; failed loads show a message
  instead of breaking the page.
- A page that fails shows "This page could not be shown" with a retry button; the rest of the app keeps working.
- Dark mode: "Lock" and other minimal danger buttons were filled red; outlined buttons lost their text colour on hover.
- Small dropdowns (the Team page roles) no longer draw the arrow over the text.
- Status badges are as wide as their text in lists, instead of stretching across the cell.

### Performance
- Search reads one indexed column instead of six: with 50,000 parcels a two-word Arabic search takes about 4 ms and every
  list and dashboard call stays under 75 ms.

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
