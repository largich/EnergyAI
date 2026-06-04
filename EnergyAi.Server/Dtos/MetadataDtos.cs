namespace EnergyAi.Server.Dtos;

public record ClassCodeDto(long Id, string Code, string? Name);
public record EquipmentDto(string Code);
public record MeasurementDto(string Code, string Name, string UnitSymbol);
public record TransferCodeDto(string Code, string? Name);
