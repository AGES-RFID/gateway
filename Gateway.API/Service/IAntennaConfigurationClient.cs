using RfidGateway.Models;

namespace RfidGateway.Services;

public interface IAntennaConfigurationClient
{
    Task<AntennaConfiguration?> CheckForDesiredConfigurationAsync(
        AntennaConfiguration currentConfiguration,
        CancellationToken cancellationToken);
}
