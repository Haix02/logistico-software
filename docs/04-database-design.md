# 04 — Database Design

> **Implemented so far (Phase 0):** schemas `iam` (ASP.NET Core Identity) and `shipment` (`shipments`, `shipment_events`, `tracking_number_seq`), snake_case naming, UUIDv7 keys generated in .NET, `xmin` concurrency, tenant query filters, separate EF Core migration history per schema. Everything else in this document is the target design.

## 1. Data stores and their jobs

| Store | Role | Source of truth? |
|---|---|---|
| **PostgreSQL 18 — OLTP cluster** | All business data: shipments, customs, inventory, fleet, billing, documents, audit | **Yes** |
| **PostgreSQL 18 + TimescaleDB — Telemetry cluster** | GPS positions, sensor readings, device events | Yes (for telemetry) |
| **Valkey** | Cache, rate limiting, SignalR backplane, short-lived locks | No |
| **Object storage (S3 API)** | Documents, POD photos, signatures, generated PDFs, label archives | Yes for blobs; metadata in Postgres |
| **NATS JetStream** | Event streams (retained 7–30 days), work queues | No (events originate in the outbox) |
| **Device SQLite** (mobile/desktop) | Offline working set | No (synced replica) |
| **ClickHouse** (phase 4) | Analytics/BI over history | No (CDC projection) |

Telemetry is a **separate cluster** because GPS writes (millions of rows per day) must never compete with customs clerks and invoice posting for I/O and vacuum.

> **Managed-cloud caveat:** AWS RDS and Azure Flexible Server do **not** offer TimescaleDB. If we use them, telemetry falls back to native partitioning + `pg_partman` + BRIN indexes (same table design, no compression). Running PostgreSQL on Kubernetes with **CloudNativePG** keeps TimescaleDB available and keeps us portable to Iraqi data centres. **Recommended: CloudNativePG.**

## 2. Schemas (one per module)

```
iam        parties     ref         rates       shipment    customs
wms        fleet       returns     billing     docs        telematics (separate cluster)
notify     integ       audit       sync        infra (outbox, inbox, idempotency, sequences)
```

Each module's EF Core `DbContext` maps only its own schema. Cross-module reads go through contracts. Reporting views that join schemas live in a dedicated `reporting` schema, are read-only, and run on replicas.

## 3. Conventions

| Topic | Convention |
|---|---|
| Naming | `snake_case`, plural table names, singular column names |
| Primary keys | `uuid` holding **UUIDv7**, generated in .NET with `Guid.CreateVersion7()` (works on PostgreSQL 16 locally and 18 in production; PostgreSQL 18 also has a native `uuidv7()`). Time-ordered, so B-tree inserts stay at the right edge with little page splitting. Safe to generate on clients for offline creation. |
| Human-readable numbers | Separate columns (`tracking_no`, `invoice_no`) with unique indexes |
| Tenant | `tenant_id uuid NOT NULL` on every business table, first column of most composite indexes |
| Timestamps | `timestamptz` always; `created_at`, `updated_at`, `created_by`, `updated_by` |
| Concurrency | Optimistic via `xmin` (system column) mapped as an EF Core concurrency token. No extra column needed. |
| Money | `numeric(19,4)` + `char(3)` currency. Never `float`, never `money`. |
| Weights / dims | `numeric(12,3)` kg, `numeric(8,2)` cm |
| Enumerations | `text` + `CHECK` constraint, or a lookup table if users manage the list. **No PostgreSQL `ENUM` types** (painful to change). |
| Flexible attributes | `jsonb` column `ext` for tenant custom fields, validated by JSON Schema per tenant; GIN index only where queried |
| Translations | `name_ar`, `name_en`, `name_ckb` columns for small fixed sets; `jsonb` `{ "ar": "...", "en": "..." }` for user-managed catalogues |
| Deletes | Business records are never hard-deleted: `status = 'cancelled' / 'archived'`. GDPR-style erasure is done by an anonymisation job. |
| Geography | PostGIS `geography(Point, 4326)` for locations; `geography(Polygon, 4326)` for geofences |

## 4. Core table sketches

These are design sketches. The final DDL comes from EF Core migrations, reviewed as SQL. (`shipment.shipment_events` is a plain table in Phase 0; it is converted to monthly partitions when volume requires it.)

### 4.1 Tenancy & parties

