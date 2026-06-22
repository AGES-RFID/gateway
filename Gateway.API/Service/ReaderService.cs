using Impinj.OctaneSdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RfidGateway.Models;
using System.Diagnostics.CodeAnalysis;

namespace RfidGateway.Services;

[ExcludeFromCodeCoverage]
public sealed class ReaderService : IReaderService
{
    private const double DefaultSensitivity = -70;
    private const double MinSensitivity = -93;
    private const double MaxSensitivity = -30;
    private readonly ImpinjReader _reader = new();
    private readonly object _sync = new();
    private readonly ILogger<ReaderService> _logger;
    private FeatureSet? _featureSet;
    private IConfiguration? _configuration;

    public ReaderService(ILogger<ReaderService> logger)
    {
        _logger = logger;
    }

    public bool IsConnected => _reader.IsConnected;

    public void SubscribeToEvents(
        Action<ImpinjReader, TagReport> onTagsReported,
        Action<ImpinjReader> onKeepalive,
        Action<ImpinjReader> onConnectionLost)
    {
        _reader.TagsReported += (s, r) => onTagsReported(s, r);
        _reader.KeepaliveReceived += s => onKeepalive(s);
        _reader.ConnectionLost += s => onConnectionLost(s);
    }

    public void Connect(string hostname) { lock (_sync) _reader.Connect(hostname); }

    public void SetGpo(ushort portNumber, bool isActive) { lock (_sync) _reader.SetGpo(portNumber, isActive); }

    public void ConfigureAndApplySettings(IConfiguration configuration)
    {
        lock (_sync)
        {
            _configuration = configuration;
            var settings = BuildConfiguredSettings(configuration);
            _reader.ApplySettings(settings);
        }
    }

    public void Start() { lock (_sync) _reader.Start(); }

    public void Stop() { lock (_sync) _reader.Stop(); }

    public void Disconnect() { lock (_sync) _reader.Disconnect(); }

    public IReadOnlyList<Models.AntennaStatus> GetAntennaStatus()
    {
        lock (_sync)
        {
            var settings = _reader.QuerySettings();
            var result = new List<Models.AntennaStatus>();
            foreach (AntennaConfig a in settings.Antennas)
            {
                result.Add(new Models.AntennaStatus(
                    a.PortNumber,
                    a.IsEnabled,
                    a.TxPowerInDbm,
                    a.RxSensitivityInDbm));
            }
            return result;
        }
    }

    public void ApplyAntennaConfiguration(IReadOnlyList<Models.AntennaStatus> antennas)
    {
        lock (_sync)
        {
            var desiredByPort = antennas.ToDictionary(a => a.Port);
            var settings = QuerySettingsOrBuildConfiguredSettings();
            var supportedPorts = GetSupportedPorts(settings);
            var supportedDesiredPorts = desiredByPort.Keys
                .Where(supportedPorts.Contains)
                .ToArray();
            var unsupportedDesiredPorts = desiredByPort.Keys
                .Where(port => !supportedPorts.Contains(port))
                .ToArray();

            if (unsupportedDesiredPorts.Length > 0 && _configuration is not null)
            {
                _logger.LogInformation(
                    "Current reader settings do not expose requested antenna ports {Ports}. Rebuilding base settings before applying backend antenna changes.",
                    string.Join(", ", unsupportedDesiredPorts));

                settings = BuildConfiguredSettings(_configuration);
                supportedPorts = GetSupportedPorts(settings);
                supportedDesiredPorts = desiredByPort.Keys
                    .Where(supportedPorts.Contains)
                    .ToArray();
                unsupportedDesiredPorts = desiredByPort.Keys
                    .Where(port => !supportedPorts.Contains(port))
                    .ToArray();
            }

            if (unsupportedDesiredPorts.Length > 0)
            {
                _logger.LogWarning(
                    "Backend requested unsupported antenna ports: {Ports}. Supported ports: {SupportedPorts}.",
                    string.Join(", ", unsupportedDesiredPorts),
                    string.Join(", ", supportedPorts.OrderBy(port => port)));
            }

            if (supportedDesiredPorts.Length > 0)
                settings.Antennas.EnableById(supportedDesiredPorts);

            foreach (AntennaConfig antenna in settings.Antennas)
            {
                if (!desiredByPort.TryGetValue(antenna.PortNumber, out var desired))
                    continue;

                var previousPower = antenna.TxPowerInDbm;
                var previousSensitivity = antenna.RxSensitivityInDbm;
                antenna.IsEnabled = desired.Connected;
                antenna.TxPowerInDbm = desired.Power;
                ApplySensitivity(antenna, desired.Sensitivity);

                if (previousPower != antenna.TxPowerInDbm)
                {
                    _logger.LogInformation(
                        "Antenna {Port} power changed from {PreviousPower} dBm to {NewPower} dBm.",
                        antenna.PortNumber,
                        previousPower,
                        antenna.TxPowerInDbm);
                }

                if (previousSensitivity != antenna.RxSensitivityInDbm)
                {
                    _logger.LogInformation(
                        "Antenna {Port} sensitivity changed from {PreviousSensitivity} dBm to {NewSensitivity} dBm.",
                        antenna.PortNumber,
                        previousSensitivity,
                        antenna.RxSensitivityInDbm);
                }

                _logger.LogInformation(
                    "Applying antenna {Port}: enabled={Enabled}, power={Power} dBm, requestedSensitivity={RequestedSensitivity} dBm, appliedSensitivity={AppliedSensitivity} dBm, maxRxSensitivity={MaxRxSensitivity}.",
                    antenna.PortNumber,
                    antenna.IsEnabled,
                    antenna.TxPowerInDbm,
                    desired.Sensitivity,
                    antenna.RxSensitivityInDbm,
                    antenna.MaxRxSensitivity);
            }

            TryStopReader();
            _reader.ApplySettings(settings);
            _reader.Start();
            LogAppliedSettings();
        }
    }

