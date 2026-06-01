using Impinj.OctaneSdk;
using Microsoft.Extensions.Configuration;
using RfidGateway.Models;

namespace RfidGateway.Services;

public interface IReaderService
{
    bool IsConnected { get; }

    IReadOnlyList<AntennaResponse> GetAntennas();
    AntennaResponse? GetAntenna(ushort port);
    bool UpdateAntenna(ushort portNumber, double? power, double? sensitivity);
    IReadOnlyList<AntennaStat> GetAntennaStats();

    void SubscribeToEvents(
        Action<ImpinjReader, TagReport> onTagsReported,
        Action<ImpinjReader> onKeepalive,
        Action<ImpinjReader> onConnectionLost);
    void Connect(string hostname);
    void ConfigureAndApplySettings(IConfiguration configuration);
    void Start();
    void Stop();
    void Disconnect();
}
