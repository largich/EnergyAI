using EnergyAi.Server.Dtos;

namespace EnergyAi.Server.Services;

public interface IAnomalyService
{
    Task<IReadOnlyList<AnomalyPoint>> DetectAsync(
        ConsumptionQuery query,
        double zScoreThreshold = 2.5,
        double deviationPercentThreshold = 30.0,
        CancellationToken ct = default);
}

/// <summary>
/// Two overlapping detectors running on each aggregated series:
///   1. Z-score vs the series mean/std — catches points "far" from normal.
///   2. Rolling-median deviation — catches sharp spikes compared to recent neighbours.
/// A point is flagged if either test fires.
/// </summary>
public class AnomalyService : IAnomalyService
{
    private readonly IConsumptionService _consumption;

    public AnomalyService(IConsumptionService consumption) => _consumption = consumption;

    public async Task<IReadOnlyList<AnomalyPoint>> DetectAsync(
        ConsumptionQuery query,
        double zScoreThreshold = 2.5,
        double deviationPercentThreshold = 30.0,
        CancellationToken ct = default)
    {
        var series = await _consumption.GetSeriesAsync(query, ct);
        var results = new List<AnomalyPoint>();

        foreach (var s in series)
        {
            if (s.Buckets.Count < 5) continue; // need a few points before statistics are meaningful

            // TotalQuantity is nullable — treat missing buckets as 0 for statistics.
            var values = s.Buckets.Select(b => b.TotalQuantity ?? 0d).ToArray();
            var mean = values.Average();
            var std = StdDev(values, mean);

            // rolling median (window of 7 buckets) for neighbour-based comparison
            var rolling = RollingMedian(values, window: 7);

            for (int i = 0; i < s.Buckets.Count; i++)
            {
                var b = s.Buckets[i];
                var v = b.TotalQuantity ?? 0d;

                double z = std > 0 ? (v - mean) / std : 0;
                double expected = rolling[i];
                double deviationPct = expected == 0 ? 0 : (v - expected) / expected * 100.0;

                var zTrigger = Math.Abs(z) >= zScoreThreshold;
                var devTrigger = Math.Abs(deviationPct) >= deviationPercentThreshold;

                if (!zTrigger && !devTrigger) continue;

                var severity = Math.Abs(z) >= zScoreThreshold * 1.5 || Math.Abs(deviationPct) >= deviationPercentThreshold * 2
                    ? "high"
                    : "medium";

                var reasonParts = new List<string>();
                if (zTrigger) reasonParts.Add($"z-score {z:F2} (|z| ≥ {zScoreThreshold:F1})");
                if (devTrigger) reasonParts.Add($"deviates {deviationPct:F1}% from rolling median (|%|≥{deviationPercentThreshold:F0})");

                results.Add(new AnomalyPoint(
                    BucketStartUtc: b.BucketStartUtc,
                    EquipmentCode: s.EquipmentCode,
                    ClassCode: s.ClassCode,
                    MeasurementCode: s.MeasurementCode,
                    UnitSymbol: s.UnitSymbol,
                    Quantity: b.TotalQuantity ?? 0,
                    ExpectedQuantity: (double)Math.Round(expected, 4),
                    DeviationPercent: (double)Math.Round(deviationPct, 2),
                    ZScore: (double)Math.Round(z, 3),
                    Severity: severity,
                    Reason: string.Join("; ", reasonParts)
                ));
            }
        }

        return results
            .OrderByDescending(a => a.Severity == "high")
            .ThenByDescending(a => Math.Abs(a.ZScore))
            .ToList();
    }

    private static double StdDev(double[] values, double mean)
    {
        if (values.Length < 2) return 0;
        double sum = 0;
        foreach (var v in values) { var d = v - mean; sum += d * d; }
        return Math.Sqrt(sum / (values.Length - 1));
    }

    private static double[] RollingMedian(double[] values, int window)
    {
        var result = new double[values.Length];
        var half = window / 2;
        for (int i = 0; i < values.Length; i++)
        {
            var start = Math.Max(0, i - half);
            var end = Math.Min(values.Length, i + half + 1);
            var slice = new double[end - start];
            Array.Copy(values, start, slice, 0, slice.Length);
            Array.Sort(slice);
            result[i] = slice.Length % 2 == 1
                ? slice[slice.Length / 2]
                : (slice[slice.Length / 2 - 1] + slice[slice.Length / 2]) / 2.0;
        }
        return result;
    }
}
