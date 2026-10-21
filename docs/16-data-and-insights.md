# 16 — Data, demand intelligence and open data

Iraq's online commerce runs on Instagram shops, cash on delivery and couriers who know their neighbourhoods. Until now
no one could see what Iraqi customers actually order, where, when and how those orders end. Every delivery company holds
a piece of that picture. Logistico turns each company's own deliveries into **demand intelligence** and lets companies
**publish anonymised totals** so the whole market, from sellers to couriers to planners, can learn from them.

## 1. The demand intelligence page (`/insights`)

Menu: *Intelligence → Demand intelligence* (shortcut `g e`). Periods: 30 days (default), 90 days, 12 months, each
compared with the period just before it.

| Panel | Question it answers | How it is computed |
|---|---|---|
| KPI rail | How many orders, how many delivered, how big a basket, how many customers, how loyal | Domestic orders booked in the period, cancelled ones excluded |
| Key findings | What should I act on first? | Rules in `Pages/Insights/InsightFindings.cs` (below) |
| Demand across Iraq | Where do orders go, and where do they succeed? | Choropleth of the 18 governorates by orders, delivery success, basket or delivery time |
| Governorates | Growth, success, basket and the leading product per governorate | Same orders grouped by the customer's governorate; growth against the previous period |
| What customers buy | Which products, and which come back? | Item descriptions classified into categories (§2) |
| When Iraq orders | Which weekday and hour (Baghdad time) | Weekday × hour heat grid of booking times |
| Basket size and refusals | Are expensive cash-on-delivery orders refused more? | Orders in COD bands: prepaid, < 25K, 25–50K, 50–100K, 100–200K, ≥ 200K IQD |
| Busiest trade lanes | Seller's governorate → customer's governorate | Orders, success and average delivery time per lane |
| Most requested products | The words orders mention most | Normalised words of item descriptions, counted once per order |
| First-time and repeat customers | Do loyal customers accept their parcels more often? | Customers are mobile numbers; repeat = two or more orders in the period |

### Definitions

- **Order**: a domestic delivery booked in the period (cancelled orders excluded).
- **Delivery success**: delivered ÷ (delivered + returned) for the period's orders. Orders still on their way count in neither.
- **Return rate**: returned ÷ (delivered + returned). **Refusal rate**: returns whose reason is *customer refused* ÷ (delivered + returned).
- **Average basket**: mean cash-on-delivery amount of orders that have one (prepaid orders excluded).
- **Delivery time**: hours from booking to delivery, delivered orders only.
- **Top failure reason**: the most common reason of failed delivery attempts recorded in the period, per governorate.

### Key findings

Findings are short sentences generated from the numbers. Groups with fewer than 10 orders are never called out.

| Finding | Shown when |
|---|---|
| Leading category and its share | Always (when a category is recognised) |
| Fastest-growing governorate | ≥ 10 orders now and in the previous period, growth ≥ 10 % |
| Weakest governorate and its main failure reason | ≥ 10 finished orders, at least 3 points below the overall success rate |
| Risky basket size | A COD band returned at least 1.5× as often as average |
| Category with the highest return rate | At least 1.3× the average return rate |
| Peak ordering time | Always |
| Repeat vs first-time customers | ≥ 10 finished orders on each side |

## 2. Recognising products (`ProductCatalog`)

Sellers type free text: `فستان سهرة`, `AirPods case`, `عطور ×2`. `Contracts/Rules/ProductCatalog.cs` turns it into one of
eleven categories (fashion, shoes & bags, beauty & perfume, phones & electronics, watches & accessories, kids & toys,
home & kitchen, books, food, health, other) and into words for the "most requested" panel.

- **Arabic normalisation**: diacritics and tatweel removed; `أ إ آ ٱ → ا`, `ى ئ ی → ي`, `ة → ه`, `ؤ → و`, `گ ک → ك`;
  the article and attached `و/ب/ف/ك/ل` dropped (`والعطور → عطور`). Latin is lower-cased.
