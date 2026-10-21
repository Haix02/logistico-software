# 00 — Overview

## 1. What Logistico is

Logistico is an open-source shipping and delivery platform built for **Iraq**. It started with the job most Iraqi delivery companies do every day, delivering parcels for Instagram and online shops to customers in all 18 governorates and collecting cash on delivery, and it is designed to grow into the wider picture: goods coming **into Iraq from abroad**, through customs, into warehouses and out to customers.

It is **.NET end to end**, runs **in the browser**, and works in **Arabic (default, right-to-left) and English**.

## 2. What is built (3.0)

| Area | What works today |
|---|---|
| Delivery orders | Seller, customer and Iraqi address (governorate, area, nearest landmark); pickup or drop-off; COD in IQD; delivery promise that skips Fridays; customer history and return risk while typing the phone number; edit and cancel |
| Warehouses and branches | Each company manages its own sorting hubs, branches and fulfilment stores per governorate: coverage, map position, capacity, live load |
| Hub network and trucks | Origin, destination, current and target hub on every parcel; trucks between hubs planned automatically, loaded and unloaded by scan, with manifests and missing-parcel detection |
| Hub station | Barcode-scanner friendly receive, load, dispatch and route check; shelves; misroute alerts; live hub radar |
| Order confirmation | COD orders from risky customers (or all orders) wait for the customer's confirmation by WhatsApp, the tracking page or a call |
| Pricing | A rate card per company: own-governorate price, price per destination, weight allowance, per-seller discounts; the order form quotes from it |
| Returns | Started automatically after the last failed attempt or a refusal; travel back by truck; handed to the seller against a return manifest |
| Customer requests | Reschedule, change address or phone, cancel, return, complaint, from the public tracking page or by staff; simple ones applied automatically |
| Automation | Per-company rules and a sweep every minute that raises and clears alerts, plans trucks, reschedules, cancels unconfirmed orders and queues WhatsApp messages |
| Tracking and ratings | Public tracking page without personal data; customers rate deliveries; low ratings become complaints |
| Dashboard | Period-over-period KPIs, daily volume, pipeline, failure reasons, sellers, governorates, couriers and customer rating |
| Demand intelligence | What Iraqi customers order, where and when, basket size against refusals, trade lanes, repeat customers, plain-language findings, anonymised open-data export ([16](16-data-and-insights.md)) |
| Labels | 4×6" thermal labels with Code-128 barcodes and the hub routing code |
| Customs (ASYCUDA) | Declaration workbench, selectivity lanes, CoO and invoice e-verification gate, duty estimate, SAD XML export |
| Fleet and finance | Couriers and vehicles, run sheets, cash held by couriers, seller COD balances, settlement batches with statements |
| Team and roles | Administrators add colleagues and give each one role (Admin, Operations, Customer support, Finance, Viewer), lock, unlock and set new passwords; the API enforces every role and the screens follow it |

## 3. Who it is for

| Person | Device | Main jobs in Logistico |
|---|---|---|
| Operations staff | Browser (PC) | Book and edit orders, watch the network, plan and run trucks |
| Hub staff | PC with a barcode scanner, or a phone | Receive, shelve, load, dispatch, return |
| Customer support | Browser | Confirmation calls, customer requests, messages |
| Finance | Browser | COD collection, seller settlements |
| Administrator | Browser | Warehouses, prices, automation rules, the team |
| Customer | Phone browser / WhatsApp | Track, confirm, change the delivery, rate it |
| Courier | Phone (today: run sheet and WhatsApp links) | Deliveries and COD collection |

The larger scope ([01](01-research-findings.md), [02](02-domain-model.md)) adds origin offices abroad, customs brokers, warehouse inventory (WMS) and live GPS.

## 4. Platforms

| Platform | Technology | Status |
|---|---|---|
| Web app (all office roles) | Blazor WebAssembly + Blueprint 6 CSS with our own Blazor components, served by the ASP.NET Core host; dark operations-console theme by default | Built |
| Evaluation | `docker compose up` with demo data, no .NET SDK needed | Built |
| Public tracking | Same web app, anonymous pages | Built |
| Desktop app | Installable web app (PWA), .NET MAUI Blazor Hybrid or Avalonia ([09](09-client-applications.md)) | Open for discussion |
| Driver and warehouse apps | Proposed: .NET MAUI Blazor Hybrid reusing the web components | Open for discussion |
| Hardware from the browser | Proposed: a small local .NET device service ([06](06-device-integration.md)) | Open for discussion |

## 5. Key decisions

The full log with reasons is [14 — Architecture decisions](14-architecture-decisions.md).

| # | Decision | Why |
|---|---|---|
| D1 | **.NET (C#) for the whole system** | One language from database to screen, rules shared between server and browser, LTS runtime, very fast. |
| D2 | **Web first: Blazor WebAssembly + REST API** | Runs in any browser and copes with unstable connections; the same API later serves mobile apps and partners. |
| D3 | **Blueprint 6** CSS and icons with our own Blazor components | Built for dense, data-heavy professional tools; open source (Apache-2.0); no React and no paid UI licence. |
| D4 | **PostgreSQL** | Free, fast, PostGIS and TimescaleDB options, runs anywhere. |
| D5 | **Modular monolith + ASP.NET Core Identity** | One deployable with strict module boundaries; full control over the domain. |
| D6 | **Arabic by default, English second** | Full RTL, the Almarai font, Western digits, Iraqi month names, correct Arabic plurals. |
| D7 | **Seller orders: pickup first; staff sign-in only** | Sellers hand parcels over; customers use tracking links. A seller portal comes later. |
| D8 | **Built-in Iraq map; warehouses are data** | No map service or API key; each company describes its own network. |

Proposed but not adopted (contributions and discussion welcome): NATS JetStream for events, Valkey cache, Traccar as GPS gateway, TimescaleDB for telemetry, Kubernetes for large deployments, the local device service, and the desktop and mobile technologies.

## 6. Non-functional targets

| Area | Target |
|---|---|
| API latency | p95 < 150 ms for reads, < 300 ms for writes at the expected staff load |
| Lists | Server-side paging, filtering and sorting; first page p95 < 400 ms over millions of rows |
| Web app start | First visit < 6 s on 4G (WebAssembly download, then cached); later visits < 2 s |
| Scan → result | < 300 ms on the hub network, excluding printing |
| Availability | 99.9 % for the core API |
| Languages | Arabic (RTL) and English today; Kurdish Sorani next |
| Security | OWASP ASVS Level 2; roles on every endpoint; tenant isolation tested ([10](10-security-and-compliance.md)) |

These are targets, not measurements: see [15 — Production readiness](15-production-readiness.md) for how to verify them on your installation.

## 7. Where it is going

[13 — Roadmap](13-roadmap.md) lists what comes next and where help is most useful.
