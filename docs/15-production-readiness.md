# 15 — Production readiness

Logistico 3.0 is suitable for a controlled pilot once the items below are in place. This page lists the configuration the app reads, what to do before real customers' data goes in, and how to check that an installation performs well enough.

## 1. Configuration

Settings come from `appsettings.json`, environment variables (`Section__Key`, for example `ConnectionStrings__Logistico`; list items take an index, as in `ReverseProxy__KnownNetworks__0=10.0.0.0/8`) or any other ASP.NET Core configuration source. Never put production secrets in the repository.

| Setting | Default | Notes |
|---|---|---|
| `ConnectionStrings:Logistico` | *(required)* | PostgreSQL connection string. The app refuses to start without it. |
| `DataProtection:KeyRingPath` | *(required outside Development)* | A durable folder shared by all instances. Sign-in cookies are encrypted with these keys; losing them signs everyone out. |
| `Database:MigrateOnStartup` | `false` | `true` applies migrations when the app starts (fine for one instance). For several instances, run the migration bundles from CI before starting the new version. |
| `Identity:AllowSelfRegistration` | `false` | Leave off. Self-registered accounts get no role, but there is no reason to let strangers create accounts. |
| `Seed:AdminEmail`, `Seed:AdminPassword` | *(empty)* | Creates the first administrator at start-up. Use once, then remove (see §2). |
| `Seed:DemoShipments` | `0` | Demo data. Keep `0` in production. |
| `Automation:Enabled` | `true` | The background sweep (alerts, truck planning, auto-cancel). One instance at a time runs it (PostgreSQL advisory lock). |
| `ReverseProxy:KnownNetworks` | *(empty)* | CIDR ranges of your reverse proxies (`10.0.0.5/32` for one address, `10.0.0.0/8` inside a cluster). Only then are `X-Forwarded-For` and `X-Forwarded-Proto` trusted. |
| `Seed:DemoTeam` | `false` | Development only: adds one demo account per role, with the administrator's password. |
| `Insights:OpenDataMinimumOrders` | `10` | Groups with fewer orders are left out of the anonymised open-data export. |

Health endpoints for the orchestrator: `/health/live` (process) and `/health/ready` (PostgreSQL reachable). Both are anonymous and uncached.

## 2. Before go-live

**Platform**

1. HTTPS at the edge (TLS 1.2+), with HSTS. Behind a proxy, set `ReverseProxy:KnownNetworks` so the app sees the real client address: rate limits for sign-in, tracking and customer self-service are per client address, and without it every visitor shares the proxy's address and its limits.
2. `DataProtection:KeyRingPath` on a durable volume shared by all instances.
3. PostgreSQL with automated backups, and a restore you have actually performed into a fresh database.
4. A patched .NET runtime and PostgreSQL version, and a way to apply updates.

**Accounts**

5. Bootstrap the first administrator with `Seed:AdminEmail` / `Seed:AdminPassword`, sign in, add the real administrators on the **Team** page, then **remove the seed settings** and deactivate or re-password the bootstrap account.
6. Give every colleague the narrowest role that fits ([10](10-security-and-compliance.md#roles)). Keep at least two administrators.
7. Ask staff to turn on two-step sign-in under *My account* (the Team page shows who has). Enforcement is not built yet.
8. Replace the no-op email sender (`IdentityNoOpEmailSender`) with a real one before relying on password reset or email confirmation by email. Until then, administrators set new passwords on the Team page.

**Data and customers**

9. Review the customer self-service actions on the public tracking page (confirm, rate, change, cancel). They require the last four digits of the mobile number on the order and lock the order's self-service for a day after five wrong answers. That is knowledge-based, not strong authentication; decide whether it is acceptable for your customers until OTP or signed links exist.
10. Set your company's real warehouses, prices and automation rules (the defaults are examples).
11. Decide how long you keep customer data, and who may export it.

**Assurance**

12. Run a load test (§3) on a production-sized, anonymized data set.
13. Have the installation penetration-tested before opening it to the public internet.

## 3. Performance acceptance

Measure these on staging with a realistic data set and the expected number of concurrent staff, rather than assuming them from a laptop:

| Scenario | Target |
|---|---|
| Shipment list and search | p95 < 500 ms |
| Barcode receive / dispatch scan | p95 < 300 ms on the hub network (excluding printing) |
| Dashboard (7 days) | p95 < 800 ms |
| Public tracking | p95 < 400 ms at the planned anonymous traffic |
| Error rate | < 0.1 % excluding validation and authorization answers |
| Concurrency | No duplicate status changes or lost truck membership under concurrent scans of the same parcels |

Record p50/p95/p99, throughput, database CPU, lock waits and error rate, and keep the results with the release.

## 4. Operations

- Logs go to standard output in the container; collect them centrally. Team changes (who added, changed or reset whom) and automation actions are logged.
- Watch `/health/ready`, the automation sweep's last run (shown on the automation page) and PostgreSQL disk and connection usage.
- See [12 — Observability](12-observability-and-operations.md) for the fuller plan and [05](05-infrastructure-and-deployment.md) for hosting options.
