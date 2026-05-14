namespace EnergyAi.Server.Data.Entities;

/// <summary>
/// Maps to opex.counter_value — a single time-series measurement point.
/// </summary>
public class CounterValue
{
    public long Id { get; set; }

    /// <summary>Location code (e.g. "Building A").</summary>
    public string EquipmentCode { get; set; } = default!;

    public DateTime UtcMeasurementStartTime { get; set; }
    public DateTime UtcMeasurementEndTime { get; set; }

    /// <summary>Consumed quantity in the unit defined on the related Measurement.</summary>
    public double Quantity { get; set; }

    public int TransferCodeId { get; set; }
    public TransferCode TransferCode { get; set; } = default!;

    public int TransferClassCodeId { get; set; }
    public TransferClassCode TransferClassCode { get; set; } = default!;
}