```sql
create table iam.tenants (
  id            uuid primary key default uuidv7(),
  code          text not null unique,
  name          text not null,
  default_currency char(3) not null default 'USD',
  settings      jsonb not null default '{}',
  created_at    timestamptz not null default now()
);

create table parties.parties (
  id            uuid primary key default uuidv7(),
  tenant_id     uuid not null,
  kind          text not null check (kind in ('customer','shipper','consignee','agent','carrier','broker','supplier')),
  code          text not null,
  legal_name    text not null,
  name_ar       text,
  tax_id        text,
  phone_e164    text,
  email         text,
  credit_limit  numeric(19,4),
  credit_currency char(3),
  status        text not null default 'active',
  ext           jsonb not null default '{}',
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now(),
  unique (tenant_id, code)
);
create index on parties.parties using gin (legal_name gin_trgm_ops);
create index on parties.parties (tenant_id, phone_e164);

create table parties.addresses (
  id            uuid primary key default uuidv7(),
  tenant_id     uuid not null,
  party_id      uuid not null references parties.parties(id),
  country_code  char(2) not null,
  governorate   text,           -- Iraq: Baghdad, Basra, Erbil, ...
  district      text,
  area          text,           -- neighbourhood / mahalla
  landmark      text,           -- "near Al-Mansour mall"
  line1         text,
  line1_ar      text,
  postal_code   text,
  location      geography(Point, 4326),
  phone_e164    text,
  is_default    boolean not null default false
);
create index on parties.addresses using gist (location);
```

### 4.2 Shipments

```sql
create table shipment.shipments (
  id                uuid primary key default uuidv7(),
  tenant_id         uuid not null,
  branch_id         uuid not null,
  tracking_no       text not null,
  house_bill_no     text,
  mode              text not null check (mode in ('SEA_FCL','SEA_LCL','AIR','ROAD','COURIER')),
  incoterm          text,
  shipper_id        uuid not null,
  consignee_id      uuid not null,
  origin_code       text not null,        -- UN/LOCODE or IATA (CNSHA, TRIST, AEDXB)
  destination_code  text not null,        -- IQUQR, IQBGW, ...
  entry_point_code  text,                 -- port/crossing where customs happens
  status            text not null,
  exception_flags   text[] not null default '{}',
  pieces            int not null default 0,
  gross_weight_kg   numeric(12,3),
  volume_cbm        numeric(12,4),
  chargeable_weight_kg numeric(12,3),
  declared_value    numeric(19,4),
  declared_currency char(3),
  cod_amount        numeric(19,4),
  cod_currency      char(3),
  promised_at       timestamptz,
  eta_at            timestamptz,
  delivered_at      timestamptz,
  master_bill_id    uuid,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now(),
  unique (tenant_id, tracking_no)
);
-- Active work queues only index what operators look at
create index shipments_active_idx on shipment.shipments (tenant_id, branch_id, status, eta_at)
  where status not in ('Delivered','Cancelled','Returned');
create index on shipment.shipments (tenant_id, consignee_id, created_at desc);
create index on shipment.shipments (tenant_id, master_bill_id);
create index on shipment.shipments using gin (tracking_no gin_trgm_ops);  -- partial-number search

create table shipment.packages (
  id              uuid primary key default uuidv7(),
  tenant_id       uuid not null,
  shipment_id     uuid not null references shipment.shipments(id),
  piece_no        int not null,
  barcode         text not null,            -- SSCC-18 or LX…-003
  weight_kg       numeric(12,3),
  length_cm       numeric(8,2), width_cm numeric(8,2), height_cm numeric(8,2),
  container_id    uuid,
  current_location_id uuid,                 -- wms location when in a warehouse
  unique (tenant_id, barcode)
);

-- Append-only event history; status is a projection of this.
create table shipment.shipment_events (
  id            uuid not null default uuidv7(),
  tenant_id     uuid not null,
  shipment_id   uuid not null,
  package_id    uuid,
  code          text not null,              -- internal code, mapped to DCSA/IATA
  occurred_at   timestamptz not null,       -- when it happened in the real world
  recorded_at   timestamptz not null default now(),
  source        text not null,              -- scan | gps | carrier | customs | user | system
  location_code text,
  location      geography(Point, 4326),
  actor_id      uuid,
  payload       jsonb not null default '{}',
  primary key (id, occurred_at)
) partition by range (occurred_at);
-- Monthly partitions managed by pg_partman; BRIN for time scans, B-tree for per-shipment timeline
create index on shipment.shipment_events (shipment_id, occurred_at);
create index on shipment.shipment_events using brin (occurred_at);
```

