# EnergyAi — Project Guide

Energy consumption monitoring app built on MS SQL timeseries sensor data. Users can explore consumption trends, compare periods, detect anomalies (leaks, high usage), and export reports.

## Tech stack

| Layer | Technology |
|---|---|
| Backend | .NET 10, ASP.NET Minimal API, EF Core 10 (SQL Server), ClosedXML |
| Frontend | React 18 + Vite, TypeScript, Recharts, date-fns |
| Orchestration | .NET Aspire (`EnergyAi.AppHost`) |

## Running

```powershell
# First time — pull frontend deps
cd frontend
npm install

# Start everything (API + Vite dev server via Aspire)
dotnet run --project EnergyAi.AppHost
```

Aspire surfaces the dashboard URL in the console. The Vite dev server proxies `/api/*` to the backend.

## Connection string

Set in `EnergyAi.Server/appsettings.Development.json`:

```json
"ConnectionStrings": {
  "EnergyDb": "Server=YOUR_SERVER;Database=YOUR_DB;User Id=…;Password=…;Encrypt=True;TrustServerCertificate=True"
}
```

Or as env var: `ConnectionStrings__EnergyDb=…`

The app is **read-only** against the existing database — no migrations, no writes to counter tables.

## Database schema

All tables are pre-existing. The app never runs migrations against them.

```
opex.counter_value          — timeseries readings (main fact table)
  id, equipment_code, utc_measurement_start_time, utc_measurement_end_time
  quantity (float), transfer_code_id, transfer_class_code_id

cdr.transfer_code           — maps to a measurement type
  id, code, name

cdr.transfer_class_code     — energy class (electricity, gas, water, …)
  id, code, name

utl.measurement             — measurement descriptor
  id, code, name, unit_of_measure_id

utl.unit_of_measure         — kWh, m³, etc.
  id, code, name, symbol
```

Key join: `transfer_code.code = measurement.code` — this is a **string join, not an FK**. EF models it via `HasPrincipalKey`.

## Project structure

```
EnergyAi.sln
EnergyAi.AppHost/          .NET Aspire orchestrator
EnergyAi.Server/           ASP.NET backend
  Data/
    EnergyDbContext.cs
    Entities/
      CounterValue.cs
      TransferCode.cs
      TransferClassCode.cs
      Measurement.cs
      UnitOfMeasure.cs
  Dtos/
    ConsumptionDtos.cs      ConsumptionQuery, ConsumptionSeries, ConsumptionBucket,
                            ComparisonResult, AnomalyPoint, Granularity enum
    MetadataDtos.cs         ClassCodeDto, EquipmentDto, MeasurementDto
  Services/
    ConsumptionService.cs   Aggregation into Hour/Day/Week/Month buckets
    AnomalyService.cs       Z-score + rolling-median outlier detection
    ReportService.cs        ClosedXML Excel export
  Endpoints/
    EnergyEndpoints.cs      Minimal-API routes under /api/energy/*
  Program.cs
frontend/                   React + Vite SPA
  src/
    lib/
      types.ts              TypeScript mirrors of C# DTOs
      api.ts                Typed fetch wrapper for /api/energy/*
    components/
      FilterBar.tsx         Date range, granularity, class, equipment selectors
      ConsumptionChart.tsx  Multi-series line chart (Recharts)
      ComparisonTable.tsx   Current vs previous period totals + Δ%
      AnomalyList.tsx       Sorted list of flagged buckets
    App.tsx                 Dashboard layout, KPI cards, tab switching, xlsx export
    App.css                 Dashboard styles
```

## API endpoints

All routes are under `/api/energy`.

| Method | Route | Key query params | Returns |
|---|---|---|---|
| GET | `/classes` | — | `ClassCodeDto[]` |
| GET | `/equipment` | — | `EquipmentDto[]` |
| GET | `/measurements` | — | `MeasurementDto[]` |
| GET | `/consumption` | `from`, `to`, `granularity`, `equipment` (CSV), `classCode` (CSV) | `ConsumptionSeries[]` |
| GET | `/consumption/compare` | + `previousFrom`, `previousTo` | `ComparisonResult[]` |
| GET | `/anomalies` | + `zThreshold` (default 2.5), `deviationThreshold` (default 30.0) | `AnomalyPoint[]` |
| GET | `/reports/summary.xlsx` | same + optional previous period | xlsx file |

All timestamps are UTC ISO-8601.

## Key design decisions

**In-memory bucketing** — `ConsumptionService` materialises the filtered rowset then groups in .NET rather than pushing `DATETRUNC` into SQL. This works well for typical dashboard date windows; for very large ranges it's the main performance bottleneck (see backlog).

**Anomaly detection** — `AnomalyService` flags buckets where either:
1. Z-score `|z| ≥ 2.5` against the series mean/std
2. Rolling-median deviation `≥ 30%` over a 7-bucket window

Severity escalates to `high` at ~50% beyond either threshold. Series with fewer than 5 buckets are skipped. Detection is purely statistical — it does not account for seasonality.

**Read-only EF context** — `AsNoTracking()` everywhere; no SaveChanges called.

**Aspire publish** — `AppHost` uses `PublishWithContainerFiles` to embed the Vite build into the server's `wwwroot` for single-container production deployment.

## Backlog / known gaps

- **Leak detection** — sustained non-zero consumption during expected-idle windows (nights, weekends) is not yet modelled. Needs a `LeakDetectionService` separate from statistical anomaly detection.
- **SQL-side bucketing** — move `DATEPART`/`DATETRUNC` grouping into the SQL query for large date ranges.
- **Cost layer** — tariff tables (price per kWh/m³, time-of-use) so charts can show € alongside raw units.
- **Alerting** — background `IHostedService` + SignalR hub or webhook for push notifications when thresholds are breached.
- **Heatmap view** — hour × day-of-week grid, great for spotting shift/schedule patterns.
- **Auth / multi-tenancy** — ASP.NET Identity + JWT + row-level equipment filtering if multiple sites share one DB.
- **`weatherforecast` stub** — leftover template endpoint in `Program.cs`; safe to remove.
