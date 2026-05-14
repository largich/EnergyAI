using EnergyAi.Server.Data;
using EnergyAi.Server.Dtos;
using EnergyAi.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace EnergyAi.Server.Endpoints;

public static class EnergyEndpoints
{
    public static IEndpointRouteBuilder MapEnergyEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/energy").WithTags("Energy");

        // ---- Metadata ----

        group.MapGet("/classes", async (EnergyDbContext db, CancellationToken ct) =>
        {
            var classes = await db.TransferClassCodes
                .AsNoTracking()
                .OrderBy(c => c.Code)
                .Select(c => new ClassCodeDto(c.Id, c.Code, c.Name))
                .ToListAsync(ct);
            return Results.Ok(classes);
        }).WithName("GetClassCodes");

        group.MapGet("/equipment", async (EnergyDbContext db, CancellationToken ct) =>
        {
            var codes = await db.CounterValues
                .AsNoTracking()
                .Select(cv => cv.EquipmentCode)
                .Distinct()
                .OrderBy(c => c)
                .Select(c => new EquipmentDto(c))
                .ToListAsync(ct);
            return Results.Ok(codes);
        }).WithName("GetEquipmentCodes");

        group.MapGet("/measurements", async (EnergyDbContext db, CancellationToken ct) =>
        {
            var measurements = await db.Measurements
                .AsNoTracking()
                .Include(m => m.UnitOfMeasure)
                .OrderBy(m => m.Code)
                .Select(m => new MeasurementDto(m.Code, m.Name, m.UnitOfMeasure.Symbol))
                .ToListAsync(ct);
            return Results.Ok(measurements);
        }).WithName("GetMeasurements");

        // ---- Time series ----

        group.MapGet("/consumption", async (
            DateTime from, DateTime to, Granularity granularity,
            string? equipment, string? classCode,
            IConsumptionService svc, CancellationToken ct) =>
        {
            var q = new ConsumptionQuery(
                FromUtc: DateTime.SpecifyKind(from, DateTimeKind.Utc),
                ToUtc: DateTime.SpecifyKind(to, DateTimeKind.Utc),
                Granularity: granularity,
                EquipmentCodes: SplitCsv(equipment),
                ClassCodes: SplitCsv(classCode));
            var data = await svc.GetSeriesAsync(q, ct);
            return Results.Ok(data);
        }).WithName("GetConsumption");

        group.MapGet("/consumption/compare", async (
            DateTime from, DateTime to,
            DateTime previousFrom, DateTime previousTo,
            Granularity granularity,
            string? equipment, string? classCode,
            IConsumptionService svc, CancellationToken ct) =>
        {
            var q = new ConsumptionQuery(
                FromUtc: DateTime.SpecifyKind(from, DateTimeKind.Utc),
                ToUtc: DateTime.SpecifyKind(to, DateTimeKind.Utc),
                Granularity: granularity,
                EquipmentCodes: SplitCsv(equipment),
                ClassCodes: SplitCsv(classCode));
            var data = await svc.ComparePeriodsAsync(q,
                DateTime.SpecifyKind(previousFrom, DateTimeKind.Utc),
                DateTime.SpecifyKind(previousTo, DateTimeKind.Utc), ct);
            return Results.Ok(data);
        }).WithName("CompareConsumption");

        // ---- Anomalies ----

        group.MapGet("/anomalies", async (
            DateTime from, DateTime to, Granularity granularity,
            string? equipment, string? classCode,
            double? zThreshold, double? deviationThreshold,
            IAnomalyService svc, CancellationToken ct) =>
        {
            var q = new ConsumptionQuery(
                FromUtc: DateTime.SpecifyKind(from, DateTimeKind.Utc),
                ToUtc: DateTime.SpecifyKind(to, DateTimeKind.Utc),
                Granularity: granularity,
                EquipmentCodes: SplitCsv(equipment),
                ClassCodes: SplitCsv(classCode));
            var anomalies = await svc.DetectAsync(q,
                zThreshold ?? 2.5,
                deviationThreshold ?? 30.0, ct);
            return Results.Ok(anomalies);
        }).WithName("GetAnomalies");

        // ---- Excel report ----

        group.MapGet("/reports/summary.xlsx", async (
            DateTime from, DateTime to, Granularity granularity,
            string? equipment, string? classCode,
            DateTime? previousFrom, DateTime? previousTo,
            IConsumptionService consumption, IAnomalyService anomalies, IReportService reports,
            CancellationToken ct) =>
        {
            var q = new ConsumptionQuery(
                FromUtc: DateTime.SpecifyKind(from, DateTimeKind.Utc),
                ToUtc: DateTime.SpecifyKind(to, DateTimeKind.Utc),
                Granularity: granularity,
                EquipmentCodes: SplitCsv(equipment),
                ClassCodes: SplitCsv(classCode));

            var series = await consumption.GetSeriesAsync(q, ct);
            var anoms = await anomalies.DetectAsync(q, ct: ct);

            IReadOnlyList<ComparisonResult>? cmp = null;
            if (previousFrom.HasValue && previousTo.HasValue)
            {
                cmp = await consumption.ComparePeriodsAsync(q,
                    DateTime.SpecifyKind(previousFrom.Value, DateTimeKind.Utc),
                    DateTime.SpecifyKind(previousTo.Value, DateTimeKind.Utc), ct);
            }

            var bytes = await reports.BuildSummaryReportAsync(q, series, cmp, anoms, ct);
            var fileName = $"energy-report-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx";
            return Results.File(bytes,
                contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileDownloadName: fileName);
        }).WithName("DownloadSummaryReport");

        return api;
    }

    private static string[]? SplitCsv(string? v) =>
        string.IsNullOrWhiteSpace(v)
            ? null
            : v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
