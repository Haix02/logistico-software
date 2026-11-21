# 17 — Release audits

Each release records what was checked, what was found and what changed, so contributors and operators can see why the
code looks the way it does. The newest release comes first.

## Reliability review (21 November 2026)

**How it was done.** The system was checked against the standard references for production software: Michael
Nygard, *Release It!* (2nd ed., 2018), its stability patterns and antipatterns; Martin Kleppmann, *Designing
Data-Intensive Applications* (2017), ch. 7 on lost updates and ch. 12 on end-to-end request ids; Saltzer, Reed and
Clark, "End-to-End Arguments in System Design" (1984); the IETF draft "The Idempotency-Key HTTP Header Field"; and
Google's *Site Reliability Engineering* (2016), ch. 6 on the four golden signals and ch. 22 on cascading failures.
Each pattern was compared with the code, not the documents.

### Where Logistico stands

| Pattern (source) | Before | Now |
|---|---|---|
| Request ids end to end, safe retries (DDIA ch. 12; IETF draft) | A booking retried after a lost answer was booked twice | `Idempotency-Key` on bookings from the console, the seller portal and the app (ADR-035) |
| Timeouts (Release It!) | No server-side limit: a slow request held its thread and connection for as long as it ran | 60 s per request, 180 s for exports, open data and planning; then 504. The app gives up at 30 s, the database at its command timeout |
| Steady state (Release It!) | — | Spent keys are deleted every hour. Scan, automation and planning history still grow without limit (open below) |
| Fail fast (Release It!) | Validation (400) and rules (422) before any change; the tenant fails closed | Unchanged |
| Unbounded result sets (Release It!) | Lists are paged and capped at 200 rows; CSV exports are the only full reads | Exports now have their own time limit |
| Bulkheads (Release It!) | Public tracking and sign-in have their own rate limits; scans at a hub take a turn each | Unchanged |
| Lost updates (DDIA ch. 7) | `xmin` + `ETag` / `If-Match` on every change (412 when stale) | Unchanged |
| Integration points, circuit breaker (Release It!) | The server calls no outside service | None needed yet. OSRM, SMS or a carrier API will each need a time limit and a circuit breaker |
| Health checks (SRE) | `/health/live`, `/health/ready` with a 3 s database check | Unchanged |
| Golden signals: latency, traffic, errors, saturation (SRE ch. 6) | Logs only; the QA suite records each endpoint's time | Open below |

### Verified

| Gate | Result |
|---|---:|
| Release build | 0 warnings, 0 errors |
| Unit, architecture and integration tests | 687 passed |
| Server code coverage | 88.5 % (minimum 85 %) |
| Browser QA suite | 11 passed |
| Mobile app | `flutter analyze` clean, 12 tests passed |
| Against the running server | The same order sent twice with one key: one order, the same tracking number, the second marked `Idempotent-Replayed`. The same key for another order: 422 in Arabic |

### Open: needs a decision

1. **Telemetry.** OpenTelemetry metrics and traces (golden signals per endpoint, booking latency against the SLOs in
   [12](12-observability-and-operations.md)) need a choice of library and of where the data goes.
2. **Load test.** The 2.1 performance targets below have never been measured. A load tool (k6 or NBomber) and a
   production-sized data set are needed.
3. **Retention.** `shipment.hub_scans`, `shipment.automation_log` and `shipment.planning_runs` need periods agreed
   with operations (for example, scans 24 months, automation log 6 months, unapproved plans 30 days).
4. **Parcel labels to GS1.** An SSCC (GS1 application identifier 00) in a GS1-128 barcode would let a partner's
   scanner read our labels. It needs the company's GS1 company prefix.

## QA pass (8 November 2026)

**How it was done.** Seven testers worked on a running copy of the product with demo data, each in one role: a
courier, a customer on the tracking page, a seller, a hub clerk, the office (operations and finance), a new company
setting up from nothing, and a security reviewer. They used the screens in Chrome and the API as the mobile app does,
in Arabic and English, and wrote down every step that failed with what they expected. Their scenarios became the QA
suite (`tests/Logistico.Qa`, [11](11-ci-cd-and-devops.md) §3), which now runs in GitHub Actions and Jenkins on every
change.

### Fixed

