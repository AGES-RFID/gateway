using Impinj.OctaneSdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RfidGateway.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RfidGateway.Services;

public sealed class RfidReaderWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RfidReaderWorker> _logger;
    private ImpinjReader? _reader;
    private readonly HttpClient _httpClient = new HttpClient();

    public RfidReaderWorker(IConfiguration configuration, ILogger<RfidReaderWorker> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var hostname = _configuration["Reader:Hostname"]
            ?? throw new InvalidOperationException("Reader:Hostname was not configured.");

        _reader = new ImpinjReader();
        _reader.TagsReported += OnTagsReported;
        _reader.KeepaliveReceived += OnKeepaliveReceived;
        _reader.ConnectionLost += OnConnectionLost;

        _logger.LogInformation("Connecting to Impinj reader at {Hostname}", hostname);
        _reader.Connect(hostname);

        var settings = _reader.QueryDefaultSettings();

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
        {
            settings.Antennas.EnableAll();
        }
        else
        {
            settings.Antennas.EnableById(antennaIds);
        }

        // settings.ReaderMode = ParseReaderMode(
        //     _configuration["Reader:ReaderMode"],
        //     ReaderMode.AutoSetDenseReader);

        // settings.SearchMode = ParseSearchMode(
        //     _configuration["Reader:SearchMode"],
        //     SearchMode.DualTarget);

        settings.Session = _configuration.GetValue<ushort?>("Reader:Session") ?? 2;
        settings.TagPopulationEstimate = _configuration.GetValue<ushort?>("Reader:TagPopulationEstimate") ?? 32;

        _reader.ApplySettings(settings);
        _reader.Start();

        _logger.LogInformation("Reader started.");
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        if (_reader is not null)
        {
            try
            {
                _logger.LogInformation("Stopping reader.");
                _reader.Stop();
                _reader.Disconnect();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while stopping/disconnecting reader.");
            }
        }

        return base.StopAsync(cancellationToken);
    }

    private void OnTagsReported(ImpinjReader sender, TagReport report)
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

            var tid = tag.IsFastIdPresent ? tag.Tid.ToHexString() : null;
            var accessEvent = new ParkingAccessEvent
            {
                Tid = message.Tid,
                Epc = message.Epc,
                Entrance = message.AntennaPort == 1,
                Timestamp = message.LastSeenUtc ?? DateTime.UtcNow
            };

            _logger.LogInformation(
                "TAG EPC={Epc} ANT={Antenna} RSSI={Rssi} FIRST={FirstSeen} LAST={LastSeen} COUNT={SeenCount} TID={Tid}",
                message.Epc,
                message.AntennaPort,
                message.PeakRssiInDbm,
                message.FirstSeenUtc,
                message.LastSeenUtc,
                message.SeenCount,
                message.Tid
            );

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

        var url = $"{domain}/{endpoint}";
        _ = PublishToGatewayAsync(url, accessEvent);
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
            {
                _logger.LogWarning("Publishing tag to {Url} returned {Status}", url, resp.StatusCode);
            }
            else
            {
                _logger.LogDebug("Published tag to {Url}", url);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish tag to {Url}", url);
        }
    }

    private void OnKeepaliveReceived(ImpinjReader sender)
    {
        _logger.LogDebug("Keepalive received from reader.");
    }

    private void OnConnectionLost(ImpinjReader sender)
    {
        _logger.LogWarning("Connection lost to reader.");
    }

    private static ReaderMode ParseReaderMode(string? value, ReaderMode fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return Enum.TryParse<ReaderMode>(value, true, out var parsed)
            ? parsed
            : fallback;
    }

    private static SearchMode ParseSearchMode(string? value, SearchMode fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return Enum.TryParse<SearchMode>(value, true, out var parsed)
            ? parsed
            : fallback;
    }
}