### 4.3 Customs

```sql
create table customs.declarations (
  id                uuid primary key default uuidv7(),
  tenant_id         uuid not null,
  shipment_id       uuid not null,
  regime            text not null,           -- IM4 (import home use), IM7 (warehousing), EX1, transit...
  office_code       text not null,           -- ASYCUDA customs office code
  system            text not null,           -- ASYCUDA_IQ | KRG_MANUAL | ORIGIN_EXPORT
  status            text not null,           -- draft | ready | exported_xml | lodged | assessed | paid | released | hold
  asycuda_reg_no    text, asycuda_reg_date date,
  assessment_no     text, receipt_no text,
  release_no        text,
  selectivity_lane  text,                    -- green | yellow | red | blue
  total_customs_value numeric(19,4), currency char(3),
  estimated_duties  numeric(19,4), paid_duties numeric(19,4),
  xml_document_id   uuid,                    -- generated SAD XML in docs
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table customs.declaration_items (
  id              uuid primary key default uuidv7(),
  declaration_id  uuid not null references customs.declarations(id),
  item_no         int not null,
  hs_code         text not null,             -- national tariff line
  description     text not null,
  origin_country  char(2) not null,
  packages        int, gross_kg numeric(12,3), net_kg numeric(12,3),
  quantity        numeric(14,3), unit text,
  item_value      numeric(19,4), currency char(3),
  procedure_code  text,
  unique (declaration_id, item_no)
);

create table customs.compliance_documents (
  id              uuid primary key default uuidv7(),
  tenant_id       uuid not null,
  shipment_id     uuid not null,
  kind            text not null,     -- COMMERCIAL_INVOICE | CERT_OF_ORIGIN | COC | PACKING_LIST | BL | AWB | LICENSE
  number          text not null,
  issuer          text,
  issued_on       date,
  e_verification_no  text,           -- mandatory for CoO + invoice since 2026-07-10
  e_verified_at   timestamptz,
  file_id         uuid               -- docs.files
);
```

### 4.4 Warehouse & inventory

```sql
create table wms.locations (
  id            uuid primary key default uuidv7(),
  tenant_id     uuid not null,
  warehouse_id  uuid not null,
  code          text not null,             -- A-01-03-B (zone-aisle-rack-level)
  kind          text not null,             -- receiving | storage | pick_face | staging | dock | quarantine | bonded
  capacity_cbm  numeric(10,3),
  unique (tenant_id, warehouse_id, code)
);

-- Immutable stock ledger
create table wms.inventory_transactions (
  id            uuid not null default uuidv7(),
  tenant_id     uuid not null,
  occurred_at   timestamptz not null default now(),
  owner_id      uuid not null,             -- 3PL: client that owns the stock
  sku_id        uuid not null,
  lot_id        uuid,
  location_id   uuid not null,
  stock_status  text not null,             -- available | quarantine | damaged | bonded
  qty_delta     numeric(14,3) not null,
  reason        text not null,             -- receive | putaway | move | pick | adjust | count
  ref_type      text, ref_id uuid,         -- task / order that caused it
  actor_id      uuid,
  primary key (id, occurred_at)
) partition by range (occurred_at);

-- Projection updated in the same transaction as the ledger insert
create table wms.inventory_balances (
  tenant_id     uuid not null,
  owner_id      uuid not null,
  sku_id        uuid not null,
  lot_id        uuid not null default '00000000-0000-0000-0000-000000000000',
  location_id   uuid not null,
  stock_status  text not null,
  qty_on_hand   numeric(14,3) not null default 0 check (qty_on_hand >= 0),
  qty_reserved  numeric(14,3) not null default 0 check (qty_reserved >= 0),
  check (qty_reserved <= qty_on_hand),
  primary key (tenant_id, owner_id, sku_id, location_id, lot_id, stock_status)
);
```

**Lock-free reservation:** a single conditional update reserves stock atomically. The `CHECK` constraints are the last line of defence.

```sql
update wms.inventory_balances
   set qty_reserved = qty_reserved + :qty
 where tenant_id = :t and owner_id = :o and sku_id = :s and location_id = :l
   and lot_id = :lot and stock_status = 'available'
   and qty_on_hand - qty_reserved >= :qty;     -- 0 rows updated → not enough stock
```

