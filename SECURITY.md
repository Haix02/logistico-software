# Security policy

Logistico stores customers' names, phone numbers and addresses and handles cash-on-delivery money. We take reports
seriously, fix confirmed issues before anything else, and appreciate responsible disclosure.

## Supported versions

| Version | Supported |
|---|---|
| 3.0.x | Yes |
| Older | No. Please upgrade. |

## Reporting a vulnerability

**Do not open a public issue.** Use GitHub's private reporting instead: the repository's **Security** tab →
**Report a vulnerability**. Only the maintainers can see the report. Please include:

- what an attacker can do and which data or money is at risk;
- steps to reproduce (requests, roles, tenant set-up) against a local instance with demo data, and the version or commit;
- any suggested fix.

We aim to acknowledge reports within **3 working days** and to agree a fix and disclosure date with you. We credit
reporters who wish to be named.

## Scope and design notes

In scope: the web host, the REST API, the Blazor client, the account pages, migrations and the container image. Out of
scope: findings that need a compromised server or database, denial of service by volume, and the demo credentials in
`appsettings.Development.json` (they exist for local development only).

Built-in protections you can rely on (and should not weaken in a pull request):

- **Tenant isolation**: every company's data is filtered by tenant in EF Core; public endpoints that look across
  tenants re-scope to the shipment's tenant immediately. Staff policies also require a valid tenant claim.
- **Roles on every endpoint**: reads need a staff role and changes need `operate`, `care`, `finance` or `administer`
  ([docs/10](docs/10-security-and-compliance.md#roles)); a test fails if an API endpoint has no staff policy.
- **Team changes** run one at a time per company in a transaction, so a company always keeps an active administrator.
- **Public tracking** shows no customer name, phone, address or declared value, and is rate-limited per client address.
- **Customer self-service** (change phone/address, confirm, rate) needs the last four digits of the order's mobile and
  locks an order for 24 hours after five wrong answers, whatever addresses the guesses come from.
- **Behind a proxy**, set `ReverseProxy:KnownNetworks` so rate limits see real client addresses.
- **Open data exports** contain aggregates only and leave out groups smaller than `Insights:OpenDataMinimumOrders`.
- Cookies are `HttpOnly`, `SameSite=Lax` and `Secure` outside Development; anti-forgery is on; a Content-Security-Policy
  with a per-response nonce applies to pages; API responses are `no-store`; CSV exports neutralise spreadsheet formulas.

## Running Logistico safely

Before exposing an installation to the internet, work through
[docs/15-production-readiness.md](docs/15-production-readiness.md). In short: HTTPS only, `DataProtection:KeyRingPath`
on durable storage, `Identity:AllowSelfRegistration` **false**, no `Seed:*` values except the first administrator (then
removed), database credentials from a secret store, a real email sender, and backups you have restored at least once.
