using Impinj.OctaneSdk;
using Microsoft.Extensions.Configuration;

namespace RfidGateway.Services;

public interface IReaderService
{
    bool IsConnected { get; }

    IReadOnlyList<Models.AntennaStatus> GetAntennaStatus();
    void ApplyAntennaConfiguration(IReadOnlyList<Models.AntennaStatus> antennas);

    void SubscribeToEvents(
        Action<ImpinjReader, TagReport> onTagsReported,
        Action<ImpinjReader> onKeepalive,
        Action<ImpinjReader> onConnectionLost);
    void Connect(string hostname);
    void ConfigureAndApplySettings(IConfiguration configuration);
    void SetGpo(ushort portNumber, bool isActive);
    void Start();
    void Stop();
    void Disconnect();
}
