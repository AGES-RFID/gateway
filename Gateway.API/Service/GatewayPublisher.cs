using RfidGateway.Models;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
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
            var url = BuildUrl(domain, endpoint);
            var resp = await _httpClient.PostAsync(url, content).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                LogNotFoundUrl(resp.StatusCode, url);
                _logger.LogWarning("Publishing tag to {Url} returned {Status}", url, resp.StatusCode);
                return;
            }

            _logger.LogDebug("Published tag to {Url}", url);

            var gpoPort = ResolveGpoPort(accessEvent.Entrance);
            var gpoDuration = _configuration.GetValue("Reader:GpoDurationSeconds", 15);
            _logger.LogInformation(
                "Access accepted as {Direction}. Opening {GateDirection} gate on GPO port {Port}.",
                accessEvent.Entrance ? "entry" : "exit",
                accessEvent.Entrance ? "entry" : "exit",
                gpoPort);
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
            var url = BuildUrl(domain, endpoint);
            var resp = await _httpClient.PostAsync(url, content).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                LogNotFoundUrl(resp.StatusCode, url);
                _logger.LogWarning("Publishing tag for creation to {Url} returned {Status}", url, resp.StatusCode);
                return;
            }

            _logger.LogDebug("Published tag for creation to {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish tag for creation to {Domain}", domain);
        }
    }

    public async Task<IReadOnlyList<AntennaStatus>> PublishStatusAsync(ReaderStatus status)
    {
        var domain = _configuration["Gateway:Domain"];
        var endpoint = _configuration["Gateway:StatusEndpoint"] ?? "api/status";

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogDebug("Gateway:Domain or Gateway:StatusEndpoint not configured; skipping status publish.");
            return [];
        }

        try
        {
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(status, opts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = BuildUrl(domain, endpoint);
            var resp = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                LogNotFoundUrl(resp.StatusCode, url);
                _logger.LogWarning("Publishing status to {Url} returned {Status}", url, resp.StatusCode);
            }
            else
            {
                _logger.LogDebug("Published status to {Url}", url);
                var responseJson = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogInformation("Backend status response: {Response}", responseJson);
                var response = JsonSerializer.Deserialize<GatewayStatusResponse>(responseJson, opts);
                var desiredAntennas = response?.DesiredAntennas ?? [];

                foreach (var antenna in desiredAntennas)
                {
                    _logger.LogInformation(
                        "Backend requested antenna {Port}: connected={Connected}, power={Power} dBm, sensitivity={Sensitivity} dBm.",
                        antenna.Port,
                        antenna.Connected,
                        antenna.Power,
                        antenna.Sensitivity);
                }

                return desiredAntennas;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish status to {Domain}", domain);
        }

        return [];
    }

    public async Task ConfirmConfigurationAsync(ReaderStatus status)
    {
        var domain = _configuration["Gateway:Domain"];
        var endpoint = _configuration["Gateway:AntennaConfigurationEndpoint"] ?? "api/gateway/configuration";

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogDebug("Gateway:Domain or Gateway:AntennaConfigurationEndpoint not configured; skipping configuration confirmation.");
            return;
        }

        try
        {
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(status, opts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = BuildUrl(domain, endpoint);
            var resp = await _httpClient.PostAsync(url, content).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                LogNotFoundUrl(resp.StatusCode, url);
                _logger.LogWarning("Confirming antenna configuration to {Url} returned {Status}", url, resp.StatusCode);
                return;
            }

            _logger.LogDebug("Confirmed antenna configuration to {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm antenna configuration.");
        }
    }

    private static string BuildUrl(string domain, string endpoint) =>
        $"{domain.TrimEnd('/')}/{endpoint.TrimStart('/')}";

    private void LogNotFoundUrl(System.Net.HttpStatusCode statusCode, string url)
    {
        if (statusCode == System.Net.HttpStatusCode.NotFound)
            _logger.LogWarning("Gateway request returned 404 NotFound. Request URL: {Url}", url);
    }

    private ushort ResolveGpoPort(bool entrance)
    {
        var directionKey = entrance ? "Reader:EntryGpoPort" : "Reader:ExitGpoPort";
        var directionPort = _configuration.GetValue<ushort?>(directionKey);
        return directionPort is > 0
            ? directionPort.Value
            : _configuration.GetValue<ushort>("Reader:GpoPort", 1);
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

    private sealed record GatewayStatusResponse(IReadOnlyList<AntennaStatus> DesiredAntennas);
}
