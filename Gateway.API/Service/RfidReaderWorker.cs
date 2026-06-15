using Impinj.OctaneSdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RfidGateway.Models;
using System.Collections.Concurrent;

namespace RfidGateway.Services;

public sealed class RfidReaderWorker : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RfidReaderWorker> _logger;
    private readonly IReaderService _readerService;
    private readonly ReaderStatusService _status;
    private readonly IGatewayPublisher _publisher;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ConcurrentDictionary<string, DateTime> _lastPublishedAt = new();
    private ReaderStatus? _lastPublishedStatus;
    internal TimeSpan _tagCooldown;

    public RfidReaderWorker(
        IConfiguration configuration,
        ILogger<RfidReaderWorker> logger,
        IReaderService readerService,
        ReaderStatusService status,
        IGatewayPublisher publisher,
        IHostApplicationLifetime applicationLifetime)
    {
        _configuration = configuration;
        _logger = logger;
        _readerService = readerService;
        _status = status;
        _publisher = publisher;
        _applicationLifetime = applicationLifetime;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var hostname = _configuration["Reader:Hostname"]
            ?? throw new InvalidOperationException("Reader:Hostname was not configured.");

        _readerService.SubscribeToEvents(OnTagsReported, OnKeepaliveReceived, OnConnectionLost);

        var cooldownSeconds = _configuration.GetValue("Reader:TagCooldownSeconds", 30);
        _tagCooldown = TimeSpan.FromSeconds(cooldownSeconds);

        await StartReaderWithRetryAsync(hostname, cancellationToken);

        _status.SetConnected(true);
        PublishStatusIfChanged();
        _logger.LogInformation("Reader started. Tag cooldown: {Cooldown}s", cooldownSeconds);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Stopping reader.");
            _readerService.Stop();
            _readerService.Disconnect();
            _status.SetConnected(false);
            PublishStatusIfChanged();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while stopping/disconnecting reader.");
        }

        return Task.CompletedTask;
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

            ProcessTag(message);
        }
    }

    internal void ProcessTag(TagReadMessage message)
    {
        var now = DateTime.UtcNow;
        if (_lastPublishedAt.TryGetValue(message.Epc, out var lastSeen) && now - lastSeen < _tagCooldown)
            return;

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

        _ = _publisher.PublishTagsAsync(accessEvent);
    }

    private void OnKeepaliveReceived(ImpinjReader _)
    {
        _logger.LogDebug("Keepalive received from reader.");
        PublishStatusIfChanged();
    }

    private void OnConnectionLost(ImpinjReader _)
    {
        _status.SetConnected(false);
        PublishStatusIfChanged();
        _logger.LogWarning("Connection lost to reader.");
    }

    private void PublishStatusIfChanged()
    {
        var status = GetCurrentStatus();
        if (StatusEquals(_lastPublishedStatus, status))
            return;

        _lastPublishedStatus = status;
        _ = _publisher.PublishStatusAsync(status);
    }

    private ReaderStatus GetCurrentStatus()
    {
        var connected = _status.IsConnected;
        IReadOnlyList<Models.AntennaStatus> antennas = [];

        if (connected)
        {
            try
            {
                antennas = _readerService
                    .GetAntennaStatus()
                    .OrderBy(a => a.Port)
                    .ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query antenna status.");
                connected = false;
            }
        }

        return new ReaderStatus(connected, antennas);
    }

    private static bool StatusEquals(ReaderStatus? current, ReaderStatus next) =>
        current is not null &&
        current.Connected == next.Connected &&
        current.Antennas.SequenceEqual(next.Antennas);

    private async Task StartReaderWithRetryAsync(string hostname, CancellationToken cancellationToken)
    {
        var retryTimeout = TimeSpan.FromMinutes(_configuration.GetValue("Reader:StartupRetryTimeoutMinutes", 60));
        var retryInterval = TimeSpan.FromSeconds(_configuration.GetValue("Reader:StartupRetryIntervalSeconds", 5));
        if (retryInterval < TimeSpan.Zero)
            retryInterval = TimeSpan.Zero;

        var retryUntil = DateTimeOffset.UtcNow + retryTimeout;
        var attempt = 1;
        Exception? lastException = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogInformation(
                    "Connecting to Impinj reader at {Hostname}. Attempt {Attempt}",
                    hostname,
                    attempt);

                _readerService.Connect(hostname);
                _readerService.ConfigureAndApplySettings(_configuration);
                _readerService.Start();
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastException = ex;
                CleanupReaderAfterFailedStartupAttempt();

                if (DateTimeOffset.UtcNow >= retryUntil)
                    break;

                _logger.LogWarning(
                    ex,
                    "Reader not found at {Hostname}. Retrying for up to {RetryTimeoutMinutes} minutes.",
                    hostname,
                    retryTimeout.TotalMinutes);

                var remainingRetryTime = retryUntil - DateTimeOffset.UtcNow;
                if (remainingRetryTime <= TimeSpan.Zero)
                    break;

                await Task.Delay(
                    retryInterval < remainingRetryTime ? retryInterval : remainingRetryTime,
                    cancellationToken);
                attempt++;
            }
        }

        _logger.LogCritical(
            lastException,
            "Reader was not found at {Hostname} after {RetryTimeoutMinutes} minutes. Shutting down application.",
            hostname,
            retryTimeout.TotalMinutes);

        _applicationLifetime.StopApplication();

        throw new InvalidOperationException(
            $"Reader was not found at {hostname} after {retryTimeout.TotalMinutes} minutes.",
            lastException);
    }

    private void CleanupReaderAfterFailedStartupAttempt()
    {
        try
        {
            _readerService.Stop();
            _readerService.Disconnect();
            _status.SetConnected(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error while cleaning up reader after failed startup attempt.");
        }
    }
}
