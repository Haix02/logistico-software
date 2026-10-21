# 01 — Research Findings

Research done 18 October 2026. Every claim that matters to a decision links to its source at the bottom. Items marked **⚠ verify** were not confirmed from a primary source and must be checked before we commit money or code to them.

---

## 1. DevExpress VCL: what "authentic" means, and the catch

### 1.1 What DevExpress VCL is

DevExpress VCL is a commercial component suite for **Embarcadero Delphi and C++Builder** (VCL = Visual Component Library). The current line (v25.2–v26.1) supports Delphi/C++Builder 10.4 to 13.1. Its main components:

- **Data:** Data Grid (ExpressQuantumGrid), Tree List, Vertical Grid, Filter Control, Gantt
- **Analytics:** Pivot Grid, Charts, Gauges, Maps, BI Dashboard, ExpressReports, Spreadsheet, PDF Viewer
- **Office-style:** Ribbon UI, Scheduler, Rich Text Editor, Spell Checker
- **Navigation:** Navigation Bar, Tile Bar, Toolbars, App Menus, Tree View
- **Layout:** Docking Manager, Layout Manager, Tabbed MDI Manager, Splitter, Wizard
- **Skins:** 55+ skins (WXI/Windows 11, The Bezier, Office 2007–2019, Visual Studio), a Skin Editor, runtime light/dark switching
- **Platform:** per-monitor DPI, SVG icons that recolour per skin, **right-to-left support**, touch, DirectX rendering

### 1.2 What makes software *feel* like DevExpress VCL

These traits are the spec for our design system ([08](08-ui-ux-design-system.md)):

1. **Ribbon** with contextual tabs, galleries, Quick Access Toolbar and a Backstage (File) view
2. **Docking**: panels that auto-hide, float, tab together and remember their layout
3. **Grids that are tools, not tables**: group panel ("drag a column header here to group"), auto-filter row, header filter pop-ups, filter builder, find panel, column chooser, band headers, master–detail, footer and group summaries, in-place editors, conditional formatting, export, print preview, **saved per-user layouts**
4. **Layout Control forms**: aligned labels, groups, tabbed groups, users can re-arrange at runtime
5. **Dense information**: 22–24 px rows, 1 px borders, many columns on screen
6. **Keyboard-first** work: Enter/Tab navigation, F-keys, shortcuts everywhere
7. **Skins** applied consistently to every control, including icons

### 1.3 The catch: VCL cannot reach your platforms

DevExpress VCL runs **only on Windows**. VCL is Windows-only by definition, and DevExpress does not ship a FireMonkey (FMX) suite. A Delphi/VCL app cannot run on macOS, in a browser or on phones. Its **.NET siblings** can, and the project chose **.NET for the whole system**.

| Option | Win | Mac | Web | Mobile | DevExpress authenticity | Verdict |
|---|---|---|---|---|---|---|
| Delphi + DevExpress VCL | ✅ | ❌ | ❌ | ❌ | 100 % | Fails the platform requirement |
| Blazor WebAssembly + DevExpress Blazor (web-first) | ✅ browser | ✅ browser | ✅ | ✅ browser / MAUI later | 85 % | First choice, **dropped 18 Oct 2026**: no licence budget (see §1.4) |
| **Blazor WebAssembly + Blueprint 6** (web-first) | ✅ browser | ✅ browser | ✅ | ✅ browser / MAUI Hybrid later | 70 % | **✅ Chosen (D2, D3).** Free (Apache-2.0), dense professional look, own Blazor components |
| DevExpress WPF (+ Avalonia XPF for macOS) | ✅ native | ✅ via XPF | ❌ | ❌ | 95 % | Out while there is no DevExpress licence |
| .NET MAUI Blazor Hybrid | ✅ | ✅ (Mac Catalyst) | reuses Blazor UI | ✅ | 85 % | Candidate for desktop/mobile packaging of the same Blazor UI |
| DevExpress .NET MAUI (native mobile) | — | — | — | ✅ | n/a | Out while there is no DevExpress licence |
| Telerik / Syncfusion / Radzen / MudBlazor for Blazor | ✅ | ✅ | ✅ | — | 30–60 % | Not chosen: different look, or missing Ribbon/docking/large-data grid |

