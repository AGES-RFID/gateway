using RfidGateway.Models;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace RfidGateway.Services;

[ExcludeFromCodeCoverage]
public sealed class GatewayPublisher : IGatewayPublisher
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GatewayPublisher> _logger;

    public GatewayPublisher(HttpClient httpClient, IConfiguration configuration, ILogger<GatewayPublisher> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task PublishAsync(ParkingAccessEvent accessEvent)
    {
        var domain = _configuration["Gateway:Domain"];
        var endpoint = _configuration["Gateway:Endpoint"];

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogDebug("Gateway:Domain or Gateway:Endpoint not configured; skipping publish.");
            return;
        }

        try
        {
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(accessEvent, opts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _httpClient.PostAsync($"{domain}/{endpoint}", content).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Publishing tag to {Domain} returned {Status}", domain, resp.StatusCode);
            else
                _logger.LogDebug("Published tag to {Domain}", domain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish tag to {Domain}", domain);
        }
    }
}
