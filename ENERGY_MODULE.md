# EnergyAI — Energy management module

A dashboard for exploring `opex.counter_value` time-series data: consumption by
hour/day/week/month, period-over-period comparison, anomaly detection, and an
Excel summary export.

## What was added

### Backend (`EnergyAi.Server`)

```
Data/
  EnergyDbContext.cs            EF Core context, read-only against your existing schema
  Entities/
    CounterValue.cs             opex.counter_value
    TransferCode.cs             cdr.transfer_code (join on code → utl.measurement)
    TransferClassCode.cs        cdr.transfer_class_code (gas/electricity/…)
    Measurement.cs              utl.measurement
    UnitOfMeasure.cs            utl.unit_of_measure
Dtos/
  ConsumptionDtos.cs            Series, bucket, comparison, anomaly DTOs + Granularity enum
  MetadataDtos.cs               Dropdown data shapes
Services/
  ConsumptionService.cs         Aggregation into hourly/daily/weekly/monthly buckets
  AnomalyService.cs             Z-score + rolling-median outlier detection
  ReportService.cs              ClosedXML workbook builder (Summary/Series/Comparison/Anomalies)
Endpoints/
  EnergyEndpoints.cs            Minimal-API routes under /api/energy/*
Program.cs                      DI registration + MapEnergyEndpoints()
EnergyAi.Server.csproj          +EF Core SqlServer, +ClosedXML
appsettings.Development.json    +ConnectionStrings:EnergyDb
```

### Frontend (`frontend`)

```
src/
  lib/
    types.ts                    Mirror of C# DTOs
    api.ts                      Typed fetch wrapper for /api/energy/*
  components/
    FilterBar.tsx               Date range, granularity, class, equipment selectors
    ConsumptionChart.tsx        Multi-series line chart (recharts)
    ComparisonTable.tsx         Current vs previous period totals and Δ%
    AnomalyList.tsx             Sorted list of flagged buckets
  App.tsx                       Dashboard layout, KPIs, tabs, xlsx export button
  App.css                       +dashboard styles appended
package.json                    +recharts, +date-fns
```

## Configuration

Set the connection string in `EnergyAi.Server/appsettings.Development.json`
(already scaffolded to LocalDB):

```json
"ConnectionStrings": {
  "EnergyDb": "Server=YOUR_SERVER;Database=YOUR_DB;User Id=…;Password=…;Encrypt=True;TrustServerCertificate=True"
}
```

Or via env var: `ConnectionStrings__EnergyDb=…`

The app reads from the existing schemas (`opex`, `cdr`, `utl`) — no migrations
are generated and the app never writes to the counter tables.

## Running

From the solution root, as usual:

```
dotnet run --project EnergyAi.AppHost
```

Aspire orchestrates the API and the Vite dev server. Open the dashboard URL
surfaced in the Aspire console.

First run in the frontend folder (to pull recharts and date-fns):

```
cd frontend
npm install
```

## API

All endpoints are under `/api/energy`.

| Method | Route | Query | Returns |
|---|---|---|---|
| GET | `/classes` | — | list of transfer class codes |
| GET | `/equipment` | — | distinct equipment codes from counter_value |
| GET | `/measurements` | — | measurement + unit |
| GET | `/consumption` | `from`, `to`, `granularity` (`Hour`\|`Day`\|`Week`\|`Month`), `equipment` (CSV), `classCode` (CSV) | `ConsumptionSeries[]` |
| GET | `/consumption/compare` | same + `previousFrom`, `previousTo` | `ComparisonResult[]` |
| GET | `/anomalies` | same + optional `zThreshold` (default 2.5), `deviationThreshold` (default 30.0) | `AnomalyPoint[]` |
| GET | `/reports/summary.xlsx` | same + optional `previousFrom`, `previousTo` | xlsx file download |

Dates are UTC ISO-8601.

## Anomaly detection logic

For each `(equipment, class, measurement)` series, buckets are flagged when
**either** of these rules fire:

1. **Z-score rule** — `|z| ≥ 2.5` (configurable) against the series mean / std.
2. **Rolling-median rule** — the point deviates by `≥ 30%` (configurable) from
   a rolling median over a window of 7 buckets.

Severity is escalated to `high` when a point is ~50% beyond either threshold.
Series with fewer than 5 buckets are skipped to avoid noisy statistics on tiny
datasets.

## Notes & next steps

- The original SQL joined `utl.measurement` on `m.code = tc.code` (a string
  join, not an FK). The EF model replicates that via a principal-key
  relationship on `TransferCode.Code → Measurement.Code`.
- Aggregation is done in memory after the filtered rowset is materialised. For
  very long windows this can become the bottleneck — moving the bucketing to
  SQL (via `DATEPART`/`DATETRUNC`) is a straightforward optimisation later.
- Anomaly detection is deliberately simple. If seasonality matters (e.g. weekly
  cycle for a hospital HVAC), swap in an STL decomposition or Prophet-style
  model on the same series shape.
- The Excel report is generated on demand. For large ranges you may want to
  stream to the response instead of building an in-memory byte array.
