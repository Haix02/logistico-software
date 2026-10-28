# 13 — Roadmap

This is where Logistico is heading. It is a direction, not a promise of dates: items move when someone needs them and someone builds them. If you want to work on one, comment on (or open) its issue first so we can agree on the approach.

## Done

### 3.0 (October 2026)

- **Warehouses as data:** each company manages its own sorting hubs, branches and fulfilment stores per governorate, with coverage, map position, capacity and live load. Routing, labels and the map follow them.
- **Dashboard:** period-over-period KPIs, daily volume, pipeline, failure reasons, top sellers, governorates, couriers, customer rating.
- **Order confirmation** for COD orders from risky customers (or all orders), with a call queue and automatic cancellation of unconfirmed orders.
- **Pricing:** a rate card per company used by the order form and the server.
- **Customer history and return risk** while typing the phone number; **order editing and cancellation**.
- **Delivery ratings** from the tracking page; low ratings open complaints.
- **Team and roles:** administrators manage staff accounts; five roles (including customer support) enforced by the API on every endpoint and followed by the UI; safe concurrent team changes; password resets by administrators.
- **Demand intelligence:** what customers order, where and when, basket size against refusals, trade lanes, repeat customers, plain-language findings and an anonymised open-data export.
- **Operations-console theme**, later set to light by default with dark on request ([ADR-029](14-architecture-decisions.md)), Content-Security-Policy with nonces, compressed API answers, proxy-aware rate limits, truck-manifest guards, customer self-service lockout.
- Translated sign-in and password messages; Docker Compose for evaluation; open-source release (Apache-2.0).

### 2.1 and earlier

Shipments (international and domestic), hub network and trucks, hub station with scanners, returns, customer requests, automation engine, labels, customs workbench, fleet and COD settlements, public tracking, Arabic/English with RTL, security hardening. See [CHANGELOG.md](../CHANGELOG.md) and [17 — Release audits](17-release-audits.md).

### Unreleased

- **Proof of delivery:** who took the parcel, an optional signature and the courier's location at every attempt ([18](18-ideas-from-open-source.md)).
- **Courier app:** couriers deliver from their phones (their own parcels only), with call and WhatsApp, cash to collect and failure reasons; the app can be installed on the home screen.
- **Seller portal:** shops book their own orders at the company's prices, follow them and their returns, cancel before pickup, and see what they are owed and every payout with its statement (their own shop only).
- **Courier cash hand-in:** the office receives each courier's collected cash against the parcels it covers, shortages are recorded, and sellers are paid only from cash the company holds.
- **Saved views:** each person keeps named views of the shipments list (filters, sort and columns) and their usual columns.
- **Audit log:** who changed what and when, for every order, price, rule, payout, seller and staff account, with each field before and after; append-only, recorded in the same transaction as the change.
- **Return fee:** an optional share of the delivery fee charged to the seller for parcels sent back to them (not for damage in our care), deducted from their next payout.

## Next

These are the most useful next steps, roughly in order.

| Item | Why it matters | Notes |
|---|---|---|
| **Audit log, part 2** | Tamper evidence and the rest of the security events | Per-tenant hash chain, monthly partitions, sign-ins, exports and sensitive reads ([04](04-database-design.md) §4.6) |
| **Real email and MFA enforcement** | Password reset and confirmation in production; second factor for staff | [10](10-security-and-compliance.md) §8 |
| **Customer OTP or signed links** | Self-service on the tracking page without relying on knowing the phone number | WhatsApp or SMS provider needed |
| **WhatsApp Business API sending** | Today messages are queued and sent by staff with one click | Provider-neutral interface |
| **Saved views on more lists** | The same named views on returns, transfers, customs and finance | The API already takes a list name |
| **Kurdish (Sorani)** | Third language, RTL | Texts only; the RTL work is done |
| **Load tests and telemetry** | Turn the targets in [00](00-overview.md) §6 into measurements | OpenTelemetry, k6 or NBomber |

## Later

- **Shipments MVP for imports:** parties and addresses, masters and containers, consolidation, invoicing, notifications ([02](02-domain-model.md)).
- **Warehouse inventory (WMS):** receiving, putaway, picking, packing, counts, lots, bonded stock; fulfilment from stock for sellers.
- **Fleet:** GPS (Traccar), live map, geofence milestones, dispatch and trips.
- **Customs and finance:** ASYCUDA round trips, tariff and duties, AR/AP, accounting export ([07](07-customs-asycuda.md)).
- **Scale and intelligence:** carrier integrations, ETA prediction, BI, document extraction, partner API and webhooks.

## Known limitations

- The confirmation, rating and request actions on the public tracking page verify the last four digits of the order's mobile number, with a 24-hour lock after five wrong answers. That is knowledge-based. Treat it as a convenience, not strong authentication.
- Email sending is a no-op: password reset and email confirmation do not send anything until a sender is configured.
- The audit log records changes, not reads, sign-ins or exports, and is not hash-chained yet.
- Performance targets have not been measured under production load.

Production requirements are listed in [15 — Production readiness](15-production-readiness.md).
