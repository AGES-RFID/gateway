using System.Text.Json.Serialization;

namespace RfidGateway.Models;

public sealed class AccessRequest
{
    [JsonPropertyName("tag_id")]
    public required string TagId { get; init; }
}
