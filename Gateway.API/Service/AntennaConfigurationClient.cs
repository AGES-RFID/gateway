using RfidGateway.Models;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RfidGateway.Services;

[ExcludeFromCodeCoverage]
public sealed class AntennaConfigurationClient : IAntennaConfigurationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AntennaConfigurationClient> _logger;

    public AntennaConfigurationClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AntennaConfigurationClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AntennaConfiguration?> CheckForDesiredConfigurationAsync(
        AntennaConfiguration currentConfiguration,
        CancellationToken cancellationToken)
    {
        var domain = _configuration["Gateway:Domain"];
        var endpoint = _configuration["Gateway:AntennaConfigurationEndpoint"] ?? "api/antenna/configuration";

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogDebug("Gateway:Domain or Gateway:AntennaConfigurationEndpoint not configured; skipping antenna configuration sync.");
            return null;
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                $"{domain}/{endpoint}",
                currentConfiguration,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                _logger.LogDebug("Antenna configuration is already up to date.");
                return null;
            }

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var desiredConfiguration = await response.Content
                    .ReadFromJsonAsync<AntennaConfiguration>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (desiredConfiguration is null)
                    _logger.LogWarning("Backend returned 200 for antenna configuration sync, but the response body was empty.");

                return desiredConfiguration;
            }

            _logger.LogWarning(
                "Antenna configuration sync returned unexpected status {Status}",
                response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync antenna configuration with backend.");
        }

        return null;
    }
}
