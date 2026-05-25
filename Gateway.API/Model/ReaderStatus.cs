namespace RfidGateway.Models;

public sealed record ReaderStatus(
    bool Connected,
    IReadOnlyList<AntennaStat> Antennas
);

public sealed record AntennaStat(
    ushort Port,
    bool Connected
);
