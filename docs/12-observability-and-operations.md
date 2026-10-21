# 12 — Observability & Operations

> **Status:** 🟡 planned for Phase 1. Today the app writes structured ASP.NET Core logs to the console. The tooling choices below (Grafana stack, Sentry) are proposals.

## 1. Telemetry

| Signal | Source | Destination (proposed) |
|---|---|---|
| Traces | OpenTelemetry .NET (ASP.NET Core, HttpClient, EF Core/Npgsql instrumentation) | Tempo |
| Metrics | OpenTelemetry metrics + runtime/process metrics; PostgreSQL exporter | Prometheus / Mimir |
| Logs | `ILogger` → OpenTelemetry logs (JSON), trace-correlated | Loki |
| Browser errors | Blazor `ErrorBoundary` + JS error hook → `/api/v1/client-errors` | Loki (+ Sentry/GlitchTip optional) |
| Dashboards & alerts | — | Grafana |

Every log line and span carries `tenant_id`, `user` (hashed), `trace_id` and, where relevant, `tracking_number`.

## 2. Service level objectives

| SLO | Target | Measured by |
|---|---|---|
| API availability | 99.9 % monthly (non-5xx / all) | Gateway metrics |
| API read latency | p95 < 150 ms | Server histograms |
| Grid first page | p95 < 400 ms | `/shipments/grid` histogram |
| Booking latency | p95 < 300 ms | `POST /shipments` histogram |
| Tracking freshness (Phase 2) | Vehicle on map < 5 s after device send | Telematics pipeline lag |
| Print latency (Phase 1–2) | Scan → label < 1 s on site | Device Service job timings |

Error budgets drive release pace: when a budget is burned, feature releases pause for reliability work.

## 3. Business health dashboards

Technical health is not enough; operations needs to see the business:

- Shipments **stuck**: no event for more than 72 h, by status and branch
- **Customs dwell** per entry point (Umm Qasr, Baghdad Airport, Safwan, Ibrahim Khalil…)
- **Customs holds** and missing e-verification numbers
- On-time delivery %, promise versus ETA slippage
- COD outstanding by driver/branch (Phase 2)
- Devices offline: GPS trackers, printers, edge nodes (Phase 2)

## 4. Alerting

| Alert | Severity | Route |
|---|---|---|
| API 5xx rate > 2 % for 5 min | Critical | On-call phone |
| p95 latency > 2× SLO for 10 min | Warning | Team channel |
| PostgreSQL replication lag > 60 s, disk > 80 %, connections > 80 % | Critical | On-call |
| Backup missing or restore drill failed | Critical | On-call + lead |
| Edge node offline > 10 min (Phase 2) | Warning | Site support |
| Certificate expiring < 14 days | Warning | Team channel |

## 5. Runbooks (to write alongside each alert)

Each runbook covers: symptoms → dashboards to open → likely causes → safe actions → escalation. Phase 1 set: API down, database failover, slow grid queries (find the query in `pg_stat_statements`, check the plan, add an index through a migration), failed deployment rollback, restoring a deleted record from PITR, licence/certificate expiry.

## 6. Operations routines

| Routine | Frequency |
|---|---|
| Restore drill (latest backup into a scratch database, row-count checks) | Monthly |
| Dependency & security update review (Dependabot PRs) | Weekly |
| `pg_stat_statements` top-20 review | Each sprint |
| DR failover rehearsal | Quarterly (once DR exists) |
| Access review (who has admin / finance rights) | Quarterly |
