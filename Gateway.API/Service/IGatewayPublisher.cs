using RfidGateway.Models;

namespace RfidGateway.Services;

public interface IGatewayPublisher
{
    Task PublishTagsAsync(ParkingAccessEvent accessEvent);
    Task PublishTagForCreationAsync(TagReadMessage tag);
    Task PublishStatusAsync(ReaderStatus status);
}
