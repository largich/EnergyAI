using System.Diagnostics;
using EnergyAi.Server.Data;
using EnergyAi.Server.Data.Entities;
using EnergyAi.Server.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnergyAi.Server.Services;

public interface IConsumptionService
{
    Task<IReadOnlyList<ConsumptionSeries>> GetSeriesAsync(ConsumptionQuery q, CancellationToken ct = default);

    Task<IReadOnlyList<ComparisonResult>> ComparePeriodsAsync(
        ConsumptionQuery current,
        DateTime previousFromUtc,
        DateTime previousToUtc,
        CancellationToken ct = default);
}

/// <summary>
/// Aggregates counter_value rows into hourly/daily/weekly/monthly buckets, grouped
/// by (equipment, class, measurement). Bucketing is pushed to SQL via DATEPART grouping
/// on integer date components; DateTime reconstruction happens in-memory after
/// materialisation to avoid DATETIMEFROMPARTS translation concerns.
/// </summary>
public class ConsumptionService : IConsumptionService
{
    // 1900-01-01 is a Monday — used as epoch for ISO week-offset arithmetic.
    // DATEDIFF(day, epoch, ts) / 7 gives a stable integer week number whose
    // value zero corresponds to the week starting 1900-01-01.
    private static readonly DateTime s_weekEpoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan SlowQueryThreshold = TimeSpan.FromSeconds(2);

    private readonly EnergyDbContext                _db;
    private readonly ILogger<ConsumptionService>    _logger;

