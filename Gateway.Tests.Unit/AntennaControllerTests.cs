using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RfidGateway.Controllers;
using RfidGateway.Models;
using RfidGateway.Services;

namespace Gateway.Tests.Unit;

public class AntennaControllerTests
{
    private readonly IReaderService _reader;
    private readonly AntennaController _controller;

    public AntennaControllerTests()
    {
        _reader = Substitute.For<IReaderService>();
        var logger = Substitute.For<ILogger<AntennaController>>();
        _controller = new AntennaController(_reader, logger);
    }

    // --- GetAll ---

    [Fact]
    public void GetAll_WhenReaderAvailable_ReturnsOkWithAntennaList()
    {
        var antennas = new List<AntennaResponse> { new((ushort)1, 23.5, -70.0, true) };
        _reader.GetAntennas().Returns(antennas);

        var result = _controller.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(antennas, ok.Value);
    }

    [Fact]
    public void GetAll_WhenReaderThrows_Returns503()
    {
        _reader.GetAntennas().Throws(new Exception("reader unavailable"));

        var result = _controller.GetAll();

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, obj.StatusCode);
    }

    // --- Get ---

    [Fact]
    public void Get_WhenPortBelowOne_ReturnsBadRequest()
    {
        var result = _controller.Get(0);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Get_WhenAntennaFound_ReturnsOk()
    {
        var antenna = new AntennaResponse((ushort)1, 23.5, -70.0, true);
        _reader.GetAntenna((ushort)1).Returns(antenna);

        var result = _controller.Get(1);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(antenna, ok.Value);
    }

    [Fact]
    public void Get_WhenAntennaNotFound_ReturnsNotFound()
    {
        _reader.GetAntenna(Arg.Any<ushort>()).Returns((AntennaResponse?)null);

        var result = _controller.Get(1);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void Get_WhenReaderThrows_Returns503()
    {
        _reader.GetAntenna(Arg.Any<ushort>()).Throws(new Exception("reader unavailable"));

        var result = _controller.Get(1);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, obj.StatusCode);
    }

    // --- Update ---

    [Fact]
    public void Update_WhenPortBelowOne_ReturnsBadRequest()
    {
        var result = _controller.Update(0, new AntennaUpdateRequest(23.5, null));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Update_WhenNoPowerOrSensitivity_ReturnsBadRequest()
    {
        var result = _controller.Update(1, new AntennaUpdateRequest(null, null));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Update_WhenAntennaFound_ReturnsNoContent()
    {
        _reader.UpdateAntenna(Arg.Any<ushort>(), Arg.Any<double?>(), Arg.Any<double?>()).Returns(true);

        var result = _controller.Update(1, new AntennaUpdateRequest(23.5, null));

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public void Update_WhenAntennaNotFound_ReturnsNotFound()
    {
        _reader.UpdateAntenna(Arg.Any<ushort>(), Arg.Any<double?>(), Arg.Any<double?>()).Returns(false);

        var result = _controller.Update(1, new AntennaUpdateRequest(23.5, null));

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void Update_WhenReaderThrows_Returns503()
    {
        _reader.UpdateAntenna(Arg.Any<ushort>(), Arg.Any<double?>(), Arg.Any<double?>())
               .Throws(new Exception("reader unavailable"));

        var result = _controller.Update(1, new AntennaUpdateRequest(23.5, null));

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, obj.StatusCode);
    }
}
