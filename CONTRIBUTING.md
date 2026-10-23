# Contributing to Logistico

Thank you for helping build open logistics software for Iraq. Contributions of every size are welcome: a missing Arabic
word in the product catalogue, a bug report from a hub, a new courier integration, or a whole module.

Please follow our [Code of Conduct](CODE_OF_CONDUCT.md). Security problems go through [SECURITY.md](SECURITY.md), never a
public issue.

## Ways to help

- **Report a bug or idea**: open an issue with the templates. Screenshots in Arabic and English help a lot.
- **Improve the data**: product words in Arabic, Kurdish (Sorani) or English for `ProductCatalog`, governorate and
  area names, delivery rules that match how Iraqi companies really work.
- **Write code**: pick an issue labelled `good first issue` or `help wanted`, or propose a change in an issue first if
  it is large (a new module, a database change, a new dependency). The [roadmap](docs/13-roadmap.md) lists what is next.
- **Translate and review texts**: every screen exists in Arabic (default) and English.

## Run it

**With Docker** (any OS, nothing else to install):

```bash
docker compose up --build
# http://localhost:5210  ·  admin@logistico.local / Logistico#2026
```

**With the .NET SDK** (for development): .NET 10 SDK (see `global.json`) and PostgreSQL 16+.

```bash
psql -d postgres -c "CREATE ROLE logistico LOGIN PASSWORD 'logistico_dev' CREATEDB;"
dotnet tool restore
dotnet run --project src/Logistico.Web --launch-profile http   # migrates and seeds demo data
```

Or start only the database from the compose file: `docker compose up db`, then point
`ConnectionStrings__Logistico` at `Host=localhost;Port=5432;Database=logistico;Username=logistico;Password=logistico_dev`
(map the port by adding `ports: ["5432:5432"]` to the `db` service).

The demo credentials exist only in Development settings. `Seed:DemoTeam` adds `ops@`, `finance@` and
`viewer@logistico.local` (same password) so you can try each role. `dotnet watch --project src/Logistico.Web` rebuilds
the server and the WebAssembly client when you save.

## Before you open a pull request

Run what CI runs:

```bash
dotnet format Logistico.slnx --verify-no-changes --severity warn
dotnet build Logistico.slnx -c Release          # warnings are errors
dotnet test Logistico.slnx -c Release           # integration tests need PostgreSQL (LOGISTICO_TEST_DB)
```

The integration tests create and drop a throw-away database per test class on the local PostgreSQL server; set
`LOGISTICO_TEST_DB` (a full connection string) to use another server.

If you changed an EF Core model, add a migration for that module's context and check it:

```bash
dotnet ef migrations add <Name> --project src/Modules/Shipments/Logistico.Modules.Shipments --startup-project src/Logistico.Web --context ShipmentsDbContext
dotnet ef migrations has-pending-model-changes --project src/Modules/Shipments/Logistico.Modules.Shipments --startup-project src/Logistico.Web --context ShipmentsDbContext
```

Never edit a migration that has been released; add a new one. Data changes in a migration must be safe on databases
that already have the data, and breaking schema changes follow expand → migrate → contract
([docs/11](docs/11-ci-cd-and-devops.md)).

## House rules

The architecture tests (`tests/Logistico.ArchitectureTests`) and `RoleAccessTests` enforce most of these; the rest are
conventions.

**Modules**
- One deployable, strict module boundaries: a module talks to another only through its `*.Contracts` project.
- Domain rules live in the domain (`Shipment`, `HubTransfer`…) or in pure rules under `Contracts/Rules`, with unit tests.
  `*.Contracts` projects must stay free of EF Core and ASP.NET Core: the browser uses them too.
- Every tenant-owned table has a global query filter on `TenantId`. `IgnoreQueryFilters()` is allowed only with an
  explicit tenant condition next to it, and only where the request has no signed-in tenant (public tracking, jobs).

**Roles**
- Module endpoints get a staff role automatically: `staff` to read and `operate` to change. Ask for the narrowest
  stricter policy where it applies: `care` (customer calls, requests, messages), `finance` (prices, payouts) or
  `administer` (rules, warehouses, team, open data). The table is in [docs/10](docs/10-security-and-compliance.md#roles).
- Add a test that a role which must be refused gets `403`.
- In the browser, wrap actions in `<Allowed Policy="@StaffPolicies.X">`, show settings through `<EditableBy>`, and give
  pages that only some roles may open `@attribute [Authorize(Policy = StaffPolicies.X)]` and a `Policy` in
  `AppNavigation`. This only hides what the API refuses anyway.

**Arabic first**
- Every user-visible text is a resx key, in **both** `*.resx` and `*.ar.resx` (Arabic plural forms `_One`, `_Two`,
  `_Few`, `_Many` where counts appear, used through `L.Count`). Never hard-code text in a page.
- CSS uses logical properties only (`margin-inline-start`, `inset-inline-end`, `text-align: start`). A test rejects
  `left`/`right`.
- Mixed Arabic/Latin user text goes through `<Mixed>`; phone numbers, codes and numbers stay left-to-right.
- Do not letter-space or upper-case Arabic: those rules are scoped to `html[lang^="en"]`.
- Write Arabic that an Iraqi dispatcher would actually say, and keep it gender-neutral where you can.

**UI**
- Use the components in `src/Logistico.Web.Client/Ui` (Blueprint markup) and the `--lx-*` colour tokens in `app.css`;
  check your screen in dark and light, Arabic and English, and at 390 px wide. Please do not add a component library
  or a JavaScript framework; open an issue if you think one is needed.
- Icons must exist in Blueprint's icon font (a test checks).

**API**
- REST under `/api/v1`, RFC 9457 problem details with stable `code`s, `If-Match` for updates.
- Validate requests with DataAnnotations/`IValidatableObject` on the contract types.

## Tests

| Project | What belongs there |
|---|---|
| `tests/Logistico.Modules.Shipments.Tests` | Domain rules and pure functions: state machines, routing, pricing, risk, catalogue, formatting. Fast, no database. |
| `tests/Logistico.ArchitectureTests` | Module boundaries, texts in both languages, UI conventions. |
| `tests/Logistico.Web.IntegrationTests` | The real API on real PostgreSQL: flows, roles, tenant isolation, concurrency. |

Every behaviour change needs a test that would have failed before it; a bug fix starts with the test that reproduces it.

## Commits and pull requests

- `main` is protected: changes arrive through a pull request that passes the **Build & test** check and is approved by
  a maintainer (see `.github/CODEOWNERS`). Direct pushes and force-pushes to `main` are refused.
- Small, focused pull requests with a clear description of the *why*; link the issue.
- Add or update tests with every behaviour change.
- For UI changes, add screenshots in Arabic and English (and at phone width if the layout changed). No real customer data.
- Keep the docs in `docs/` true: if you change a decision, update the ADR log (`docs/14-architecture-decisions.md`), and
  add a line under `Unreleased` in `CHANGELOG.md` for anything users or operators notice.

## Licence

By contributing you agree that your contribution is licensed under the [Apache License 2.0](LICENSE), the licence of
this project.
