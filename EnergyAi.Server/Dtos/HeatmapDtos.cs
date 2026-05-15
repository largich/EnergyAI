namespace EnergyAi.Server.Dtos;

/// <summary>One cell in the hour-of-day × day-of-week heatmap.</summary>
/// <param name="DayOfWeek">0 = Sunday … 6 = Saturday (.NET DayOfWeek convention).</param>
/// <param name="Hour">0 – 23.</param>
public record HeatmapCell(
    int    DayOfWeek,
    int    Hour,
    double AvgQuantity,
    int    SampleCount
);

public record HeatmapSeries(
    string EquipmentCode,
    string ClassCode,
    string ClassName,
    string MeasurementCode,
    string MeasurementName,
    string UnitSymbol,
    IReadOnlyList<HeatmapCell> Cells
);