    public ConsumptionService(EnergyDbContext db, ILogger<ConsumptionService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public async Task<IReadOnlyList<ConsumptionSeries>> GetSeriesAsync(
        ConsumptionQuery q, CancellationToken ct = default)
    {
        var sw      = Stopwatch.StartNew();
        var flat    = BuildFlatQuery(q);
        var buckets = await GroupBySqlAsync(flat, q.Granularity, ct);
        sw.Stop();

        if (sw.Elapsed > SlowQueryThreshold)
            _logger.LogWarning("Slow consumption query. Granularity={Granularity} From={From} To={To} Elapsed={ElapsedMs}ms",
                q.Granularity, q.FromUtc, q.ToUtc, sw.ElapsedMilliseconds);

        return BuildSeries(buckets, q.Granularity);
    }

    public async Task<IReadOnlyList<ComparisonResult>> ComparePeriodsAsync(
        ConsumptionQuery current,
        DateTime previousFromUtc,
        DateTime previousToUtc,
        CancellationToken ct = default)
    {
        var currentSeries  = await GetSeriesAsync(current, ct);
        var previousSeries = await GetSeriesAsync(
            current with { FromUtc = previousFromUtc, ToUtc = previousToUtc }, ct);

        var prevByKey = previousSeries.ToDictionary(s => (s.EquipmentCode, s.ClassCode, s.MeasurementCode));

        var results = new List<ComparisonResult>(currentSeries.Count);
        foreach (var cur in currentSeries)
        {
            prevByKey.TryGetValue((cur.EquipmentCode, cur.ClassCode, cur.MeasurementCode), out var prev);

            var curTotal  = cur.Buckets.Sum(b => b.TotalQuantity ?? 0d);
            var prevTotal = prev?.Buckets.Sum(b => b.TotalQuantity ?? 0d) ?? 0d;
            var delta     = curTotal - prevTotal;

            double? pct = (prev is null || prevTotal == 0d)
                ? null
                : Math.Round(delta / prevTotal * 100d, 2);

            results.Add(new ComparisonResult(
                Current: cur,
                Previous: prev ?? new ConsumptionSeries(
                    cur.EquipmentCode, cur.ClassCode, cur.ClassName,
                    cur.MeasurementCode, cur.MeasurementName, cur.UnitSymbol,
                    cur.Granularity, Array.Empty<ConsumptionBucket>()),
                CurrentTotal:  curTotal,
                PreviousTotal: prevTotal,
                DeltaAbsolute: delta,
                DeltaPercent:  pct
            ));
        }

        return results;
    }

    // -------------------------------------------------------------------------
    // Query building
    // -------------------------------------------------------------------------

    // Projects CounterValue rows to a flat shape that EF can translate cleanly.
    // Navigation access inside Select is automatically translated to JOINs —
    // no Include() calls needed.
    private IQueryable<Row> BuildFlatQuery(ConsumptionQuery q)
    {
        IQueryable<CounterValue> query = _db.CounterValues
            .AsNoTracking()
            .Where(cv => cv.UtcMeasurementStartTime >= q.FromUtc
                      && cv.UtcMeasurementStartTime  < q.ToUtc);

        if (q.EquipmentCodes is { Length: > 0 })
            query = query.Where(cv => q.EquipmentCodes.Contains(cv.EquipmentCode));

        if (q.ClassCodes is { Length: > 0 })
            query = query.Where(cv => q.ClassCodes.Contains(cv.TransferClassCode.Code));

        return query.Select(cv => new Row
        {
            EquipmentCode   = cv.EquipmentCode,
            ClassCode       = cv.TransferClassCode.Code,
            ClassName       = cv.TransferClassCode.Name,
            TransferCode    = cv.TransferCode.Code,
            MeasurementCode = cv.TransferCode.Measurement != null ? cv.TransferCode.Measurement.Code  : null,
            MeasurementName = cv.TransferCode.Measurement != null ? cv.TransferCode.Measurement.Name  : null,
            UnitSymbol      = cv.TransferCode.Measurement != null ? cv.TransferCode.Measurement.UnitOfMeasure.Symbol : null,
            UtcStart        = cv.UtcMeasurementStartTime,
            Quantity        = cv.Quantity
        });
    }

    // -------------------------------------------------------------------------
    // SQL-side bucketing
    // -------------------------------------------------------------------------

    private static Task<List<Bucket>> GroupBySqlAsync(
        IQueryable<Row> rows, Granularity g, CancellationToken ct)
        => g switch
        {
            Granularity.Hour  => GroupByHourAsync(rows, ct),
            Granularity.Day   => GroupByDayAsync(rows, ct),
            Granularity.Week  => GroupByWeekAsync(rows, ct),
            Granularity.Month => GroupByMonthAsync(rows, ct),
            _                 => GroupByDayAsync(rows, ct)
        };

    // GROUP BY ..., DATEPART(year,ts), DATEPART(month,ts), DATEPART(day,ts), DATEPART(hour,ts)
    private static Task<List<Bucket>> GroupByHourAsync(IQueryable<Row> rows, CancellationToken ct)
        => rows
            .GroupBy(r => new
            {
                r.EquipmentCode, r.ClassCode, r.ClassName,
                r.TransferCode,  r.MeasurementCode, r.MeasurementName, r.UnitSymbol,
                r.UtcStart.Year, r.UtcStart.Month, r.UtcStart.Day, r.UtcStart.Hour
            })
            .Select(g => new Bucket
            {
                EquipmentCode   = g.Key.EquipmentCode,
                ClassCode       = g.Key.ClassCode,
                ClassName       = g.Key.ClassName,
                TransferCode    = g.Key.TransferCode,
                MeasurementCode = g.Key.MeasurementCode,
                MeasurementName = g.Key.MeasurementName,
                UnitSymbol      = g.Key.UnitSymbol,
                Year  = g.Key.Year,
                Month = g.Key.Month,
                Day   = g.Key.Day,
                Hour  = g.Key.Hour,
                TotalQuantity = g.Sum(r => r.Quantity),
                SampleCount   = g.Count()
            })
            .ToListAsync(ct);

    // GROUP BY ..., DATEPART(year,ts), DATEPART(month,ts), DATEPART(day,ts)
    private static Task<List<Bucket>> GroupByDayAsync(IQueryable<Row> rows, CancellationToken ct)
        => rows
            .GroupBy(r => new
            {
                r.EquipmentCode, r.ClassCode, r.ClassName,
                r.TransferCode,  r.MeasurementCode, r.MeasurementName, r.UnitSymbol,
                r.UtcStart.Year, r.UtcStart.Month, r.UtcStart.Day
            })
            .Select(g => new Bucket
            {
                EquipmentCode   = g.Key.EquipmentCode,
                ClassCode       = g.Key.ClassCode,
                ClassName       = g.Key.ClassName,
                TransferCode    = g.Key.TransferCode,
                MeasurementCode = g.Key.MeasurementCode,
                MeasurementName = g.Key.MeasurementName,
                UnitSymbol      = g.Key.UnitSymbol,
                Year  = g.Key.Year,
                Month = g.Key.Month,
                Day   = g.Key.Day,
                TotalQuantity = g.Sum(r => r.Quantity),
                SampleCount   = g.Count()
            })
            .ToListAsync(ct);

    // GROUP BY ..., DATEPART(year,ts), DATEPART(month,ts)
    private static Task<List<Bucket>> GroupByMonthAsync(IQueryable<Row> rows, CancellationToken ct)
        => rows
            .GroupBy(r => new
            {
                r.EquipmentCode, r.ClassCode, r.ClassName,
                r.TransferCode,  r.MeasurementCode, r.MeasurementName, r.UnitSymbol,
                r.UtcStart.Year, r.UtcStart.Month
            })
            .Select(g => new Bucket
            {
                EquipmentCode   = g.Key.EquipmentCode,
                ClassCode       = g.Key.ClassCode,
                ClassName       = g.Key.ClassName,
                TransferCode    = g.Key.TransferCode,
                MeasurementCode = g.Key.MeasurementCode,
                MeasurementName = g.Key.MeasurementName,
                UnitSymbol      = g.Key.UnitSymbol,
                Year  = g.Key.Year,
                Month = g.Key.Month,
                TotalQuantity = g.Sum(r => r.Quantity),
                SampleCount   = g.Count()
            })
            .ToListAsync(ct);

    // GROUP BY ..., DATEDIFF(day, '1900-01-01', ts) / 7
    // The integer division gives a stable ISO week number (epoch = Monday 1900-01-01).
    // BucketStartUtc is reconstructed in-memory after materialisation.
    private static async Task<List<Bucket>> GroupByWeekAsync(IQueryable<Row> rows, CancellationToken ct)
    {
        var raw = await rows
            .GroupBy(r => new
            {
                r.EquipmentCode, r.ClassCode, r.ClassName,
                r.TransferCode,  r.MeasurementCode, r.MeasurementName, r.UnitSymbol,
                WeekOffset = EF.Functions.DateDiffDay(s_weekEpoch, r.UtcStart) / 7
            })
            .Select(g => new
            {
                g.Key.EquipmentCode, g.Key.ClassCode, g.Key.ClassName,
                g.Key.TransferCode,  g.Key.MeasurementCode, g.Key.MeasurementName, g.Key.UnitSymbol,
                g.Key.WeekOffset,
                TotalQuantity = g.Sum(r => r.Quantity),
                SampleCount   = g.Count()
            })
            .ToListAsync(ct);

        return raw.Select(r => new Bucket
        {
            EquipmentCode   = r.EquipmentCode,
            ClassCode       = r.ClassCode,
            ClassName       = r.ClassName,
            TransferCode    = r.TransferCode,
            MeasurementCode = r.MeasurementCode,
            MeasurementName = r.MeasurementName,
            UnitSymbol      = r.UnitSymbol,
            WeekOffset      = r.WeekOffset,
            TotalQuantity   = r.TotalQuantity,
            SampleCount     = r.SampleCount
        }).ToList();
    }

    // -------------------------------------------------------------------------
    // In-memory assembly into ConsumptionSeries
    // -------------------------------------------------------------------------

    private static IReadOnlyList<ConsumptionSeries> BuildSeries(List<Bucket> buckets, Granularity g)
    {
        return buckets
            .Select(b => new
            {
                b.EquipmentCode,
                b.ClassCode,
                ClassName       = b.ClassName       ?? b.ClassCode,
                MeasurementCode = b.MeasurementCode ?? b.TransferCode,
                MeasurementName = b.MeasurementName ?? b.TransferCode,
                UnitSymbol      = b.UnitSymbol      ?? "",
                BucketStartUtc  = ReconstructBucketStart(b, g),
                b.TotalQuantity,
                b.SampleCount
            })
            .GroupBy(r => new { r.EquipmentCode, r.ClassCode, r.ClassName, r.MeasurementCode, r.MeasurementName, r.UnitSymbol })
            .Select(grp => new ConsumptionSeries(
                EquipmentCode:   grp.Key.EquipmentCode,
                ClassCode:       grp.Key.ClassCode,
                ClassName:       grp.Key.ClassName,
                MeasurementCode: grp.Key.MeasurementCode,
                MeasurementName: grp.Key.MeasurementName,
                UnitSymbol:      grp.Key.UnitSymbol,
                Granularity:     g,
                Buckets:         grp
                    .OrderBy(b => b.BucketStartUtc)
                    .Select(b => new ConsumptionBucket(b.BucketStartUtc, b.TotalQuantity, b.SampleCount))
                    .ToList()
            ))
            .OrderBy(s => s.EquipmentCode)
            .ThenBy(s => s.ClassCode)
            .ToList();
    }

    private static DateTime ReconstructBucketStart(Bucket b, Granularity g)
        => g switch
        {
            Granularity.Hour  => new DateTime(b.Year, b.Month, b.Day, b.Hour, 0, 0, DateTimeKind.Utc),
            Granularity.Day   => new DateTime(b.Year, b.Month, b.Day,      0, 0, 0, DateTimeKind.Utc),
            Granularity.Month => new DateTime(b.Year, b.Month,          1, 0, 0, 0, DateTimeKind.Utc),
            Granularity.Week  => DateTime.SpecifyKind(s_weekEpoch.AddDays(b.WeekOffset * 7), DateTimeKind.Utc),
            _                 => new DateTime(b.Year, b.Month, b.Day,      0, 0, 0, DateTimeKind.Utc)
        };

    // -------------------------------------------------------------------------
    // Private types
    // -------------------------------------------------------------------------

    // Flat projection from CounterValue — maps cleanly to a SQL SELECT with JOINs.
    private sealed class Row
    {
        public string  EquipmentCode   { get; set; } = default!;
        public string  ClassCode       { get; set; } = default!;
        public string? ClassName       { get; set; }
        public string  TransferCode    { get; set; } = default!;
        public string? MeasurementCode { get; set; }
        public string? MeasurementName { get; set; }
        public string? UnitSymbol      { get; set; }
        public DateTime UtcStart       { get; set; }
        public double   Quantity       { get; set; }
    }

    // Result of a SQL GROUP BY — integer date components for Hour/Day/Month,
    // WeekOffset for Week; aggregates are SQL SUM/COUNT.
    private sealed class Bucket
    {
        public string  EquipmentCode   { get; set; } = default!;
        public string  ClassCode       { get; set; } = default!;
        public string? ClassName       { get; set; }
        public string  TransferCode    { get; set; } = default!;
        public string? MeasurementCode { get; set; }
        public string? MeasurementName { get; set; }
        public string? UnitSymbol      { get; set; }
        // Hour/Day/Month granularities
        public int Year       { get; set; }
        public int Month      { get; set; }
        public int Day        { get; set; }
        public int Hour       { get; set; }
        // Week granularity: days-from-epoch / 7
        public int WeekOffset { get; set; }
        // Aggregates
        public double TotalQuantity { get; set; }
        public int    SampleCount   { get; set; }
    }
}