- **Phrases before words**: `ساعة ذكية` is electronics while `ساعة` alone is an accessory; `ملابس أطفال` is kids, not fashion.
- **Filler words** (quantities, units, colours, "new", "original", "gift"…) are not counted as products.
- Words are shown as written (`سهرة`) but counted in normalised form (`سهره`), so spelling variants count once.
- Descriptions are grouped in PostgreSQL first, so classification runs once per distinct description, not per order.

To improve recognition, add words or phrases to the lists in `ProductCatalog` and a case to `ProductCatalogTests`.
Kurdish (Sorani) keywords are a welcome contribution.

## 3. Open data export

`GET /api/v1/insights/open-data.csv?days=30|90|365` (signed-in staff; the *Open data (CSV)* button on the page) returns
the same period as aggregate rows that a company can publish (illustrative values):

```
period_start,period_end,section,key,name,orders,delivered,returned,success_rate_pct,return_rate_pct,avg_basket_iqd,avg_delivery_hours
2026-09-20,2026-10-19,governorate,IQ-BG,Baghdad,151,120,4,96.8,3.2,60000,22.6
2026-09-20,2026-10-19,category,Electronics,Electronics,72,55,5,91.7,8.3,56000,
2026-09-20,2026-10-19,lane,IQ-BG>IQ-AR,Baghdad > Erbil,18,15,0,100,0,,34.4
2026-09-20,2026-10-19,basket_iqd,5,200000-,15,8,3,72.7,27.3,,
2026-09-20,2026-10-19,weekday,4,Thursday,64,,,,,,
```

| Section | Key | Notes |
|---|---|---|
| `governorate` | ISO 3166-2:IQ code | Customer's governorate |
| `category` | `ProductCategory` name | See §2 |
| `lane` | `IQ-XX>IQ-YY` | Seller's governorate > customer's governorate (top 12 lanes) |
| `basket_iqd` | band 0–5 | 0 = prepaid; `name` gives the IQD range |
| `weekday` | 0 = Sunday … 6 = Saturday | Orders only (Baghdad time) |

### How it protects people

- **No personal data**: no names, phone numbers, addresses, tracking numbers, seller names or free text: only codes,
  category names, counts, rates and averages.
- **Small groups are left out**: a row describing fewer than `Insights:OpenDataMinimumOrders` orders (default **10**) is
  not written at all, so no single customer, courier or small seller can be singled out.
- **Averages are coarsened**: baskets rounded to 1,000 IQD, delivery time to 0.1 hour.
- **Per company**: the export only ever contains the signed-in user's own company (tenant).

Raise the minimum in `appsettings.json` (`"Insights": { "OpenDataMinimumOrders": 25 }`) for stricter publishing. Before
publishing, check the file against your own legal obligations; the export reduces risk but is not legal advice.

### Toward a national dataset

Because every company exports the same columns and codes, files can be combined into one picture of Iraqi e-commerce:
demand per governorate, which lanes are slow, where cash-on-delivery refusals hurt sellers. A shared repository of these
files (one folder per company and month) is a simple first step; a future version may offer an opt-in publishing endpoint.

## 4. Extending the analytics

- Server: `Modules/Shipments/.../Endpoints/InsightsEndpoints.cs`. Aggregations run in PostgreSQL (`GROUP BY` with EF Core,
  one raw SQL query for weekday × hour in `Asia/Baghdad`). Add new measures to `DemandInsights` in
  `Contracts/InsightsContracts.cs` so the browser and the server share them.
- Tests: `ProductCatalogTests`, `OpenDataTests` (unit) and `InsightsTests` (API on PostgreSQL).
- Indexes: the queries range over `(tenant_id, created_at)` on shipments and `(tenant_id, status, occurred_at)` on
  shipment events; both indexes already exist.
