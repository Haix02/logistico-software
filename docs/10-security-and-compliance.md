# 10 — Security & Compliance

Target: **OWASP ASVS Level 2** for the web app and API. ✅ = implemented, 🟡 = planned.

## 1. Identity & sign-in

| Control | Design | Status |
|---|---|---|
| Identity store | ASP.NET Core Identity in PostgreSQL schema `iam` | ✅ |
| Session | HttpOnly, Secure (behind HTTPS), SameSite=Lax cookie `logistico.auth`; 10 h sliding expiry | ✅ |
| Passwords | Minimum 10 characters + Identity complexity rules; PBKDF2 hashing (Identity v3) | ✅ |
| Lockout | 5 failed attempts → temporary lockout. Administrators can deactivate an account (indefinite lockout) | ✅ |
| Session revocation | Role changes, deactivation and password resets update the security stamp; open sessions are re-checked every minute | ✅ |
| Error messages | Identity's password and account messages are translated (Arabic and English) | ✅ |
| MFA | TOTP authenticator apps and **passkeys** (WebAuthn) pages are present; MFA **required** for all staff | Pages ✅, enforcement 🟡 |
| Self-registration | Disabled outside Development (`Identity:AllowSelfRegistration`). Self-registered accounts get **no role** and see nothing until an administrator assigns one. Staff accounts are normally created on the **Team** page | ✅ |
| Email | No-op sender today; a real sender is required before enabling confirmation or password-reset emails ([15](15-production-readiness.md)) | 🟡 |
| SSO | OIDC/Azure AD/Google Workspace external logins can be added to Identity later | 🟡 |
| API clients (partners, mobile) | Bearer tokens (Identity bearer endpoints or OpenIddict) with scopes | 🟡 Phase 2/4 |

## 2. Authorization

- **Every API endpoint needs a staff role by default.** The host maps the modules inside a route group whose convention runs last
  (`StaffAuthorization.RequireStaffRoles`): reads get the `staff` policy, any other HTTP method gets `operate`, unless the
  endpoint is anonymous or asks for a stricter policy itself. A test walks every `api/` endpoint and fails if one has no
  staff policy or is anonymous without being on the public list (`RoleAccessTests`). ✅
