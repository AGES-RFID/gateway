namespace RfidGateway.Models;

public sealed class TagReadMessage
{
    public required string Epc { get; init; }
    public ushort AntennaPort { get; init; }
    public double? PeakRssiInDbm { get; init; }
    public DateTime? FirstSeenUtc { get; init; }
    public DateTime? LastSeenUtc { get; init; }
    public uint? SeenCount { get; init; }
    public string? Tid { get; init; }
}