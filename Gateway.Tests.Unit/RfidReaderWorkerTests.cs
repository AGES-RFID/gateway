using Impinj.OctaneSdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RfidGateway.Models;
using RfidGateway.Services;

namespace Gateway.Tests.Unit;

public class RfidReaderWorkerTests
{
    private readonly IReaderService _readerService;
    private readonly IGatewayPublisher _publisher;
    private readonly ReaderStatusService _statusService;
    private readonly RfidReaderWorker _worker;

    public RfidReaderWorkerTests()
    {
        _readerService = Substitute.For<IReaderService>();
        _publisher = Substitute.For<IGatewayPublisher>();
        _statusService = new ReaderStatusService();
        var logger = Substitute.For<ILogger<RfidReaderWorker>>();

        _worker = new RfidReaderWorker(
            BuildConfig(new Dictionary<string, string?> { ["Reader:Hostname"] = "test-reader" }),
            logger,
            _readerService,
            _statusService,
            _publisher);

        _worker._tagCooldown = TimeSpan.FromSeconds(30);
    }

    // --- StartAsync ---

    [Fact]
    public async Task StartAsync_WhenHostnameMissing_Throws()
    {
        var worker = CreateWorker(new Dictionary<string, string?>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            worker.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_WhenValidConfig_ConnectsToReader()
    {
        await _worker.StartAsync(CancellationToken.None);

        _readerService.Received(1).Connect("test-reader");
    }

    [Fact]
    public async Task StartAsync_WhenValidConfig_StartsReader()
    {
        await _worker.StartAsync(CancellationToken.None);

        _readerService.Received(1).Start();
    }

    [Fact]
    public async Task StartAsync_WhenValidConfig_SetsConnectedTrue()
    {
        await _worker.StartAsync(CancellationToken.None);

        Assert.True(_statusService.IsConnected);
    }

    // --- StopAsync ---

    [Fact]
    public async Task StopAsync_CallsStopAndDisconnect()
    {
        await _worker.StopAsync(CancellationToken.None);

        _readerService.Received(1).Stop();
        _readerService.Received(1).Disconnect();
    }

    [Fact]
    public async Task StopAsync_WhenReaderThrows_DoesNotThrow()
    {
        _readerService.When(x => x.Stop()).Throw(new Exception("reader error"));

        var exception = await Record.ExceptionAsync(() =>
            _worker.StopAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    // --- OnConnectionLost (via callback capturado do SubscribeToEvents) ---

    [Fact]
    public async Task OnConnectionLost_SetsStatusToDisconnected()
    {
        Action<ImpinjReader>? capturedCallback = null;
        _readerService.When(x => x.SubscribeToEvents(
            Arg.Any<Action<ImpinjReader, TagReport>>(),
            Arg.Any<Action<ImpinjReader>>(),
            Arg.Any<Action<ImpinjReader>>()
        )).Do(call => capturedCallback = call.ArgAt<Action<ImpinjReader>>(2));

        await _worker.StartAsync(CancellationToken.None);
        _statusService.SetConnected(true);

        capturedCallback!.Invoke(null!);

        Assert.False(_statusService.IsConnected);
    }

    // --- ProcessTag ---

    [Fact]
    public void ProcessTag_NewTag_PublishesEvent()
    {
        var message = new TagReadMessage { Epc = "AABBCC", AntennaPort = 1, Tid = "TID001" };

        _worker.ProcessTag(message);

        _publisher.Received(1).PublishAsync(Arg.Is<ParkingAccessEvent>(e =>
            e.Epc == "AABBCC" && e.Tid == "TID001"));
    }

    [Fact]
    public void ProcessTag_SameTagWithinCooldown_SkipsPublish()
    {
        var message = new TagReadMessage { Epc = "AABBCC", AntennaPort = 1 };

        _worker.ProcessTag(message);
        _worker.ProcessTag(message);

        _publisher.Received(1).PublishAsync(Arg.Any<ParkingAccessEvent>());
    }

    [Fact]
    public void ProcessTag_DifferentEpc_Publishes()
    {
        var first = new TagReadMessage { Epc = "AABBCC", AntennaPort = 1 };
        var second = new TagReadMessage { Epc = "DDEEFF", AntennaPort = 1 };

        _worker.ProcessTag(first);
        _worker.ProcessTag(second);

        _publisher.Received(2).PublishAsync(Arg.Any<ParkingAccessEvent>());
    }

    [Fact]
    public void ProcessTag_Antenna1_SetsEntranceTrue()
    {
        ParkingAccessEvent? published = null;
        _publisher.PublishAsync(Arg.Do<ParkingAccessEvent>(e => published = e))
                  .Returns(Task.CompletedTask);

        _worker.ProcessTag(new TagReadMessage { Epc = "AABBCC", AntennaPort = 1 });

        Assert.True(published!.Entrance);
    }

    [Fact]
    public void ProcessTag_Antenna2_SetsEntranceFalse()
    {
        ParkingAccessEvent? published = null;
        _publisher.PublishAsync(Arg.Do<ParkingAccessEvent>(e => published = e))
                  .Returns(Task.CompletedTask);

        _worker.ProcessTag(new TagReadMessage { Epc = "AABBCC", AntennaPort = 2 });

        Assert.False(published!.Entrance);
    }

    // --- Helpers ---

    private RfidReaderWorker CreateWorker(Dictionary<string, string?> configValues) =>
        new RfidReaderWorker(
            BuildConfig(configValues),
            Substitute.For<ILogger<RfidReaderWorker>>(),
            Substitute.For<IReaderService>(),
            new ReaderStatusService(),
            Substitute.For<IGatewayPublisher>());

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
