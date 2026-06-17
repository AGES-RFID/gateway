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
    private readonly IReaderService _reader;

    public GatewayPublisher(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GatewayPublisher> logger,
        IReaderService reader)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _reader = reader;
    }

    public async Task PublishTagsAsync(ParkingAccessEvent accessEvent)
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
            {
                _logger.LogWarning("Publishing tag to {Domain} returned {Status}", domain, resp.StatusCode);
                return;
            }

            _logger.LogDebug("Published tag to {Domain}", domain);

            var gpoPort = _configuration.GetValue<ushort>("Reader:GpoPort", 1);
            var gpoDuration = _configuration.GetValue("Reader:GpoDurationSeconds", 15);
            _ = ActivateGpoAsync(gpoPort, gpoDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish tag to {Domain}", domain);
        }
    }

    public async Task PublishTagForCreationAsync(TagReadMessage tag)
    {
        var domain = _configuration["Gateway:Domain"];
        var endpoint = _configuration["Gateway:TagCreationEndpoint"] ?? "api/tags";

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogDebug("Gateway:Domain or Gateway:TagCreationEndpoint not configured; skipping tag creation publish.");
            return;
        }

        try
        {
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(tag, opts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _httpClient.PostAsync($"{domain}/{endpoint}", content).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Publishing tag for creation to {Domain} returned {Status}", domain, resp.StatusCode);
                return;
            }

            _logger.LogDebug("Published tag for creation to {Domain}", domain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish tag for creation to {Domain}", domain);
        }
    }

    public async Task PublishStatusAsync(ReaderStatus status)
    {
        var domain = _configuration["Gateway:Domain"];
        var endpoint = _configuration["Gateway:StatusEndpoint"] ?? "api/status";

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogDebug("Gateway:Domain or Gateway:StatusEndpoint not configured; skipping status publish.");
            return;
        }

        try
        {
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(status, opts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _httpClient.PostAsync($"{domain}/{endpoint}", content).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Publishing status to {Domain} returned {Status}", domain, resp.StatusCode);
            else
                _logger.LogDebug("Published status to {Domain}", domain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish status to {Domain}", domain);
        }
    }

    private async Task ActivateGpoAsync(ushort portNumber, int durationSeconds)
    {
        try
        {
            _reader.SetGpo(portNumber, true);
            _logger.LogInformation("GPO port {Port} activated for {Duration}s", portNumber, durationSeconds);

            await Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ConfigureAwait(false);

            _reader.SetGpo(portNumber, false);
            _logger.LogInformation("GPO port {Port} deactivated", portNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to control GPO port {Port}", portNumber);
            try { _reader.SetGpo(portNumber, false); } catch { }
        }
    }
}
