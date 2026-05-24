namespace RfidGateway.Models;

public sealed class ParkingAccessEvent
{
    public required string Tid { get; init; }
    public required string Epc { get; init; }
    public bool Entrance { get; init; }
}