**What DevExpress Blazor 26.1 provides (verified from the package API, Oct 2026):**
- **Data:** Grid (group panel, filter row, filter menu, Filter Builder, search box, column chooser, fixed columns, master–detail, total and group summaries, virtual scrolling, layout save/restore, export to XLSX/CSV/PDF), TreeList, Pivot Table (separate package)
- **Server-side data mode:** `GridDevExtremeDataSource<T>` binds the grid to an HTTP endpoint backed by `DevExtreme.AspNet.Data`. Paging, sorting, filtering, grouping and summaries all run as SQL on the server. This is implemented in Logistico.
- **Ribbon** (`DxRibbon`): tabs, groups, contextual tabs, application (File) tab, toggle items, combo/spin items, adaptive collapse
- **Layout:** Form Layout (groups, tabs, column spans), Splitter, Tabs, Stack/Grid Layout, Drawer, Popup, Window, Flyout
- **Navigation:** Accordion (VCL Navigation Bar equivalent), TreeView, Menu, Context Menu, Toolbar
- **Editors:** TextBox, ComboBox, Lookup, SpinEdit, DateEdit, TimeEdit, Memo, TagBox, Masked Input, Upload, HTML Editor
- **Feedback:** Toast (VCL Alert Window), Message Box, Loading Panel
- **Visualisation:** Charts, Gauges, Map, Sparkline, Sankey
- **Themes:** Fluent (light/dark, accent colours, Small/Medium/Large size modes), classic themes
- **Gap:** no general-purpose **docking manager**. We use tabs and splitters now, and can add a dock layout later if users need floating panels.

**Licensing:** DevExpress .NET products are distributed through **nuget.org** (v25.1+) and need a per-developer licence key (`DevExpress_License.txt`). Without it, builds warn (DX1000/DX1001) and pages show an evaluation banner. Blazor is included in the *ASP.NET & Blazor*, *DXperience* and *Universal* subscriptions; Universal also covers WPF, WinForms and .NET MAUI.

### 1.4 Update (18 Oct 2026): Blueprint replaces DevExpress

A commercial licence was not an option for the project, so DevExpress was removed from the code in favour of **Blueprint** (Palantir's open-source design system, Apache-2.0; IBM's equivalent is Carbon).

- Blueprint ships React components. Logistico uses only its **CSS and icon font** (pinned versions vendored by `scripts/vendor-ui-assets.sh`) and builds **its own Blazor components** on that markup ([08](08-ui-ux-design-system.md)). No React, no JavaScript UI framework, no licence.
- What we kept from the VCL traits in §1.2: dense tables that work as tools (server-side search, filters, sort, paging, CSV export), keyboard and scanner-first input, consistent status colours, light/dark themes, full RTL.
- What we gave up for now: ribbon, docking, saved per-user grid layouts, built-in pivot and reporting. These can be added as our own components if users ask for them.

---

## 2. ASYCUDA in Iraq ("Asicoda")

ASYCUDA (Automated SYstem for CUstoms DAta) is UNCTAD's customs management system. **ASYCUDA World** is the current Java-based generation. Iraq's General Customs Authority (GCA) has been rolling it out with UNCTAD.

### 2.1 Rollout timeline (federal Iraq)

| Date | Milestone |
|---|---|
| Early rollout | Live in the 9 largest customs offices, about 81 % of trade volume (UNCTAD) |
| **1 Jan 2025** | Paper → **electronic declarations**. Web payment adopted. |
| 2025 | **Electronic manifests** linked with shipping agencies and airlines. Exemptions and land-transport clearance modules completed. Exports activated in all centres. |
| **1 Jun 2025** | Valuation module (Cabinet Decision 270/2025) |
| **1 Nov 2025** | Smart selectivity (risk channels) piloted in 3 centres |
| Nov 2025 | Director-General: ASYCUDA covers **all 22 federal border crossings**. Kurdistan Region crossings **not yet**. |
| **1 Dec 2025** | **Pre-arrival declarations** scheduled to start |
| Jan 2026 | Taxes collected at ports; calculations done automatically in ASYCUDA |
| **24 Jun 2026** | End of the Phase Two plan (risk analysis, post-clearance audit, Authorized Economic Operators) |
| **10 Jul 2026** | **Mandatory electronic verification of Certificates of Origin and commercial invoices** issued through the Federation of Iraqi Chambers of Commerce. Declarations can be **suspended** if the documents are not e-verified. Clearing agents enter the document numbers in ASYCUDA fields. |
| Sep 2026 | Customs duty **pre-payment** automated through ASYCUDA and approved e-payment systems |

