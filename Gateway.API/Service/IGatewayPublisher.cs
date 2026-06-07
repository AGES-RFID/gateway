using RfidGateway.Models;

namespace RfidGateway.Services;

public interface IGatewayPublisher
{
    Task PublishTagsAsync(ParkingAccessEvent accessEvent);
    Task PublishStatusAsync(ReaderStatus status);
}
