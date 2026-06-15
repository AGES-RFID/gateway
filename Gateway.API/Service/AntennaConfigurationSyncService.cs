using RfidGateway.Models;

namespace RfidGateway.Services;

public sealed class AntennaConfigurationSyncService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AntennaConfigurationSyncService> _logger;
    private readonly IReaderService _readerService;
    private readonly IAntennaConfigurationClient _configurationClient;

    public AntennaConfigurationSyncService(
        IConfiguration configuration,
        ILogger<AntennaConfigurationSyncService> logger,
        IReaderService readerService,
        IAntennaConfigurationClient configurationClient)
    {
        _configuration = configuration;
        _logger = logger;
        _readerService = readerService;
        _configurationClient = configurationClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = _configuration.GetValue("Gateway:AntennaConfigurationSyncIntervalSeconds", 60);
        var interval = TimeSpan.FromSeconds(Math.Max(1, intervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncOnceAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task SyncOnceAsync(CancellationToken cancellationToken)
    {
        if (!_readerService.IsConnected)
        {
            _logger.LogDebug("Reader is disconnected; skipping antenna configuration sync.");
            return;
        }

        try
        {
            var currentConfiguration = _readerService.GetAntennaConfiguration();
            var desiredConfiguration = await _configurationClient
                .CheckForDesiredConfigurationAsync(currentConfiguration, cancellationToken)
                .ConfigureAwait(false);

            if (desiredConfiguration is null)
                return;

            ApplyDesiredConfiguration(desiredConfiguration);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply antenna configuration sync.");
        }
    }

    private void ApplyDesiredConfiguration(AntennaConfiguration desiredConfiguration)
    {
        _readerService.ApplyAntennaConfigurationToAll(desiredConfiguration);
        _logger.LogInformation(
            "Applied antenna configuration to all antennas. Power={Power}, Sensitivity={Sensitivity}",
            desiredConfiguration.Power,
            desiredConfiguration.Sensitivity);
    }
}
