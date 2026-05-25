namespace RfidGateway.Models;

public sealed record AntennaResponse(
    ushort Port,
    double TxPower,
    double RxSensitivity,
    bool Connected
);

public sealed record AntennaUpdateRequest(
    double? TxPower,
    double? RxSensitivity
);