**Linked government systems:** Standardization (COSQC), Traffic, the Ur e-government platform (e-payment portal), Radiation, Car Trading, Ports, Central Bank, Maritime Transport, plus weighbridges and scanners at some yards. **Under study:** Ministry of Trade, National Data Center, IATA, regional customs systems.
**Remaining federal sites:** Al-Shaljia customs office and the Khor Al-Zubair free zone (scheduled).

### 2.2 Kurdistan Region

Status is **contested**. Reports from November 2025 onward conflict: some say the KRG cabinet backed implementing ASYCUDA at its crossings, while a KRG adviser said no final agreement exists. **Design implication:** customs handling must be **pluggable per border crossing**. Federal crossings use ASYCUDA; KRG crossings (e.g., Ibrahim Khalil, Haj Omran, Bashmakh, Parviz Khan) use their own process until that changes.

### 2.3 How private companies integrate

- **No public API** for private companies has been documented for Iraq.
- ASYCUDA World **supports XML import/export of the SAD** (Single Administrative Document). UNCTAD publishes the SAD XML message description, and several countries publish copies and samples (Saint Lucia, Aruba, Saint Kitts & Nevis). Users click "Import XML File" in the ASYCUDA World client.
- **Manifests:** carriers and agents can submit manifest and degroupage data as XML instead of re-keying it.
- Access is granted by each national customs authority through an application process.

**Conclusion → our strategy ([07](07-customs-asycuda.md)):** generate validated SAD and manifest XML from Logistico data (Level 1), and capture every reference ASYCUDA returns (registration, assessment, receipt, release). In parallel, apply to the GCA for declarant credentials, the Iraqi schema version and a test environment, and for a direct B2G web-service link (Level 3) if one is offered.

---

## 3. Other Iraqi regulatory facts that shape the design

| Topic | Finding | Design impact |
|---|---|---|
| **COSQC Certificate of Conformity (VOC Iraq)** | Regulated goods need pre-shipment inspection and a COC from an appointed body (TÜV Austria, Applus+, Bureau Veritas, …). Consignments without a COC can be **rejected** at the border. | COC is a first-class document on shipments, with checklist rules per HS code. |
| **Iraqi Quality Mark** | Made **optional** by Council of Ministers Decision 121/2026 (4 March 2026). The COC is still required. | Rule engine must support effective-dated rule changes. |
| **COC geographic scope** | Some inspection bodies say the programme excludes Kurdistan; Bureau Veritas lists a Mosul–Alsad checkpoint for KRG-side traffic. ⚠ verify | Rules keyed by entry point, not just by country. |
| **CoO + invoice e-verification** | Mandatory from 10 Jul 2026 (see 2.1) | Capture verification numbers, block declaration filing when missing |
| **Pre-arrival declarations** | From Dec 2025 | Declarations prepared from booking data before arrival |
| **Duty pre-payment** | Automated Sep 2026 | Payment references and status per declaration |

---

## 4. Hosting landscape in Iraq

- **No hyperscale cloud region inside Iraq.** Enterprises connect to UAE cloud hubs over private interconnects.
- The **Ministry of Communications** contracted Nokia (Sept 2025) to build the first government data centres (Intisar and al-Rashid exchanges), with the stated goal of keeping Iraqi citizens' and companies' data inside Iraq. A distributed network (Baghdad, Basra, Mosul) was announced in Sept 2026.
- **Iraq's first commercial Tier III data centre** is reported to start operating by the end of 2026.
- **Data-localization rules are not yet defined**, according to market research.

**Design impact:** everything ships as **portable Kubernetes**, so the same system runs in a UAE region today and moves into an Iraqi data centre if the law or customers require it. See [05](05-infrastructure-and-deployment.md).

---

## 5. Open-source landscape: build vs reuse

