using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RfidGateway.Controllers;
using RfidGateway.Models;
using RfidGateway.Services;

namespace Gateway.Tests.Unit;

public class StatusControllerTests
{
    private readonly ReaderStatusService _statusService;
    private readonly IReaderService _reader;
    private readonly StatusController _controller;

    public StatusControllerTests()
    {
        _statusService = new ReaderStatusService();
        _reader = Substitute.For<IReaderService>();
        var logger = Substitute.For<ILogger<StatusController>>();
        _controller = new StatusController(_statusService, _reader, logger);
    }

    [Fact]
    public void Get_WhenReaderDisconnected_ReturnsOkWithDisconnectedStatus()
    {
        var result = _controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result);
        var status = Assert.IsType<ReaderStatus>(ok.Value);
        Assert.False(status.Connected);
        Assert.Empty(status.Antennas);
    }

    [Fact]
    public void Get_WhenReaderConnected_ReturnsOkWithAntennaStats()
    {
        _statusService.SetConnected(true);
        var stats = new List<AntennaStat> { new((ushort)1, true), new((ushort)2, false) };
        _reader.GetAntennaStats().Returns(stats);

        var result = _controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result);
        var status = Assert.IsType<ReaderStatus>(ok.Value);
        Assert.True(status.Connected);
        Assert.Equal(2, status.Antennas.Count);
    }

    [Fact]
    public void Get_WhenConnectedAndReaderQueryFails_ReturnsOkWithDisconnectedStatus()
    {
        _statusService.SetConnected(true);
        _reader.GetAntennaStats().Throws(new Exception("reader unavailable"));

        var result = _controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result);
        var status = Assert.IsType<ReaderStatus>(ok.Value);
        Assert.False(status.Connected);
        Assert.Empty(status.Antennas);
    }
}
