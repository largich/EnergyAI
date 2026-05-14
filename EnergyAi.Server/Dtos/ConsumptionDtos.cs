namespace EnergyAi.Server.Dtos;

public enum Granularity
{
    Hour,
    Day,
    Week,
    Month
}

/// <summary>One bucket in an aggregated time series.</summary>
public record ConsumptionBucket(
    DateTime BucketStartUtc,
    double? TotalQuantity,
    int SampleCount
);

/// <summary>Wraps a series with metadata so the chart knows units & labels.</summary>
public record ConsumptionSeries(
    string EquipmentCode,
    string ClassCode,
    string ClassName,
    string MeasurementCode,
    string MeasurementName,
    string UnitSymbol,
    Granularity Granularity,
    IReadOnlyList<ConsumptionBucket> Buckets
);

public record ConsumptionQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Granularity Granularity,
    string[]? EquipmentCodes,
    string[]? ClassCodes
);

public record ComparisonResult(
    ConsumptionSeries Current,
    ConsumptionSeries Previous,
    double CurrentTotal,
    double PreviousTotal,
    double DeltaAbsolute,
    double? DeltaPercent
);

public record AnomalyPoint(
    DateTime BucketStartUtc,
    string EquipmentCode,
    string ClassCode,
    string MeasurementCode,
    string UnitSymbol,
    double Quantity,
    double ExpectedQuantity,
    double DeviationPercent,
    double ZScore,
    string Severity,
    string Reason
);