| Project | What it does | Licence | Stack | Verdict |
|---|---|---|---|---|
| **Traccar** | GPS tracking server, **200+ protocols, 2,000+ device models**, REST API | **Apache 2.0** | Java | **Reuse as a sidecar** protocol gateway. Writing 200 binary protocol decoders ourselves has no business value. |
| **Karrio** | Multi-carrier shipping API (rates, labels, tracking) for couriers | Open-core: community + separately licensed `/ee` (⚠ verify LICENSE) | Python/Django, PostgreSQL | **Optional sidecar** for international courier labels (DHL/FedEx/UPS/Aramex), or study its carrier abstraction |
| **Fleetbase** | Modular logistics platform: TMS, driver app, customer app | **AGPL-3.0** | PHP/Laravel + Ember | **Learn from, do not embed.** AGPL's network clause would force us to publish our source. |
| **OpenBoxes** | Inventory and supply chain for low-resource settings: lots, expiry, scanner receiving/putaway/picking | Open source (⚠ verify exact licence) | Groovy/Grails | **Learn from** its receiving/putaway workflows |
| **OpenWMS.org** | WMS + material flow control, Spring Boot microservices | Apache 2.0 core, some GPLv3 services | Java | **Learn from** its domain model |
| **Odoo Community / ERPNext** | General ERPs with inventory | LGPLv3 / GPLv3 | Python | **Not a base.** ERP-shaped, not freight-shaped; customs and multi-leg freight would be heavy customisation. Possible **accounting integration target**. |
| **PowerSync** | Postgres ↔ SQLite offline sync | Service **FSL** (source-available, free self-host "Open Edition"); client SDKs Apache 2.0 | — | **Candidate** for mobile offline sync if its .NET SDK is mature enough (⚠ verify); alternatives: CommunityToolkit.Datasync (MIT, Microsoft) or a small in-house command-queue sync |
| Zebra DataWedge | Rugged-scanner integration through Android intents | Zebra docs | Android (.NET MAUI platform code) | **Use** in the warehouse app |
| **DCSA**, **IATA ONE Record** | Ocean and air cargo data standards | Open standards | — | **Adopt** their event codes and data models |

**Verdict:** as requested, we **build Logistico's core from zero**: domain, UI, customs, WMS, TMS and finance. We **reuse infrastructure-grade components** where reinventing gives no competitive advantage: PostgreSQL, ASP.NET Core Identity, and (proposed) Traccar's protocol decoders and NATS.

---

## 6. Licensing traps found (2025–2026)

These changed recently, and many tutorials still recommend them:

| Library | Change | Our choice |
|---|---|---|
| MassTransit (.NET) | v9 moved to a commercial licence | Wolverine (MIT) or a thin in-house outbox relay |
| MediatR, AutoMapper | Commercial licensing since 2025 | Plain handlers / Wolverine; **Mapperly** (source-generated mapping) |
| Redis | Licence changed (RSAL/SSPL, later AGPL option) | **Valkey** (BSD, Linux Foundation) |
| MinIO | Community edition cut back since 2025 (console features removed, prebuilt binaries stopped) ⚠ verify | Cloud S3 or **SeaweedFS** / Ceph RGW on-prem |
| ingress-nginx (Kubernetes) | Project retirement announced (best-effort maintenance ended March 2026) ⚠ verify | **Gateway API** with Envoy Gateway |
| QuestPDF | Free only below $1 M annual revenue | Print HTML labels and documents from the browser now; decide on a PDF engine when invoices need it |
| DevExpress (.NET) | Per-developer subscription + licence key file | Not used: replaced by Blueprint (Apache-2.0), see §1.4 |
| PowerSync service | FSL (source-available) | Only if chosen for mobile sync; ADR records the exit path |
| Fleetbase | AGPL-3.0 | Do not embed |
| ASP.NET Core 10.0.11 server packages | NuGet audit reports high-severity advisories | Pinned to 10.0.13 (Oct 2026); keep runtime patched |

---

## Sources

