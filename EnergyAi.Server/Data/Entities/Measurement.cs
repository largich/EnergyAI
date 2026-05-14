namespace EnergyAi.Server.Data.Entities;

/// <summary>
/// Maps to utl.measurement — a named measurement definition, linked to its unit of measure.
/// </summary>
public class Measurement
{
    public long Id { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;

    public long UnitOfMeasureId { get; set; }
    public UnitOfMeasure UnitOfMeasure { get; set; } = default!;
}
