using System.Diagnostics;
using EnergyAi.Server.Data;
using EnergyAi.Server.Data.Entities;
using EnergyAi.Server.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnergyAi.Server.Services;

public interface IHeatmapService
{
    Task<IReadOnlyList<HeatmapSeries>> GetHeatmapAsync(
        DateTime fromUtc,
        DateTime toUtc,
        string[]? equipmentCodes  = null,
        string[]? classCodes      = null,
        string[]? transferCodes   = null,
        CancellationToken ct      = default);
}

/// <summary>
/// Builds hour-of-day × day-of-week heatmaps.
/// Day-of-week is derived via DATEDIFF(day, sundayEpoch, ts) % 7 so that
/// 0=Sunday … 6=Saturday — the same integer-arithmetic pattern used in
/// ConsumptionService for week-offset bucketing.
/// </summary>
public class HeatmapService : IHeatmapService
{
    // 1900-01-07 is a Sunday (1900-01-01 is Monday, +6 days = Sunday).
    // DATEDIFF(day, epoch, ts) % 7 → 0=Sun, 1=Mon, … 6=Sat — matches .NET DayOfWeek.
    private static readonly DateTime s_sundayEpoch = new(1900, 1, 7, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan SlowQueryThreshold = TimeSpan.FromSeconds(2);

    private readonly EnergyDbContext            _db;
    private readonly ILogger<HeatmapService>    _logger;

    public HeatmapService(EnergyDbContext db, ILogger<HeatmapService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<HeatmapSeries>> GetHeatmapAsync(
        DateTime fromUtc,
        DateTime toUtc,
        string[]? equipmentCodes  = null,
        string[]? classCodes      = null,
        string[]? transferCodes   = null,
        CancellationToken ct      = default)
    {
        var sw   = Stopwatch.StartNew();
        var flat = BuildFlatQuery(fromUtc, toUtc, equipmentCodes, classCodes, transferCodes);

        var grouped = await flat
            .GroupBy(r => new
            {
                r.EquipmentCode,
                r.ClassCode, r.ClassName,
                r.TransferCode, r.MeasurementCode, r.MeasurementName, r.UnitSymbol,
                r.DayOfWeek, r.Hour
            })
            .Select(g => new
            {
                g.Key.EquipmentCode,
                g.Key.ClassCode,    g.Key.ClassName,
                g.Key.TransferCode, g.Key.MeasurementCode, g.Key.MeasurementName, g.Key.UnitSymbol,
                g.Key.DayOfWeek,    g.Key.Hour,
                AvgQuantity = g.Average(r => r.Quantity),
                SampleCount = g.Count()
            })
            .ToListAsync(ct);

        sw.Stop();
        if (sw.Elapsed > SlowQueryThreshold)
            _logger.LogWarning("Slow heatmap query. From={From} To={To} Elapsed={ElapsedMs}ms",
                fromUtc, toUtc, sw.ElapsedMilliseconds);

        return grouped
            .GroupBy(r => new
            {
                r.EquipmentCode,
                r.ClassCode,    r.ClassName,
                r.TransferCode, r.MeasurementCode, r.MeasurementName, r.UnitSymbol
            })
            .Select(grp => new HeatmapSeries(
                EquipmentCode:   grp.Key.EquipmentCode,
                ClassCode:       grp.Key.ClassCode,
                ClassName:       grp.Key.ClassName       ?? grp.Key.ClassCode,
                MeasurementCode: grp.Key.MeasurementCode ?? grp.Key.TransferCode,
                MeasurementName: grp.Key.MeasurementName ?? grp.Key.TransferCode,
                UnitSymbol:      grp.Key.UnitSymbol      ?? "",
                Cells: grp
                    .Select(r => new HeatmapCell(r.DayOfWeek, r.Hour, Math.Round(r.AvgQuantity, 4), r.SampleCount))
                    .OrderBy(c => c.DayOfWeek).ThenBy(c => c.Hour)
                    .ToList()
            ))
            .OrderBy(s => s.EquipmentCode)
            .ThenBy(s => s.ClassCode)
            .ToList();
    }

    // -------------------------------------------------------------------------

    private IQueryable<Row> BuildFlatQuery(
        DateTime fromUtc, DateTime toUtc,
        string[]? equipmentCodes, string[]? classCodes, string[]? transferCodes)
    {
        IQueryable<CounterValue> query = _db.CounterValues
            .AsNoTracking()
            .Where(cv => cv.UtcMeasurementStartTime >= fromUtc
                      && cv.UtcMeasurementStartTime  < toUtc);

        if (equipmentCodes is { Length: > 0 })
            query = query.Where(cv => equipmentCodes.Contains(cv.EquipmentCode));

        if (classCodes is { Length: > 0 })
            query = query.Where(cv => classCodes.Contains(cv.TransferClassCode.Code));

        if (transferCodes is { Length: > 0 })
            query = query.Where(cv => transferCodes.Contains(cv.TransferCode.Code));

        return query.Select(cv => new Row
        {
            EquipmentCode   = cv.EquipmentCode,
            ClassCode       = cv.TransferClassCode.Code,
            ClassName       = cv.TransferClassCode.Name,
            TransferCode    = cv.TransferCode.Code,
            MeasurementCode = cv.TransferCode.Measurement != null ? cv.TransferCode.Measurement.Code   : null,
            MeasurementName = cv.TransferCode.Measurement != null ? cv.TransferCode.Measurement.Name   : null,
            UnitSymbol      = cv.TransferCode.Measurement != null ? cv.TransferCode.Measurement.UnitOfMeasure.Symbol : null,
            // DATEDIFF(day, '1900-01-07', ts) % 7  →  0=Sun … 6=Sat
            DayOfWeek       = EF.Functions.DateDiffDay(s_sundayEpoch, cv.UtcMeasurementStartTime) % 7,
            Hour            = cv.UtcMeasurementStartTime.Hour,
            Quantity        = cv.Quantity
        });
    }

    // -------------------------------------------------------------------------

    private sealed class Row
    {
        public string  EquipmentCode   { get; set; } = default!;
        public string  ClassCode       { get; set; } = default!;
        public string? ClassName       { get; set; }
        public string  TransferCode    { get; set; } = default!;
        public string? MeasurementCode { get; set; }
        public string? MeasurementName { get; set; }
        public string? UnitSymbol      { get; set; }
        public int     DayOfWeek       { get; set; }
        public int     Hour            { get; set; }
        public double  Quantity        { get; set; }
    }
}
