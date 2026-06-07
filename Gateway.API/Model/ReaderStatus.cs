namespace RfidGateway.Models;

public sealed record ReaderStatus(
    bool Connected,
    IReadOnlyList<AntennaStatus> Antennas
);

public sealed record AntennaStatus(
    ushort Port,
    bool Connected,
    double Power,
    double Sensitivity
);
