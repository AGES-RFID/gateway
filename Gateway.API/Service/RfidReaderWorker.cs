using Impinj.OctaneSdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RfidGateway.Models;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace RfidGateway.Services;

public sealed class RfidReaderWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RfidReaderWorker> _logger;
    private readonly ReaderService _readerService;
    private readonly ReaderStatusService _status;
    private readonly HttpClient _httpClient = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastPublishedAt = new();
    private TimeSpan _tagCooldown;

    public RfidReaderWorker(
        IConfiguration configuration,
        ILogger<RfidReaderWorker> logger,
        ReaderService readerService,
        ReaderStatusService status)
    {
        _configuration = configuration;
        _logger = logger;
        _readerService = readerService;
        _status = status;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var hostname = _configuration["Reader:Hostname"]
            ?? throw new InvalidOperationException("Reader:Hostname was not configured.");

        _readerService.SubscribeToEvents(OnTagsReported, OnKeepaliveReceived, OnConnectionLost);

        _logger.LogInformation("Connecting to Impinj reader at {Hostname}", hostname);
        _readerService.Connect(hostname);

        var settings = _readerService.QueryDefaultSettings();

        settings.Report.Mode = ReportMode.Individual;
        settings.Report.IncludeAntennaPortNumber = true;
        settings.Report.IncludeFastId = _configuration.GetValue("Reader:IncludeFastId", true);
        settings.Report.IncludePeakRssi = _configuration.GetValue("Reader:IncludePeakRssi", true);
        settings.Report.IncludeFirstSeenTime = _configuration.GetValue("Reader:IncludeFirstSeenTime", true);
        settings.Report.IncludeLastSeenTime = _configuration.GetValue("Reader:IncludeLastSeenTime", true);
        settings.Report.IncludeSeenCount = _configuration.GetValue("Reader:IncludeSeenCount", true);

        var antennaIds = _configuration.GetSection("Reader:AntennaIds").Get<ushort[]>() ?? Array.Empty<ushort>();
        settings.Antennas.DisableAll();

        if (antennaIds.Length == 0)
            settings.Antennas.EnableAll();
        else
            settings.Antennas.EnableById(antennaIds);

        settings.Session = _configuration.GetValue<ushort?>("Reader:Session") ?? 2;
        settings.TagPopulationEstimate = _configuration.GetValue<ushort?>("Reader:TagPopulationEstimate") ?? 32;

        _readerService.ApplySettings(settings);
        _readerService.Start();

        var cooldownSeconds = _configuration.GetValue("Reader:TagCooldownSeconds", 30);
        _tagCooldown = TimeSpan.FromSeconds(cooldownSeconds);

        _status.SetConnected(true);
        _logger.LogInformation("Reader started. Tag cooldown: {Cooldown}s", cooldownSeconds);

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(1000, stoppingToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Stopping reader.");
            _readerService.Stop();
            _readerService.Disconnect();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while stopping/disconnecting reader.");
        }

        return base.StopAsync(cancellationToken);
    }

    private void OnTagsReported(ImpinjReader _, TagReport report)
    {
        foreach (Tag tag in report)
        {
            var message = new TagReadMessage
            {
                Epc = tag.Epc?.ToHexString() ?? string.Empty,
                AntennaPort = tag.AntennaPortNumber,
                PeakRssiInDbm = tag.IsPeakRssiInDbmPresent ? tag.PeakRssiInDbm : null,
                FirstSeenUtc = tag.IsFirstSeenTimePresent ? tag.FirstSeenTime.LocalDateTime : null,
                LastSeenUtc = tag.IsLastSeenTimePresent ? tag.LastSeenTime.LocalDateTime : null,
                SeenCount = tag.IsSeenCountPresent ? tag.TagSeenCount : null,
                Tid = tag.IsFastIdPresent ? tag.Tid.ToHexString() : null
            };

            var now = DateTime.UtcNow;
            if (_lastPublishedAt.TryGetValue(message.Epc, out var lastSeen) && now - lastSeen < _tagCooldown)
                continue;

            _lastPublishedAt[message.Epc] = now;

            var accessEvent = new ParkingAccessEvent
            {
                Tid = message.Tid ?? string.Empty,
                Epc = message.Epc,
                Entrance = message.AntennaPort == 1,
            };

            _logger.LogInformation(
                "TAG EPC={Epc} ANT={Antenna} RSSI={Rssi} FIRST={FirstSeen} LAST={LastSeen} COUNT={SeenCount} TID={Tid}",
                message.Epc,
                message.AntennaPort,
                message.PeakRssiInDbm,
                message.FirstSeenUtc,
                message.LastSeenUtc,
                message.SeenCount,
                message.Tid);

            PublishToGateway(accessEvent);
        }
    }

    private void PublishToGateway(ParkingAccessEvent accessEvent)
    {
        var domain = _configuration["Gateway:Domain"];
        var endpoint = _configuration["Gateway:Endpoint"];

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogDebug("Gateway:Domain or Gateway:Endpoint not configured; skipping publish.");
            return;
        }

        _ = PublishToGatewayAsync($"{domain}/{endpoint}", accessEvent);
    }

    private async Task PublishToGatewayAsync(string url, ParkingAccessEvent accessEvent)
    {
        try
        {
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(accessEvent, opts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Publishing tag to {Url} returned {Status}", url, resp.StatusCode);
            else
                _logger.LogDebug("Published tag to {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish tag to {Url}", url);
        }
    }

    private void OnKeepaliveReceived(ImpinjReader _)
    {
        _logger.LogDebug("Keepalive received from reader.");
    }

    private void OnConnectionLost(ImpinjReader _)
    {
        _status.SetConnected(false);
        _logger.LogWarning("Connection lost to reader.");
    }
}
