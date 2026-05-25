namespace RfidGateway.Models;

public sealed class CreateTagRequest
{
    public required string Epc { get; init; }
    public required string Tid { get; init; }
}