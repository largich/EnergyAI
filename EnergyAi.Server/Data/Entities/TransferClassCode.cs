namespace EnergyAi.Server.Data.Entities;

/// <summary>
/// Maps to cdr.transfer_class_code — represents the consumption category
/// (e.g. ELECTRICITY, GAS, WATER).
/// </summary>
public class TransferClassCode
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public string? Name { get; set; }
}
