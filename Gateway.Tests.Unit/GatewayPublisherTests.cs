using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RfidGateway.Models;
using RfidGateway.Services;

namespace Gateway.Tests.Unit;

public class GatewayPublisherTests
{
    [Fact]
    public async Task PublishTagsAsync_WhenEntryIsAccepted_OpensEntryGate()
    {
        var reader = Substitute.For<IReaderService>();
        var publisher = CreatePublisher(HttpStatusCode.OK, reader);

        await publisher.PublishTagsAsync(CreateAccessEvent(entrance: true));

        await AssertEventuallyAsync(() =>
        {
            reader.Received(1).SetGpo(3, true);
            reader.Received(1).SetGpo(3, false);
        });
        reader.DidNotReceive().SetGpo(4, Arg.Any<bool>());
    }

    [Fact]
    public async Task PublishTagsAsync_WhenExitIsAccepted_OpensExitGate()
    {
        var reader = Substitute.For<IReaderService>();
        var publisher = CreatePublisher(HttpStatusCode.OK, reader);

        await publisher.PublishTagsAsync(CreateAccessEvent(entrance: false));

        await AssertEventuallyAsync(() =>
        {
            reader.Received(1).SetGpo(4, true);
            reader.Received(1).SetGpo(4, false);
        });
        reader.DidNotReceive().SetGpo(3, Arg.Any<bool>());
    }

    [Fact]
    public async Task PublishTagsAsync_WhenBackendRejectsAccess_DoesNotOpenGate()
    {
        var reader = Substitute.For<IReaderService>();
        var publisher = CreatePublisher(HttpStatusCode.BadRequest, reader);

        await publisher.PublishTagsAsync(CreateAccessEvent(entrance: true));

        reader.DidNotReceive().SetGpo(Arg.Any<ushort>(), Arg.Any<bool>());
    }

    private static GatewayPublisher CreatePublisher(HttpStatusCode statusCode, IReaderService reader)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:Domain"] = "http://backend.test",
                ["Gateway:Endpoint"] = "api/accesses",
                ["Reader:EntryGpoPort"] = "3",
                ["Reader:ExitGpoPort"] = "4",
                ["Reader:GpoDurationSeconds"] = "0",
            })
            .Build();

        var httpClient = new HttpClient(new StubHttpMessageHandler(statusCode));
        var logger = Substitute.For<ILogger<GatewayPublisher>>();

        return new GatewayPublisher(httpClient, configuration, logger, reader);
    }

    private static ParkingAccessEvent CreateAccessEvent(bool entrance) =>
        new()
        {
            Tid = "TID001",
            Epc = "EPC001",
            Entrance = entrance,
        };

    private static async Task AssertEventuallyAsync(Action assertion)
    {
        Exception? lastException = null;
        var timeoutAt = DateTime.UtcNow.AddSeconds(1);

        while (DateTime.UtcNow < timeoutAt)
        {
            try
            {
                assertion();
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                await Task.Delay(10);
            }
        }

        if (lastException is not null)
            throw lastException;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StubHttpMessageHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode));
    }
}