    private static HashSet<ushort> GetSupportedPorts(Settings settings)
    {
        var supportedPorts = new HashSet<ushort>();
        foreach (AntennaConfig antenna in settings.Antennas)
            supportedPorts.Add(antenna.PortNumber);

        return supportedPorts;
    }

    private void TryStopReader()
    {
        try
        {
            _reader.Stop();
        }
        catch
        {
            // The reader may already be idle; applying settings is still valid.
        }
    }

    private void LogAppliedSettings()
    {
        var appliedSettings = _reader.QuerySettings();
        foreach (AntennaConfig antenna in appliedSettings.Antennas)
        {
            _logger.LogInformation(
                "Reader now has antenna {Port}: enabled={Enabled}, power={Power} dBm, sensitivity={Sensitivity} dBm, maxRxSensitivity={MaxRxSensitivity}.",
                antenna.PortNumber,
                antenna.IsEnabled,
                antenna.TxPowerInDbm,
                antenna.RxSensitivityInDbm,
                antenna.MaxRxSensitivity);
        }
    }

    private Settings QuerySettingsOrBuildConfiguredSettings()
    {
        try
        {
            return _reader.QuerySettings();
        }
        catch (OctaneSdkException ex) when (ex.Message.Contains("not been configured", StringComparison.OrdinalIgnoreCase))
        {
            if (_configuration is null)
                throw;

            _logger.LogWarning(
                ex,
                "Reader has no active configuration. Rebuilding base reader settings before applying antenna changes.");
            return BuildConfiguredSettings(_configuration);
        }
    }

    private Settings BuildConfiguredSettings(IConfiguration configuration)
    {
        var settings = _reader.QueryDefaultSettings();

        settings.Report.Mode = ReportMode.Individual;
        settings.Report.IncludeAntennaPortNumber = true;
        settings.Report.IncludeFastId = configuration.GetValue("Reader:IncludeFastId", true);
        settings.Report.IncludePeakRssi = configuration.GetValue("Reader:IncludePeakRssi", true);
        settings.Report.IncludeFirstSeenTime = configuration.GetValue("Reader:IncludeFirstSeenTime", true);
        settings.Report.IncludeLastSeenTime = configuration.GetValue("Reader:IncludeLastSeenTime", true);
        settings.Report.IncludeSeenCount = configuration.GetValue("Reader:IncludeSeenCount", true);

        var antennaIds = configuration.GetSection("Reader:AntennaIds").Get<ushort[]>() ?? Array.Empty<ushort>();
        settings.Antennas.DisableAll();
        if (antennaIds.Length == 0)
            settings.Antennas.EnableAll();
        else
            settings.Antennas.EnableById(antennaIds);

        settings.Session = configuration.GetValue<ushort?>("Reader:Session") ?? 2;
        settings.TagPopulationEstimate = configuration.GetValue<ushort?>("Reader:TagPopulationEstimate") ?? 32;
        var defaultSensitivity = configuration.GetValue("Reader:DefaultRxSensitivityInDbm", DefaultSensitivity);
        _featureSet = _reader.QueryFeatureSet();

        foreach (AntennaConfig antenna in settings.Antennas)
            ApplySensitivity(antenna, defaultSensitivity);

        return settings;
    }

    private void ApplySensitivity(AntennaConfig antenna, double requestedSensitivity)
    {
        var supportedSensitivity = GetSupportedSensitivity(requestedSensitivity);
        if (supportedSensitivity.HasValue)
        {
            antenna.MaxRxSensitivity = false;
            antenna.RxSensitivityInDbm = supportedSensitivity.Value;
            return;
        }

        antenna.MaxRxSensitivity = true;
    }

    private double? GetSupportedSensitivity(double requestedSensitivity)
    {
        var normalized = NormalizeSensitivity(requestedSensitivity);
        _featureSet ??= _reader.QueryFeatureSet();
        var sensitivities = _featureSet.RxSensitivities;

        if (sensitivities.Count == 0)
            return null;

        return sensitivities
            .OrderBy(entry => Math.Abs(entry.Dbm - normalized))
            .First()
            .Dbm;
    }

    private static double NormalizeSensitivity(double sensitivity)
    {
        if (sensitivity == 0)
            return DefaultSensitivity;

        var normalized = sensitivity > 0 ? -sensitivity : sensitivity;
        return Math.Clamp(normalized, MinSensitivity, MaxSensitivity);
    }
}
