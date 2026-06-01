namespace RfidGateway.Models;

public sealed record AntennaResponse(
    ushort Port,
    double Power,
    double Sensitivity,
    bool Connected
);

public sealed record AntennaUpdateRequest(
    double? Power,
    double? Sensitivity
);