### 4.5 Fleet & telemetry (telemetry cluster)

```sql
create table telematics.positions (
  device_id     uuid not null,
  recorded_at   timestamptz not null,        -- device time
  received_at   timestamptz not null default now(),
  location      geography(Point, 4326) not null,
  speed_kmh     real,
  heading       smallint,
  altitude_m    real,
  satellites    smallint,
  ignition      boolean,
  odometer_km   numeric(12,1),
  attributes    jsonb                         -- fuel, temperature, door, battery...
);
select create_hypertable('telematics.positions', by_range('recorded_at', interval '1 day'));
create index on telematics.positions (device_id, recorded_at desc);

-- Compression (called "columnstore" in TimescaleDB >= 2.18)
alter table telematics.positions set (
  timescaledb.compress,
  timescaledb.compress_segmentby = 'device_id',
  timescaledb.compress_orderby   = 'recorded_at desc'
);
select add_compression_policy('telematics.positions', interval '7 days');
select add_retention_policy('telematics.positions', interval '400 days');

-- Hourly rollups for reports and fast history views
create materialized view telematics.positions_hourly
with (timescaledb.continuous) as
select device_id,
       time_bucket('1 hour', recorded_at) as bucket,
       count(*) as points,
       max(speed_kmh) as max_speed,
       last(location::geometry, recorded_at) as last_location
from telematics.positions
group by device_id, bucket;

-- Latest position per device: tiny hot table for the live map (upserted)
create table telematics.device_last_position (
  device_id uuid primary key,
  recorded_at timestamptz not null,
  location geography(Point, 4326) not null,
  speed_kmh real, heading smallint, ignition boolean
);
```

Geofences live in the OLTP cluster (`fleet.geofences`, GiST-indexed polygons). Telematics caches them in memory and evaluates `ST_Covers` for state transitions only (enter/exit), not every point.

### 4.6 Platform tables

```sql
-- Transactional outbox: written in the same transaction as the business change
create table infra.outbox (
  id            uuid primary key default uuidv7(),
  occurred_at   timestamptz not null default now(),
  tenant_id     uuid,
  subject       text not null,               -- e.g. lx.shipment.status_changed.v1
  payload       jsonb not null,
  headers       jsonb not null default '{}',
  published_at  timestamptz
);
create index outbox_unpublished on infra.outbox (occurred_at) where published_at is null;

-- Consumer-side dedupe
create table infra.inbox (
  consumer      text not null,
  event_id      uuid not null,
  processed_at  timestamptz not null default now(),
  primary key (consumer, event_id)
);

-- API idempotency
create table infra.idempotency_keys (
  tenant_id uuid not null, key text not null,
  request_hash bytea not null, response jsonb, status_code int,
  created_at timestamptz not null default now(),
  primary key (tenant_id, key)
);

-- Gapless document numbering (invoices): row lock inside the business transaction
create table infra.number_series (
  tenant_id uuid not null, series text not null, year int not null,
  next_value bigint not null default 1,
  primary key (tenant_id, series, year)
);

-- Print jobs routed to site device services
create table docs.print_jobs (
  id uuid primary key default uuidv7(),
  tenant_id uuid not null, site_id uuid not null, printer_id uuid not null,
  template_id uuid not null, template_version int not null,
  data jsonb not null, copies int not null default 1,
  status text not null default 'queued',      -- queued | sent | printing | done | failed
  error text, requested_by uuid, requested_at timestamptz not null default now(),
  completed_at timestamptz
);

-- Audit (partitioned monthly, hash-chained per tenant for tamper evidence)
create table audit.entries (
  id uuid not null default uuidv7(),
  tenant_id uuid not null,
  occurred_at timestamptz not null default now(),
  actor_id uuid, actor_ip inet, client text,
  action text not null,                         -- create | update | delete | view_sensitive | login | export
  entity text not null, entity_id uuid,
  changes jsonb,                                -- {field: [old, new]}
  prev_hash bytea, hash bytea not null,
  primary key (id, occurred_at)
) partition by range (occurred_at);
```

## 5. Multi-tenancy with row-level security

```sql
alter table shipment.shipments enable row level security;
alter table shipment.shipments force row level security;
create policy tenant_isolation on shipment.shipments
  using (tenant_id = current_setting('app.tenant_id')::uuid)
  with check (tenant_id = current_setting('app.tenant_id')::uuid);
```

