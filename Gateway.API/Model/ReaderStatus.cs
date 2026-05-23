namespace RfidGateway.Models;

public sealed record ReaderStatus(
    bool IsConnected,
    IReadOnlyList<AntennaStat> Antennas
);

public sealed record AntennaStat(
    int Port,
    bool IsConnected
);