- **Anonymous endpoints are explicit** (`AllowAnonymous`): `account/me` and the four public tracking routes. ✅
- **Tenant isolation:** the tenant comes from the user's claim; EF Core query filters apply it to every query. An integration test proves one tenant cannot read another's shipment, and administrators only see and change their own company's team. ✅
- **Roles** (one per person, `StaffRoles`) and the **policies** that the API enforces and the browser uses to hide what a role cannot use (`StaffPolicies`), see [Roles](#roles) below. ✅

  A signed-in account **without a role** (for example one that registered itself in Development) gets `403 auth.forbidden`
  from every company endpoint and a "waiting for access" page in the browser.
- **Team management** (`/team`, administrators): add members with a role and a first password, change roles or take access
  away, lock and unlock, and set a new password for someone who forgot theirs. Nobody can demote, lock or reset
  themselves here, and a company always keeps one active administrator (`409 team.last_admin`). Each change runs in one
  transaction behind a per-company advisory lock, so two administrators acting at once are applied one after the other
  and a failure leaves the account unchanged. A lockout after wrong passwords is shown as temporary and does not count
  as a locked administrator. Role changes, locks and new passwords update the security stamp; cookies are re-validated
  every minute, so the change reaches open sessions within a minute. ✅
- **Policies also check the tenant claim:** an account without a valid company (tenant) claim gets `403`, whatever its role. ✅

### Roles

| Can… | Admin | Operations | Customer support | Finance | Viewer | Courier | Seller |
|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| See dashboards, parcels, maps and insights (`staff`) | ✓ | ✓ | ✓ | ✓ | ✓ | | |
| Book and edit orders, scan at hubs, trucks, couriers, returns, customs (`operate`) | ✓ | ✓ | | | | | |
| Confirmation calls, customer requests and messages, customer lookup (`care`) | ✓ | ✓ | ✓ | | | | |
| Download customer lists as CSV (`operate`: names, phones and addresses in bulk) | ✓ | ✓ | | | | | |
| See cash on delivery, seller balances and settlements; change delivery prices, settle and download payouts (`finance`) | ✓ | | | ✓ | | | |
| Change automation rules and warehouses, export open data, manage the team (`administer`) | ✓ | | | | | | |
| Courier app: their own parcels out for delivery, delivered or failed (`deliver`) | | | | | | ✓ | |
| Seller portal: one shop's own orders (book, follow, cancel before pickup), balance and payout statements (`sell`) | | | | | | | ✓ |

- **Data scopes** (🟡): branch, warehouse and customer scopes limit which rows a user sees.
- **Field-level protection** (🟡): costs, margins and declared values are hidden from roles that do not need them.
- **Defence in depth** (🟡): PostgreSQL row-level security on tenant tables ([04](04-database-design.md) §5).

## 3. API & web hardening

| Control | Status |
|---|---|
| API returns 401/403, never login redirects | ✅ |
| Problem Details without stack traces outside Development | ✅ |
| Input validation on server for every request (DataAnnotations + domain rules) | ✅ |
| Optimistic concurrency (ETag/If-Match) to prevent lost updates | ✅ |
| CSRF: account pages use antiforgery tokens; JSON API relies on SameSite=Lax + JSON content type (no simple cross-site POSTs) | ✅ |
| HTTPS redirection + HSTS (non-Development) | ✅ |
| Public tracking returns no personal or commercial data (tested) | ✅ |
| Tracking numbers have a check digit, so guessed numbers usually fail validation | ✅ |
| Rate limiting: public tracking 60/min, customer self-service 10/10 min, sign-in and account forms 30/min per client address; 429 answers say why (problem details `rate_limited`, `Retry-After`) | ✅ |
| Content-Security-Policy: scripts only from this origin plus a per-response nonce for the inline theme bootstrap and import map (`'wasm-unsafe-eval'` for WebAssembly), no plugins, no framing, `form-action 'self'`; tested in `PlatformHardeningTests` | ✅ |
| `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, `Cross-Origin-Opener-Policy`, `X-Frame-Options`; API answers `Cache-Control: no-store` | ✅ |
| API answers compressed (Brotli/gzip); pages are not, so no secret is ever compressed next to reflected input (BREACH) | ✅ |
| Customer self-service (confirm, rate, change, cancel) needs the last four digits of the order's mobile and locks the order's self-service for 24 hours after five wrong answers; works for Iraqi and foreign numbers alike | ✅ (OTP or signed links 🟡) |
| Signed webhooks (HMAC-SHA256) and partner API keys | 🟡 Phase 4 |

## 4. Data protection

- **In transit:** TLS 1.2+ (1.3 preferred) at the edge; TLS to PostgreSQL in hosted environments.
- **At rest:** encrypted disks/volumes and encrypted backups. Sensitive personal fields (national ID numbers, receiver ID photos) get application-level envelope encryption with a KMS-held key (🟡).
- **ASP.NET Core Data Protection keys** persisted to a durable shared folder (`DataProtection:KeyRingPath`); outside Development the app refuses to start without it, so cookies survive restarts and scale-out. ✅
- **Secrets:** never in the repository. Development uses `appsettings.Development.json` with local-only credentials; CI uses GitHub secrets; production uses a secret manager.
- **Retention:** customs records kept for the legally required period (⚠ confirm with Iraqi counsel); GPS data 400 days raw; personal data anonymised on request where the law allows.
- **Driver privacy:** location is collected only while on duty, and drivers are told so in the app.

## 5. Audit

- Who did what, when: `created_by`/`recorded_by` on shipments and events, `updated_by` on warehouses, rate cards and rules, and log entries for every team change (who added, changed or reset whom). ✅
- A full **audit log** (🟡 Phase 1): append-only, hash-chained per tenant ([04](04-database-design.md) §4.6), covering logins, permission changes, exports, sensitive reads, and every change to customs, finance and shipment data.

## 6. Trade compliance (built into Customs, Phase 1–3)

- **Restricted/denied party screening** against UN, US OFAC SDN, EU and UK lists on shipper, consignee, notify party and payer. Matches put the shipment on hold for a compliance officer.
- **Prohibited/restricted goods** by HS code and keywords (weapons, dual-use, drugs).
- **Dangerous goods** rules (IATA DGR, IMDG) by mode.
- **Sanctioned routing** checks (transhipment points, vessels).
- Every screening result is stored with the list version used.

## 7. Supply chain security

| Control | Status |
|---|---|
| NuGet audit on every restore; vulnerabilities fail the build | ✅ |
| Vulnerable transitive packages pinned to patched versions (central transitive pinning) | ✅ |
| Dependabot weekly updates for NuGet and GitHub Actions | ✅ |
| Container image built by the .NET SDK on a minimal "chiseled" Ubuntu base (no shell, non-root) | ✅ |
| Image signing (cosign keyless) and SBOM (Syft) in CI | 🟡 |
| Pin GitHub Actions to commit SHAs | ✅ |
| Annual external penetration test before major releases | 🟡 |

## 8. Known gaps to close before production

The full deployment checklist is in [15 — Production readiness](15-production-readiness.md).

1. Real email sender (account confirmation, password reset by email; administrators can already set a new password on the Team page).
2. MFA enforcement for staff.
3. Inline `style` attributes still need `style-src 'unsafe-inline'`; moving them to classes would allow a stricter policy.
4. Audit log of who changed what (roles are in place, §2).
5. Keep the .NET runtime on servers patched (development uses SDK 10.0.4xx / runtime 10.0.13).

Data Protection keys are already required on a durable shared volume outside Development (`DataProtection:KeyRingPath`;
start-up fails without it), so cookies survive restarts and scale-out.
