namespace EnergyAi.Server.Data.Entities;

/// <summary>
/// Maps to utl.unit_of_measure — symbol/name for units (e.g. MW, litres, m³).
/// </summary>
public class UnitOfMeasure
{
    public long Id { get; set; }
    public string Code { get; set; } = default!;
    public string? Name { get; set; }
    public string Symbol { get; set; } = default!;
}