- DevExpress VCL product page: https://www.devexpress.com/products/vcl/
- DevExpress.Blazor on nuget.org: https://www.nuget.org/packages/DevExpress.Blazor
- DevExpress — Ribbon for Blazor: https://docs.devexpress.com/Blazor/DevExpress.Blazor.DxRibbon
- DevExpress — WebAssembly grid example (GridDevExtremeDataSource): https://github.com/DevExpress-Examples/blazor-webassembly-grid-export
- DevExpress — theme and size switcher example: https://github.com/DevExpress-Examples/blazor-theme-and-size-mode-switcher
- DevExpress — CI/NuGet licensing guidance: https://docs.devexpress.com/GeneralInformation/400604/installation/install-devexpress-controls-using-nuget-packages/integrate-nuget-to-popular-continuous-integration-systems
- Blueprint (Palantir) documentation: https://blueprintjs.com/docs/
- Blueprint source and licence (Apache-2.0): https://github.com/palantir/blueprint
- Avalonia XPF third-party compatibility (DevExpress WPF on macOS): https://docs.avaloniaui.net/xpf/third-party/compatibility
- Telerik Blazor DockManager: https://demos.telerik.com/blazor-ui/dockmanager/overview
- Syncfusion community licence: https://www.syncfusion.com/sales/communitylicense
- Blazor grid comparison (vendor blog, treat with care): https://www.infragistics.com/blogs/the-best-blazor-grid
- DevExtreme licensing: https://js.devexpress.com/Licensing · https://js.devexpress.com/DevExtreme/Guide/Common/Licensing
- DevExtreme v26.1 docs: https://js.devexpress.com/React/Documentation/26_1/Guide/UI_Components/Map/Demos
- devextreme-react package: https://classic.yarnpkg.com/en/package/devextreme-react
- Iraq MoF ASYCUDA page: https://mof.gov.iq/en/asycuda.aspx
- UNCTAD — Iraq digital customs: https://unctad.org/news/iraq-trade-and-government-revenue-gets-boost-digital-customs
- Rudaw — full automation, 22 crossings (Nov 2025): https://www.rudaw.net/english/middleeast/iraq/03112025
- Kurdistan24 — no final KRG deal: https://www.kurdistan24.net/en/story/913158/krg-official-says-no-final-deal-reached-on-asycuda-customs-system-with-baghdad
- Shafaq — Baghdad and Erbil to unify digital customs: https://shafaq.com/en/Economy/Baghdad-and-Erbil-to-unify-digital-customs
- Iraqi News — CoO/invoice e-verification by 10 July: https://www.iraqinews.com/iraq/iraq-customs-mandates-asycuda-electronic-invoice-verification-by-july-10/
- Deloitte — electronic verification of import documents: https://www.deloitte.com/middle-east/en/services/tax/perspectives/iraq-customs-activates-electronic-verification-of-import.html
- Iraq Business News — Umm Qasr rules (Jan 2026): https://www.iraq-businessnews.com/2026/01/08/new-customs-rules-in-force-at-umm-qasr-port/
- Iraq Business News — duty pre-payment (Sep 2026): https://www.iraq-businessnews.com/2026/09/21/new-mechanism-to-automate-customs-duty-pre-payment-in-iraq/
- INA — ASYCUDA characteristics: https://ina.iq/en/local/32059-customs-authority-outlines-characteristics-of-asycuda-system-and-announces-its-deployment-in-four-locations-including-baghdad-airport.html
- UNCTAD SAD XML message description (Aruba copy): https://asycuda.douane.aw/documents/XML%20SAD.pdf
- Manifest XML description (Aruba): https://asycuda.douane.aw/documents/XML%20manifest%20structure%20-%20August%202020.pdf
- SAD XML sample (St Kitts & Nevis): https://skncustoms.com/Show-Page.aspx?PageID=327
- ASYCUDA World declaration manual (Uganda): http://asyworld.ura.go.ug/awclient/index_files/AW-User-Manual-Declaration-Processing-Customs.pdf
- TÜV Austria — COC for Iraq: https://in.tuvaustria.com/?p=15080
- Applus+ — COC Iraq: https://www.applus.com/ae/en/what-we-do/service-sheet/certificate-of-conformity---iraq
- Bureau Veritas Verigates: https://verigates.bureauveritas.com/node/995
- Nokia government data centres: https://www.iraq-businessnews.com/2025/09/18/nokia-to-build-first-iraqi-govt-data-centres-for-commercial-use/
- Iraq data-centre directory: https://inflect.com/datacenters/emea/iraq
- Iraq ICT market (localization undefined): https://markwideresearch.com/iraq-ict-market
- Traccar README: https://cdn.jsdelivr.net/gh/traccar/traccar@master/README.md
- Karrio self-hosting: https://docs.karrio.io/product/self-hosting
- Fleetbase: https://openalternative.co/fleetbase
- OpenBoxes features: https://docs.openboxes.com/en/latest/about/features/
- OpenWMS: https://github.com/Mhamedi/org.openwms
- Frappe — ERPNext vs Odoo: https://docs.frappe.io/enterprise/competitive-comparisons/erpnext-vs-odoo.md
- PowerSync Open Edition: https://powersync.com/blog/powersync-open-edition-release
- Zebra DataWedge programmer guides: https://techdocs.zebra.com/datawedge/latest/guide/programmers-guides/articles/
