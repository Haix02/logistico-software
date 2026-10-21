# 17 — Release audits

Each release records what was checked, what was found and what changed, so contributors and operators can see why the
code looks the way it does. The newest release comes first.

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
