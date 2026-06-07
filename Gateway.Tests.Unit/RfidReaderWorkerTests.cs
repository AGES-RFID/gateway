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

    [Fact]
    public async Task StartAsync_WhenValidConfig_PublishesInitialStatus()
    {
        _readerService.GetAntennaStatus()
            .Returns([new RfidGateway.Models.AntennaStatus((ushort)1, true, 23.5, -70.0), new RfidGateway.Models.AntennaStatus((ushort)2, true, 20.0, -65.0)]);

        await _worker.StartAsync(CancellationToken.None);

        await _publisher.Received(1).PublishStatusAsync(Arg.Is<ReaderStatus>(s =>
            s.Connected &&
            s.Antennas.SequenceEqual(new[] { new RfidGateway.Models.AntennaStatus((ushort)1, true, 23.5, -70.0), new RfidGateway.Models.AntennaStatus((ushort)2, true, 20.0, -65.0) })));
    }

    [Fact]
    public async Task StartAsync_WhenAntennaStatusQueryFails_PublishesDisconnectedStatus()
    {
        _readerService.GetAntennaStatus()
            .Throws(new Exception("reader unavailable"));

        await _worker.StartAsync(CancellationToken.None);

        await _publisher.Received(1).PublishStatusAsync(Arg.Is<ReaderStatus>(s =>
            !s.Connected && s.Antennas.Count == 0));
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

    [Fact]
    public async Task OnConnectionLost_WhenStatusChanges_PublishesDisconnectedStatus()
    {
        Action<ImpinjReader>? capturedCallback = null;
        _readerService.GetAntennaStatus()
            .Returns([new RfidGateway.Models.AntennaStatus((ushort)1, true, 23.5, -70.0)]);
        _readerService.When(x => x.SubscribeToEvents(
            Arg.Any<Action<ImpinjReader, TagReport>>(),
            Arg.Any<Action<ImpinjReader>>(),
            Arg.Any<Action<ImpinjReader>>()
        )).Do(call => capturedCallback = call.ArgAt<Action<ImpinjReader>>(2));

        await _worker.StartAsync(CancellationToken.None);
        _publisher.ClearReceivedCalls();

        capturedCallback!.Invoke(null!);

        await _publisher.Received(1).PublishStatusAsync(Arg.Is<ReaderStatus>(s =>
            !s.Connected && s.Antennas.Count == 0));
    }

    [Fact]
    public async Task OnKeepalive_WhenAntennaStatusChanges_PublishesStatus()
    {
        Action<ImpinjReader>? capturedCallback = null;
        _readerService.GetAntennaStatus()
            .Returns(
                [new RfidGateway.Models.AntennaStatus((ushort)1, true, 23.5, -70.0)],
                [new RfidGateway.Models.AntennaStatus((ushort)1, false, 23.5, -70.0)]);
        _readerService.When(x => x.SubscribeToEvents(
            Arg.Any<Action<ImpinjReader, TagReport>>(),
            Arg.Any<Action<ImpinjReader>>(),
            Arg.Any<Action<ImpinjReader>>()
        )).Do(call => capturedCallback = call.ArgAt<Action<ImpinjReader>>(1));

        await _worker.StartAsync(CancellationToken.None);
        _publisher.ClearReceivedCalls();

        capturedCallback!.Invoke(null!);

        await _publisher.Received(1).PublishStatusAsync(Arg.Is<ReaderStatus>(s =>
            s.Connected &&
            s.Antennas.SequenceEqual(new[] { new RfidGateway.Models.AntennaStatus((ushort)1, false, 23.5, -70.0) })));
    }

    [Fact]
    public async Task OnKeepalive_WhenStatusUnchanged_DoesNotPublishStatus()
    {
        Action<ImpinjReader>? capturedCallback = null;
        _readerService.GetAntennaStatus()
            .Returns([new RfidGateway.Models.AntennaStatus((ushort)1, true, 23.5, -70.0)]);
        _readerService.When(x => x.SubscribeToEvents(
            Arg.Any<Action<ImpinjReader, TagReport>>(),
            Arg.Any<Action<ImpinjReader>>(),
            Arg.Any<Action<ImpinjReader>>()
        )).Do(call => capturedCallback = call.ArgAt<Action<ImpinjReader>>(1));

        await _worker.StartAsync(CancellationToken.None);
        _publisher.ClearReceivedCalls();

        capturedCallback!.Invoke(null!);

        await _publisher.DidNotReceive().PublishStatusAsync(Arg.Any<ReaderStatus>());
    }

    // --- OnTagsReported (via callback capturado do SubscribeToEvents) ---

    [Fact]
    public async Task OnTagsReported_WhenReaderReportsTag_PublishesAccessEvent()
    {
        Action<ImpinjReader, TagReport>? capturedCallback = null;
        _readerService.GetAntennaStatus()
            .Returns([]);
        _readerService.When(x => x.SubscribeToEvents(
            Arg.Any<Action<ImpinjReader, TagReport>>(),
            Arg.Any<Action<ImpinjReader>>(),
            Arg.Any<Action<ImpinjReader>>()
        )).Do(call => capturedCallback = call.ArgAt<Action<ImpinjReader, TagReport>>(0));

        await _worker.StartAsync(CancellationToken.None);
        _publisher.ClearReceivedCalls();
        ParkingAccessEvent? published = null;
        _publisher.PublishTagsAsync(Arg.Do<ParkingAccessEvent>(e => published = e))
            .Returns(Task.CompletedTask);

        capturedCallback!.Invoke(null!, CreateTagReport("AABBCCDD", 1, "01020304"));

        await _publisher.Received(1).PublishTagsAsync(Arg.Any<ParkingAccessEvent>());
        Assert.Equal("AABBCCDD", published!.Epc);
        Assert.True(published.Entrance);
    }

    [Fact]
    public async Task OnTagsReported_WhenReaderReportsTagWithOptionalFields_PublishesTid()
    {
        Action<ImpinjReader, TagReport>? capturedCallback = null;
        _readerService.GetAntennaStatus()
            .Returns([]);
        _readerService.When(x => x.SubscribeToEvents(
            Arg.Any<Action<ImpinjReader, TagReport>>(),
            Arg.Any<Action<ImpinjReader>>(),
            Arg.Any<Action<ImpinjReader>>()
        )).Do(call => capturedCallback = call.ArgAt<Action<ImpinjReader, TagReport>>(0));

        await _worker.StartAsync(CancellationToken.None);
        _publisher.ClearReceivedCalls();
        ParkingAccessEvent? published = null;
        _publisher.PublishTagsAsync(Arg.Do<ParkingAccessEvent>(e => published = e))
            .Returns(Task.CompletedTask);

        capturedCallback!.Invoke(null!, CreateTagReport(
            "AABBCCDD",
            2,
            "01020304",
            includeOptionalFields: true));

        await _publisher.Received(1).PublishTagsAsync(Arg.Any<ParkingAccessEvent>());
        Assert.Equal("01020304", published!.Tid);
        Assert.False(published.Entrance);
    }

    [Fact]
    public async Task OnTagsReported_WhenTagHasNoEpc_PublishesEmptyEpc()
    {
        Action<ImpinjReader, TagReport>? capturedCallback = null;
        _readerService.GetAntennaStatus()
            .Returns([]);
        _readerService.When(x => x.SubscribeToEvents(
            Arg.Any<Action<ImpinjReader, TagReport>>(),
            Arg.Any<Action<ImpinjReader>>(),
            Arg.Any<Action<ImpinjReader>>()
        )).Do(call => capturedCallback = call.ArgAt<Action<ImpinjReader, TagReport>>(0));

        await _worker.StartAsync(CancellationToken.None);
        _publisher.ClearReceivedCalls();
        ParkingAccessEvent? published = null;
        _publisher.PublishTagsAsync(Arg.Do<ParkingAccessEvent>(e => published = e))
            .Returns(Task.CompletedTask);

        capturedCallback!.Invoke(null!, CreateTagReport(null, 1, "01020304"));

        await _publisher.Received(1).PublishTagsAsync(Arg.Any<ParkingAccessEvent>());
        Assert.Equal(string.Empty, published!.Epc);
    }

    [Fact]
    public async Task OnTagsReported_WhenReportHasNoTags_DoesNotPublishAccessEvent()
    {
        Action<ImpinjReader, TagReport>? capturedCallback = null;
        _readerService.GetAntennaStatus()
            .Returns([]);
        _readerService.When(x => x.SubscribeToEvents(
            Arg.Any<Action<ImpinjReader, TagReport>>(),
            Arg.Any<Action<ImpinjReader>>(),
            Arg.Any<Action<ImpinjReader>>()
        )).Do(call => capturedCallback = call.ArgAt<Action<ImpinjReader, TagReport>>(0));

        await _worker.StartAsync(CancellationToken.None);
        _publisher.ClearReceivedCalls();

        capturedCallback!.Invoke(null!, CreateTagReport());

        await _publisher.DidNotReceive().PublishTagsAsync(Arg.Any<ParkingAccessEvent>());
    }

    // --- ProcessTag ---

    [Fact]
    public void ProcessTag_NewTag_PublishesEvent()
    {
        var message = new TagReadMessage { Epc = "AABBCC", AntennaPort = 1, Tid = "TID001" };

        _worker.ProcessTag(message);

        _publisher.Received(1).PublishTagsAsync(Arg.Is<ParkingAccessEvent>(e =>
            e.Epc == "AABBCC" && e.Tid == "TID001"));
    }

    [Fact]
    public void ProcessTag_SameTagWithinCooldown_SkipsPublish()
    {
        var message = new TagReadMessage { Epc = "AABBCC", AntennaPort = 1 };

        _worker.ProcessTag(message);
        _worker.ProcessTag(message);

        _publisher.Received(1).PublishTagsAsync(Arg.Any<ParkingAccessEvent>());
    }

    [Fact]
    public void ProcessTag_DifferentEpc_Publishes()
    {
        var first = new TagReadMessage { Epc = "AABBCC", AntennaPort = 1 };
        var second = new TagReadMessage { Epc = "DDEEFF", AntennaPort = 1 };

        _worker.ProcessTag(first);
        _worker.ProcessTag(second);

        _publisher.Received(2).PublishTagsAsync(Arg.Any<ParkingAccessEvent>());
    }

    [Fact]
    public void ProcessTag_Antenna1_SetsEntranceTrue()
    {
        ParkingAccessEvent? published = null;
        _publisher.PublishTagsAsync(Arg.Do<ParkingAccessEvent>(e => published = e))
                  .Returns(Task.CompletedTask);

        _worker.ProcessTag(new TagReadMessage { Epc = "AABBCC", AntennaPort = 1 });

        Assert.True(published!.Entrance);
    }

    [Fact]
    public void ProcessTag_Antenna2_SetsEntranceFalse()
    {
        ParkingAccessEvent? published = null;
        _publisher.PublishTagsAsync(Arg.Do<ParkingAccessEvent>(e => published = e))
                  .Returns(Task.CompletedTask);

        _worker.ProcessTag(new TagReadMessage { Epc = "AABBCC", AntennaPort = 2 });

        Assert.False(published!.Entrance);
    }

    [Fact]
    public async Task PublishStatus_WhenAntennaStatusIncludesConnectedAndPower_SendsValues()
    {
        _readerService.GetAntennaStatus()
            .Returns([new RfidGateway.Models.AntennaStatus((ushort)1, true, 23.5, -70.0)]);

        await _worker.StartAsync(CancellationToken.None);

        await _publisher.Received(1).PublishStatusAsync(Arg.Is<ReaderStatus>(s =>
            s.Antennas.Count == 1 &&
            s.Antennas[0].Connected &&
            s.Antennas[0].Power == 23.5 &&
            s.Antennas[0].Sensitivity == -70.0));
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

    private static TagReport CreateTagReport(
        string? epc,
        ushort antennaPort,
        string tid,
        bool includeOptionalFields = false)
    {
        var report = (TagReport)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(TagReport));
        report.Tags = [];
        var tag = (Tag)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Tag));
        tag.Epc = epc is null ? null : TagData.FromHexString(epc);
        tag.AntennaPortNumber = antennaPort;
        tag.Tid = TagData.FromHexString(tid);
        if (includeOptionalFields)
        {
            tag.IsFastIdPresent = true;
            tag.IsPeakRssiInDbmPresent = true;
            tag.PeakRssiInDbm = -42.5;
            tag.IsFirstSeenTimePresent = true;
            tag.FirstSeenTime = new ImpinjTimestamp(new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc));
            tag.IsLastSeenTimePresent = true;
            tag.LastSeenTime = new ImpinjTimestamp(new DateTime(2026, 6, 7, 12, 0, 1, DateTimeKind.Utc));
            tag.IsSeenCountPresent = true;
            tag.TagSeenCount = 3;
        }
        report.Tags.Add(tag);
        return report;
    }

    private static TagReport CreateTagReport()
    {
        var report = (TagReport)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(TagReport));
        report.Tags = [];
        return report;
    }
}
