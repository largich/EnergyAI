using ClosedXML.Excel;
using EnergyAi.Server.Dtos;

namespace EnergyAi.Server.Services;

public interface IReportService
{
    /// <summary>Builds an XLSX workbook with summary, series, comparison and anomaly sheets.</summary>
    Task<byte[]> BuildSummaryReportAsync(
        ConsumptionQuery query,
        IReadOnlyList<ConsumptionSeries> series,
        IReadOnlyList<ComparisonResult>? comparison,
        IReadOnlyList<AnomalyPoint> anomalies,
        CancellationToken ct = default);
}

public class ReportService : IReportService
{
    public Task<byte[]> BuildSummaryReportAsync(
        ConsumptionQuery query,
        IReadOnlyList<ConsumptionSeries> series,
        IReadOnlyList<ComparisonResult>? comparison,
        IReadOnlyList<AnomalyPoint> anomalies,
        CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();

        WriteSummarySheet(wb, query, series, anomalies);
        WriteSeriesSheet(wb, series);
        if (comparison is { Count: > 0 }) WriteComparisonSheet(wb, comparison);
        WriteAnomalySheet(wb, anomalies);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
    }

    private static void WriteSummarySheet(
        XLWorkbook wb, ConsumptionQuery query,
        IReadOnlyList<ConsumptionSeries> series,
        IReadOnlyList<AnomalyPoint> anomalies)
    {
        var ws = wb.Worksheets.Add("Summary");
        ws.Cell(1, 1).Value = "Energy Consumption Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;

        ws.Cell(3, 1).Value = "Window (UTC)";
        ws.Cell(3, 2).Value = $"{query.FromUtc:u} → {query.ToUtc:u}";
        ws.Cell(4, 1).Value = "Granularity";
        ws.Cell(4, 2).Value = query.Granularity.ToString();
        ws.Cell(5, 1).Value = "Series count";
        ws.Cell(5, 2).Value = series.Count;
        ws.Cell(6, 1).Value = "Anomalies";
        ws.Cell(6, 2).Value = anomalies.Count;

        ws.Cell(8, 1).Value = "Equipment";
        ws.Cell(8, 2).Value = "Class";
        ws.Cell(8, 3).Value = "Measurement";
        ws.Cell(8, 4).Value = "Unit";
        ws.Cell(8, 5).Value = "Total";
        ws.Cell(8, 6).Value = "Buckets";
        ws.Range(8, 1, 8, 6).Style.Font.Bold = true;

        var row = 9;
        foreach (var s in series)
        {
            ws.Cell(row, 1).Value = s.EquipmentCode;
            ws.Cell(row, 2).Value = s.ClassName;
            ws.Cell(row, 3).Value = s.MeasurementName;
            ws.Cell(row, 4).Value = s.UnitSymbol;
            ws.Cell(row, 5).Value = s.Buckets.Sum(b => b.TotalQuantity ?? 0d);
            ws.Cell(row, 6).Value = s.Buckets.Count;
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private static void WriteSeriesSheet(XLWorkbook wb, IReadOnlyList<ConsumptionSeries> series)
    {
        var ws = wb.Worksheets.Add("Series");
        string[] headers = ["Equipment", "Class", "Measurement", "Unit", "Bucket Start (UTC)", "Quantity", "Sample Count"];
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
        }
        ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;

        int row = 2;
        foreach (var s in series)
        {
            foreach (var b in s.Buckets)
            {
                ws.Cell(row, 1).Value = s.EquipmentCode;
                ws.Cell(row, 2).Value = s.ClassName;
                ws.Cell(row, 3).Value = s.MeasurementName;
                ws.Cell(row, 4).Value = s.UnitSymbol;
                ws.Cell(row, 5).Value = b.BucketStartUtc;
                ws.Cell(row, 5).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                ws.Cell(row, 6).Value = b.TotalQuantity ?? 0d;
                ws.Cell(row, 7).Value = b.SampleCount;
                row++;
            }
        }

        ws.Columns().AdjustToContents();
    }

    private static void WriteComparisonSheet(XLWorkbook wb, IReadOnlyList<ComparisonResult> comparison)
    {
        var ws = wb.Worksheets.Add("Comparison");
        string[] headers = ["Equipment", "Class", "Measurement", "Unit", "Current Total", "Previous Total", "Δ Absolute", "Δ %"];
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;

        int row = 2;
        foreach (var c in comparison)
        {
            ws.Cell(row, 1).Value = c.Current.EquipmentCode;
            ws.Cell(row, 2).Value = c.Current.ClassName;
            ws.Cell(row, 3).Value = c.Current.MeasurementName;
            ws.Cell(row, 4).Value = c.Current.UnitSymbol;
            ws.Cell(row, 5).Value = c.CurrentTotal;
            ws.Cell(row, 6).Value = c.PreviousTotal;
            ws.Cell(row, 7).Value = c.DeltaAbsolute;
            if (c.DeltaPercent.HasValue) ws.Cell(row, 8).Value = c.DeltaPercent.Value;
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private static void WriteAnomalySheet(XLWorkbook wb, IReadOnlyList<AnomalyPoint> anomalies)
    {
        var ws = wb.Worksheets.Add("Anomalies");
        string[] headers = ["Bucket (UTC)", "Equipment", "Class", "Measurement", "Unit", "Quantity", "Expected", "Deviation %", "Z-Score", "Severity", "Reason"];
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;

        int row = 2;
        foreach (var a in anomalies)
        {
            ws.Cell(row, 1).Value = a.BucketStartUtc;
            ws.Cell(row, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            ws.Cell(row, 2).Value = a.EquipmentCode;
            ws.Cell(row, 3).Value = a.ClassCode;
            ws.Cell(row, 4).Value = a.MeasurementCode;
            ws.Cell(row, 5).Value = a.UnitSymbol;
            ws.Cell(row, 6).Value = a.Quantity;
            ws.Cell(row, 7).Value = a.ExpectedQuantity;
            ws.Cell(row, 8).Value = a.DeviationPercent;
            ws.Cell(row, 9).Value = a.ZScore;
            ws.Cell(row, 10).Value = a.Severity;
            ws.Cell(row, 11).Value = a.Reason;
            row++;
        }

        ws.Columns().AdjustToContents();
    }
}
