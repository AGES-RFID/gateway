using Impinj.OctaneSdk;

namespace RfidGateway.Services;

public sealed class ReaderService
{
    private readonly ImpinjReader _reader = new();
    private readonly object _sync = new();
    private readonly ILogger<ReaderService> _logger;

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

    public Settings QueryDefaultSettings() { lock (_sync) return _reader.QueryDefaultSettings(); }

    public void ApplySettings(Settings settings) { lock (_sync) _reader.ApplySettings(settings); }

    public void Start() { lock (_sync) _reader.Start(); }

    public void Stop() { lock (_sync) _reader.Stop(); }

    public void Disconnect() { lock (_sync) _reader.Disconnect(); }

    public Settings QuerySettings() { lock (_sync) return _reader.QuerySettings(); }

    public Status QueryReaderStatus() { lock (_sync) return _reader.QueryStatus(); }

    public bool UpdateAntenna(ushort portNumber, Action<AntennaConfig> configure)
    {
        lock (_sync)
        {
            var settings = _reader.QuerySettings();
            var antenna = settings.Antennas.GetAntenna(portNumber);
            if (antenna is null) return false;
            configure(antenna);
            _reader.ApplySettingsWithoutFactoryReset(settings);
            return true;
        }
    }
}