| Area | What people hit | Now |
|---|---|---|
| Cash desk | Finance could not take a courier's cash; a hand-in took parcels delivered after the count; a shortage vanished; a renamed courier's cash could not be handed in | Finance has the cash desk; only the counted parcels are handed in; a shortage stays on the courier until repaid; a rename follows their open parcels |
| Returns | A parcel already paid out to the seller could be returned, rewriting the payout | Refused until the refund is agreed |
| Hub | Back-to-back scans were lost; scans at the same moment conflicted (412) or opened two trucks | The station queues scans; scans at a hub take a turn each, one truck per destination |
| Trucks | A parcel counted missing, or received without its truck, left no trace on the parcel | Its history says so; a missing parcel that turns up is received and its alert cleared |
| Customers | Rescheduling from a phone failed (500); Arabic-Indic digits were refused; a low rating with an open complaint was lost; a parcel on a truck or on its way back showed the wrong stage; a repeat rating showed a raw code | All fixed; every refusal code now has a message in both languages, checked by a test |
| Couriers | A double tap on Delivered gave a server error | A clear message |
| Sellers | Paste to order made up prices and places, read alley numbers as prices, took names ending in يم as landmarks; "Owed to you" was rounded | Read correctly or left for the seller to fill in; exact amounts |
| Office | Orders with an amount that is not a multiple of 250 could not be edited | Any whole-dinar amount |
| New company | Pages opened directly showed the standard Iraqi hubs after the network was changed | The company's own network |
| Security | Sign-in time revealed whether an account exists; a backslash return address crashed the language switch | Same time for both; the switch goes home |

### Still open

- **Needs a decision:** the delivery fee of an order without cash on delivery is counted as revenue but never charged to
  the seller (no invoice or deduction yet). Which one should it be?
- Couriers: a used refresh token can be used again and signing out does not end the app's session; the run shows no
  total of cash to collect; small touch targets.
- Sellers: "cash on its way" is before fees while "owed" is after; no page for their own order's details; two account
  endpoints answer 500 without the antiforgery cookie; the rows selector under their order list does nothing.
- Hub: some station and truck counts disagree with their own lists; unloading accepts a parcel that is not on the
  truck; the printed manifest does not name the truck; a typed shelf is not checked against the sort plan.
- Office: dashboard tiles open all-time lists; CSV exports give UTC times without saying so, and amounts with four
  decimals; the payout dialog preselects ZainCash.
- New company: defaults assume Baghdad even when the head office is elsewhere; the guide asks for the team before
  couriers and sellers exist; customers and sellers see "Logistico" instead of the company's name.
- Customers: a cancellation asked for while the parcel is on a truck cannot be applied by staff; searching the same
  number again after a typo does nothing.

## 3.0 (21 October 2026)

**Decision:** release candidate for controlled pilots, after the requirements in
[15 — Production readiness](15-production-readiness.md) are met.

### How 3.0 was put together

Two lines of work had grown from the same first commit: nine commits on the main branch (staff roles and team
management, demand intelligence, the operations-console theme, Content-Security-Policy, truck-manifest guards, customer
verification with lockout, Docker, open-source files) and local work that had not been committed (2.1 hardening,
warehouses and dashboard ratings, a second team implementation, documentation). 3.0 merges both. Where both sides built
the same thing, one design was kept and the other's useful parts were ported onto it:

| Area | Kept | Ported from the other side |
|---|---|---|
| Roles and team | Main branch: one role per person, policy convention on every module endpoint, router-level page protection, lock/unlock | Customer support role and `care` policy; administrator password reset; translated Identity messages; tenant-claim check inside every staff policy |
| Customer self-service verification | Main branch: last four digits with a 24-hour per-order lock | – (the complete-number check rejected foreign numbers) |
| Forwarded headers | Main branch: `ReverseProxy:KnownNetworks` | – |
| Roles in the database | Main branch: created at every start-up | – (a duplicate migration was dropped) |
| Hardening | Both | 2.1: exact-reference lookup for labels, merchant-origin check, bulk-booking validation, wrong-hub and inactive-hub scan guards |

### Findings and fixes

| Severity | Finding | Fix |
|---|---|---|
| High | Two administrators could lock or demote each other at the same moment and leave the company with no active administrator: the "last administrator" check and the change were not atomic. | Every team change runs in one transaction behind a per-company PostgreSQL advisory lock. Test: concurrent mutual locks always leave one active administrator. |
| Medium | Team changes were saved in several steps (remove roles, add role, security stamp); a failure in between left the account half-changed. | Same transaction: a failure rolls everything back. |
| Medium | A temporary lockout after wrong passwords was shown as "locked" and did not count as an active administrator. | Locked by an administrator and locked out after wrong passwords are now separate; the second is shown with its end time and still counts. Unlock clears both. |
| Medium | A request with `role: null` had to be answered with a validation problem, not an error. | Covered by a test (400 with the `Role` field). |
| Medium | Customer self-service rejected foreign phone numbers (2.1 complete-number check). | Main branch's last-four-digits verification with lockout kept. |
| Medium | No way to recover a forgotten password while email sending is a no-op. | Administrators set a new password on the Team page; the member's sessions end within a minute. |
| Low | The radar's "simulate scan" demo button wrote real scan events in production. | Removed. |
| Low | Customer ratings were collected but not shown. | Dashboard tile, courier column and demo ratings. |
| Low | In Arabic, currency and unit suffixes overlapped the number in money, weight and percentage inputs. | Inputs with a unit are laid out left-to-right with room for the unit. |
| Low | Documentation linked to renamed files and still spoke to a single owner. | Docs rewritten for contributors; links checked. |

### Verification

