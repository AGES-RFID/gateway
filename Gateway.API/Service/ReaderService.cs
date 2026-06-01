using Impinj.OctaneSdk;
using Microsoft.Extensions.Configuration;
using RfidGateway.Models;
using System.Diagnostics.CodeAnalysis;

namespace RfidGateway.Services;

[ExcludeFromCodeCoverage]
public sealed class ReaderService : IReaderService
{
    private readonly ImpinjReader _reader = new();
    private readonly object _sync = new();

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

    public void ConfigureAndApplySettings(IConfiguration configuration)
    {
        lock (_sync)
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

            _reader.ApplySettings(settings);
        }
    }

    public void Start() { lock (_sync) _reader.Start(); }

    public void Stop() { lock (_sync) _reader.Stop(); }

    public void Disconnect() { lock (_sync) _reader.Disconnect(); }

    public IReadOnlyList<AntennaResponse> GetAntennas()
    {
        lock (_sync)
        {
            var settings = _reader.QuerySettings();
            var status = _reader.QueryStatus();
            var result = new List<AntennaResponse>();
            foreach (AntennaConfig a in settings.Antennas)
            {
                AntennaStatus? antennaStatus = null;
                try { antennaStatus = status.Antennas.GetAntenna(a.PortNumber); } catch { }
                result.Add(new AntennaResponse(a.PortNumber, a.TxPowerInDbm, a.RxSensitivityInDbm, antennaStatus?.IsConnected ?? false));
            }
            return result;
        }
    }

    public AntennaResponse? GetAntenna(ushort port)
    {
        lock (_sync)
        {
            var settings = _reader.QuerySettings();
            var config = settings.Antennas.GetAntenna(port);
            if (config is null) return null;
            AntennaStatus? antennaStatus = null;
            try
            {
                var status = _reader.QueryStatus();
                antennaStatus = status.Antennas.GetAntenna(port);
            }
            catch { }
            return new AntennaResponse(config.PortNumber, config.TxPowerInDbm, config.RxSensitivityInDbm, antennaStatus?.IsConnected ?? false);
        }
    }

    public bool UpdateAntenna(ushort portNumber, double? power, double? sensitivity)
    {
        lock (_sync)
        {
            var settings = _reader.QuerySettings();
            var antenna = settings.Antennas.GetAntenna(portNumber);
            if (antenna is null) return false;
            if (power.HasValue) antenna.TxPowerInDbm = power.Value;
            if (sensitivity.HasValue) antenna.RxSensitivityInDbm = sensitivity.Value;
            _reader.ApplySettingsWithoutFactoryReset(settings);
            return true;
        }
    }

    public IReadOnlyList<AntennaStat> GetAntennaStats()
    {
        lock (_sync)
        {
            var status = _reader.QueryStatus();
            var result = new List<AntennaStat>();
            foreach (AntennaStatus a in status.Antennas)
                result.Add(new AntennaStat(a.PortNumber, a.IsConnected));
            return result;
        }
    }
}