- The API opens each transaction with `SET LOCAL app.tenant_id = '…'`. `SET LOCAL` is safe with PgBouncer transaction pooling.
- The application role does **not** have `BYPASSRLS`. Migrations and maintenance use a separate role.
- EF Core global query filters also filter by tenant (belt and braces) and keep query plans index-friendly.

## 6. Indexing & query rules

1. Every query path used by a screen has an index whose **leading columns match its filter** (`tenant_id`, then the selective filter, then the sort).
2. **Partial indexes** for "active" work queues: most shipments are delivered and never queried operationally again.
3. **BRIN** indexes on append-only time columns (events, ledger, audit). Tiny and fast for range scans.
4. **Trigram GIN** for "contains" search on tracking numbers, names and phone numbers.
5. Foreign keys are indexed (PostgreSQL does not do this automatically).
6. **No** `SELECT *` in read models. Covering indexes (`INCLUDE (...)`) for the top 20 hot queries.
7. PostgreSQL 18's **B-tree skip scan** helps multi-column indexes when the leading column is not filtered, but we still design for leading-column use.

## 7. Partitioning & retention

| Table | Partition | Retention online | Then |
|---|---|---|---|
| `shipment.shipment_events` | Monthly | 36 months | Detach → Parquet in object storage (queryable via ClickHouse) |
| `wms.inventory_transactions` | Monthly | 36 months | Same, after balances are snapshotted |
| `audit.entries` | Monthly | 10 years (⚠ confirm the legal requirement) | Cold storage after 24 months |
| `telematics.positions` | Daily chunks (Timescale) | 400 days raw, compressed after 7 days | Hourly aggregates kept 5 years |
| `notify.deliveries` | Monthly | 12 months | Drop |
| Customs declarations & documents | Not partitioned | **Never deleted** inside the retention period (⚠ confirm the Iraqi customs record-keeping period) | Archive tier |

## 8. Migrations (zero-downtime)

- EF Core migrations per module → `dotnet ef migrations script --idempotent` → **reviewed SQL** in the PR.
- A migration linter (e.g., Squawk) blocks unsafe operations: `ADD COLUMN ... DEFAULT volatile`, non-concurrent index creation, `ALTER TYPE`, long `ACCESS EXCLUSIVE` locks.
- **Expand → migrate → contract:** add the new column, dual-write, backfill in batches, switch reads, drop the old column in a later release.
- Migrations run as a Kubernetes **Job** (Argo CD PreSync hook) using an EF migration bundle, before new pods start.
- `lock_timeout = '5s'` and `statement_timeout` are set in migration sessions so nothing ever hangs production.

## 9. High availability, backup, DR

| Item | Design |
|---|---|
| Operator | **CloudNativePG** on Kubernetes (or a managed PostgreSQL if TimescaleDB is not needed) |
| Topology | 1 primary + 2 replicas across zones; **synchronous quorum** commit (`ANY 1`) → zero data loss within the region |
| Pooling | PgBouncer (CNPG `Pooler`), transaction mode |
| Backups | Continuous WAL archiving + daily base backups to object storage (Barman Cloud), encrypted |
| PITR | 30-day point-in-time recovery window |
| DR | Replica cluster in a second region / Iraqi data centre fed from WAL archive; **RPO ≤ 5 min, RTO ≤ 30 min** |
| Drills | Monthly automated restore into a scratch cluster + checksum and row-count verification; quarterly DR failover rehearsal |
| Upgrades | Minor: rolling. Major: logical replication blue/green. |

## 10. Capacity planning (initial assumptions, to revisit)

| Metric | Year 1 | Year 3 (design target ×10 headroom) |
|---|---|---|
| Shipments / day | 1,000 | 10,000 |
| Packages / day | 5,000 | 50,000 |
| Shipment events / day | 30,000 | 300,000 |
| Vehicles with trackers | 150 | 1,500 |
| GPS points / day (10 s interval while moving) | ~1.3 M | ~13 M |
| Inventory SKUs (all owners) | 50,000 | 500,000 |
| OLTP DB size (excl. telemetry) | ~50 GB | ~500 GB |
| Telemetry compressed / year | ~30 GB | ~300 GB |

Starting OLTP primary: 8 vCPU / 32 GB RAM / NVMe 500 GB (provisioned IOPS). Starting telemetry primary: 8 vCPU / 32 GB / 1 TB.