| Gate | Result |
|---|---:|
| Release build | 0 warnings, 0 errors |
| Code format | No changes needed |
| Shipment/domain tests | 207 passed |
| Architecture/UI guard tests | 31 passed |
| PostgreSQL integration tests | 62 passed |
| Browser checks | Arabic and English, desktop and 390 px, each role |

### Still open

See [13 — Roadmap](13-roadmap.md#known-limitations) and [15 — Production readiness](15-production-readiness.md): real
email sending, MFA enforcement, OTP or signed links for customer self-service, an audit log, load tests and an external
penetration test.

## 2.1 (20 October 2026)

**Audit date:** 20 October 2026  
**Release:** 2.1.0  
**Decision:** Code-complete release candidate. Suitable for controlled staging and pilot use after the deployment requirements below are met; not yet certified for unrestricted public production traffic.

### Executive assessment

Logistico is a capable modular logistics platform rather than a bad or disposable codebase. Its shipment lifecycle, Iraq hub routing, warehouse scanning, transfers, returns, customs and COD flows are unusually complete for a foundation release. The strongest areas are domain modelling, tenant query filters, optimistic concurrency, localized RTL/LTR UI and automated workflow coverage.

Before this audit, the largest risks were authorization that stopped at “signed in,” a tenant context that could silently fall back, weak public-action verification, and several delivery-control edge cases. Version 2.1 closes those defects. It also fixes batch-label truncation, malformed bulk imports, inconsistent booking origins, invalid hub fallback, keyboard accessibility, and third-party CI action pinning.

The system is not yet proven to operate at “very high efficiency” under production load. Queries used by the main work queues are projected and paginated, and the application is responsive with the seeded data set, but there is no repeatable concurrency/load-test result, production telemetry baseline or capacity model. Those are measurement gaps, not evidence of poor performance.

### Security and correctness changes in 2.1

- Role policies now separate staff read access, operational mutations, finance and configuration.
- Every authenticated API request must carry a valid, non-empty tenant claim; tenant resolution fails closed.
- Account POST endpoints and anonymous customer endpoints are rate limited.
- External login no longer bypasses two-factor authentication.
- Anonymous tracking omits seller, COD, delivery-area and exact hub-route data.
- Customer confirmations, ratings and service requests require the complete Iraqi phone number on the shipment instead of its last four digits. *(Changed in 3.0: this rejected customers with foreign numbers. 3.0 uses the last four digits with a per-order lock after five wrong answers, see above.)*
- GitHub Actions are pinned to immutable commit SHAs.
- Wrong-destination hub scans no longer mutate a shipment or detach it from its active transfer.
- Domestic orders cannot price one merchant origin while routing from another submitted origin.
- Explicit invalid or inactive hub scans are rejected instead of silently falling back.
- Bulk orders validate null items and every nested field, length and monetary range.
- Batch label printing resolves up to 200 exact IDs or tracking numbers rather than filtering the newest 100 shipments.

### Release verification

| Gate | Result |
|---|---:|
| Release build | Passed, 0 warnings and 0 errors |
| Shipment/domain tests | 172 passed |
| Architecture/UI guard tests | 31 passed |
| PostgreSQL integration tests | 34 passed |
| Total automated tests | 237 passed, 0 failed |
| Code format verification | 0 files required formatting |
| NuGet advisory scan | No vulnerable direct or transitive packages |
| Browser checks | Arabic RTL, English LTR, desktop, 390 px mobile, shipments, hub scanner and public tracking passed |
| Mobile document overflow | None detected |

### Remaining production requirements

These are intentionally not represented as completed by the 2.1 code release:

1. Replace the no-op email sender before enabling password reset, account confirmation or self-registration in production.
2. Add OTP or a short-lived customer capability token for sensitive self-service operations. A complete phone number is materially safer than four digits but is still knowledge-based verification.
3. Configure and test trusted proxy/forwarded-header handling at the ingress so IP rate limiting sees the real client without trusting spoofed headers.
4. Run authenticated load tests against a production-sized, anonymized data set and record p50/p95/p99 latency, throughput, database CPU, lock waits and error rate.
5. Complete an external penetration test and restore/backup exercise before unrestricted public launch.
6. Supply a durable shared data-protection key ring, production database secret, HTTPS termination and post-deployment health checks.
7. Remove the one-time bootstrap administrator password after provisioning and rotate it immediately.

### Suggested performance acceptance targets

Use these as a staging release gate rather than assuming performance from local response time:

- Shipment list/search: p95 under 500 ms at the expected concurrent staff load.
- Barcode receive/dispatch: p95 under 300 ms on the hub LAN, excluding printer time.
- Public tracking: p95 under 400 ms at the planned anonymous traffic rate.
- API error rate: below 0.1% excluding validation and authorization responses.
- No duplicate shipment transitions or lost transfer membership under concurrent scans.

The 2.1 changes are prepared in the working tree. Creating a Git tag, pushing the branch, publishing an image and promoting an environment remain explicit release-operations steps.
