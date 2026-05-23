namespace RfidGateway.Models;

public sealed record AntennaResponse(
    ushort PortNumber,
    double TxPowerInDbm,
    double RxSensitivityInDbm,
    bool IsConnected
);

public sealed record AntennaUpdateRequest(
    double? TxPowerInDbm,
    double? RxSensitivityInDbm
);
