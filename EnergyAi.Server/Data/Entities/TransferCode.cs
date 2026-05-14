namespace EnergyAi.Server.Data.Entities;

/// <summary>
/// Maps to cdr.transfer_code — identifies the concrete measurement source.
/// Joined to utl.measurement by Code.
/// </summary>
public class TransferCode
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public string? Name { get; set; }

    public Measurement? Measurement { get; set; }
}
