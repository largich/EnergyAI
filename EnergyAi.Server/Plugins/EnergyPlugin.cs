using System.ComponentModel;
using System.Text.Json;
using EnergyAi.Server.Data;
using EnergyAi.Server.Dtos;
using EnergyAi.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace EnergyAi.Server.Plugins;

/// <summary>
/// Exposes energy data services as AIFunctions for the Claude agent tool loop.
/// Each method is wrapped with AIFunctionFactory so UseFunctionInvocation()
/// in the IChatClient pipeline can invoke them automatically.
/// </summary>
public class EnergyPlugin
{
    private readonly IConsumptionService    _consumption;
    private readonly IAnomalyService        _anomaly;
    private readonly EnergyDbContext        _db;
    private readonly ILogger<EnergyPlugin>  _logger;

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    public EnergyPlugin(IConsumptionService consumption, IAnomalyService anomaly, EnergyDbContext db, ILogger<EnergyPlugin> logger)
    {
        _consumption = consumption;
        _anomaly     = anomaly;
        _db          = db;
        _logger      = logger;
    }

    public AIFunction[] CreateTools() =>
    [
        AIFunctionFactory.Create(QueryConsumptionAsync,  "QueryConsumption",  "Get aggregated energy consumption time-series for a date range. Returns series grouped by equipment, class, and measurement."),
        AIFunctionFactory.Create(DetectAnomaliesAsync,   "DetectAnomalies",   "Detect anomalous consumption readings (leaks, spikes) for a date range using z-score and rolling-median analysis."),
        AIFunctionFactory.Create(ComparePeriodsAsync,    "ComparePeriods",    "Compare total consumption between two date ranges and return delta % per series."),
        AIFunctionFactory.Create(GetEquipmentAsync,      "GetEquipment",      "List all equipment codes available in the system."),
        AIFunctionFactory.Create(GetClassesAsync,        "GetClasses",        "List all energy class codes (electricity, gas, water, etc.) available in the system."),
    ];

    // -------------------------------------------------------------------------

    private async Task<string> QueryConsumptionAsync(
        [Description("Start of the period, UTC ISO-8601 (e.g. 2025-01-01T00:00:00Z)")] string from,
        [Description("End of the period, UTC ISO-8601 (e.g. 2025-01-31T23:59:59Z)")]   string to,
        [Description("Bucket size: Hour, Day, Week, or Month")]                          string granularity,
        [Description("Comma-separated equipment codes to filter (optional)")]            string? equipment = null,
        [Description("Comma-separated class codes to filter, e.g. electricity (optional)")] string? classCode = null)
    {
        _logger.LogDebug("Tool=QueryConsumption From={From} To={To} Granularity={Granularity} Equipment={Equipment} Class={Class}",
            from, to, granularity, equipment, classCode);
        var q = BuildQuery(from, to, granularity, equipment, classCode);
        var series = await _consumption.GetSeriesAsync(q);

        var slim = series.Select(s => new
        {
            s.EquipmentCode, s.ClassCode, s.MeasurementCode, s.UnitSymbol,
            Buckets = s.Buckets.Select(b => new { b.BucketStartUtc, Total = b.TotalQuantity })
        });
        return JsonSerializer.Serialize(slim, _json);
    }

    private async Task<string> DetectAnomaliesAsync(
        [Description("Start of the period, UTC ISO-8601")]                               string from,
        [Description("End of the period, UTC ISO-8601")]                                 string to,
        [Description("Bucket size: Hour, Day, Week, or Month")]                          string granularity,
        [Description("Comma-separated equipment codes to filter (optional)")]            string? equipment = null,
        [Description("Comma-separated class codes to filter (optional)")]                string? classCode = null,
        [Description("Z-score threshold for anomaly flagging (default 2.5)")]            double zThreshold = 2.5,
        [Description("Rolling-median deviation % threshold (default 30)")]               double deviationThreshold = 30.0)
    {
        _logger.LogDebug("Tool=DetectAnomalies From={From} To={To} Granularity={Granularity} ZThreshold={Z} DeviationThreshold={Dev}",
            from, to, granularity, zThreshold, deviationThreshold);
        var q = BuildQuery(from, to, granularity, equipment, classCode);
        var anomalies = await _anomaly.DetectAsync(q, zThreshold, deviationThreshold);
        return JsonSerializer.Serialize(anomalies, _json);
    }

    private async Task<string> ComparePeriodsAsync(
        [Description("Current period start, UTC ISO-8601")]  string from,
        [Description("Current period end, UTC ISO-8601")]    string to,
        [Description("Previous period start, UTC ISO-8601")] string previousFrom,
        [Description("Previous period end, UTC ISO-8601")]   string previousTo,
        [Description("Bucket size: Hour, Day, Week, or Month")] string granularity,
        [Description("Comma-separated equipment codes (optional)")] string? equipment = null,
        [Description("Comma-separated class codes (optional)")]     string? classCode = null)
    {
        _logger.LogDebug("Tool=ComparePeriods From={From} To={To} PreviousFrom={PrevFrom} PreviousTo={PrevTo} Granularity={Granularity}",
            from, to, previousFrom, previousTo, granularity);
        var q = BuildQuery(from, to, granularity, equipment, classCode);
        var results = await _consumption.ComparePeriodsAsync(
            q,
            DateTime.Parse(previousFrom).ToUniversalTime(),
            DateTime.Parse(previousTo).ToUniversalTime());

        var slim = results.Select(r => new
        {
            r.Current.EquipmentCode, r.Current.ClassCode, r.Current.MeasurementCode,
            r.Current.UnitSymbol,    r.CurrentTotal, r.PreviousTotal,
            r.DeltaAbsolute,         r.DeltaPercent
        });
        return JsonSerializer.Serialize(slim, _json);
    }

    private async Task<string> GetEquipmentAsync()
    {
        _logger.LogDebug("Tool=GetEquipment");
        var codes = await _db.CounterValues
            .AsNoTracking()
            .Select(cv => cv.EquipmentCode)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
        return JsonSerializer.Serialize(codes, _json);
    }

    private async Task<string> GetClassesAsync()
    {
        _logger.LogDebug("Tool=GetClasses");
        var classes = await _db.TransferClassCodes
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => new { c.Code, c.Name })
            .ToListAsync();
        return JsonSerializer.Serialize(classes, _json);
    }

    // -------------------------------------------------------------------------

    private static ConsumptionQuery BuildQuery(
        string from, string to, string granularity,
        string? equipment, string? classCode)
        => new(
            FromUtc:        DateTime.Parse(from).ToUniversalTime(),
            ToUtc:          DateTime.Parse(to).ToUniversalTime(),
            Granularity:    Enum.Parse<Granularity>(granularity, ignoreCase: true),
            EquipmentCodes: Split(equipment),
            ClassCodes:     Split(classCode));

    private static string[]? Split(string? v) =>
        string.IsNullOrWhiteSpace(v)
            ? null
            : v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
