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

    public void SetGpo(ushort portNumber, bool isActive) { lock (_sync) _reader.SetGpo(portNumber, isActive); }

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

    public IReadOnlyList<Models.AntennaStatus> GetAntennaStatus()
    {
        lock (_sync)
        {
            var settings = _reader.QuerySettings();
            var status = _reader.QueryStatus();
            var result = new List<Models.AntennaStatus>();
            foreach (AntennaConfig a in settings.Antennas)
            {
                Impinj.OctaneSdk.AntennaStatus? antennaStatus = null;
                try { antennaStatus = status.Antennas.GetAntenna(a.PortNumber); } catch { }
                result.Add(new Models.AntennaStatus(
                    a.PortNumber,
                    antennaStatus?.IsConnected ?? false,
                    a.TxPowerInDbm,
                    a.RxSensitivityInDbm));
            }
            return result;
        }
    }

    public Models.AntennaConfiguration GetAntennaConfiguration()
    {
        lock (_sync)
        {
            var settings = _reader.QuerySettings();
            AntennaConfig? antenna = null;
            foreach (AntennaConfig configuredAntenna in settings.Antennas)
            {
                antenna = configuredAntenna;
                break;
            }

            if (antenna is null)
                throw new InvalidOperationException("No antenna configuration was found on the reader.");

            return new Models.AntennaConfiguration(
                antenna.TxPowerInDbm,
                antenna.RxSensitivityInDbm);
        }
    }

    public void ApplyAntennaConfigurationToAll(Models.AntennaConfiguration configuration)
    {
        lock (_sync)
        {
            var settings = _reader.QuerySettings();
            foreach (AntennaConfig antenna in settings.Antennas)
            {
                antenna.TxPowerInDbm = configuration.Power;
                antenna.RxSensitivityInDbm = configuration.Sensitivity;
            }

            _reader.ApplySettings(settings);
        }
    }
}
