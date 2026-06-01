using RfidGateway.Models;

namespace RfidGateway.Services;

public interface IGatewayPublisher
{
    Task PublishAsync(ParkingAccessEvent accessEvent);
}
