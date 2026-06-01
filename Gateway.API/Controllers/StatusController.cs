using Microsoft.AspNetCore.Mvc;
using RfidGateway.Models;
using RfidGateway.Services;

namespace RfidGateway.Controllers;

[ApiController]
[Route("status")]
public sealed class StatusController : ControllerBase
{
    private readonly ReaderStatusService _status;
    private readonly IReaderService _reader;
    private readonly ILogger<StatusController> _logger;

    public StatusController(ReaderStatusService status, IReaderService reader, ILogger<StatusController> logger)
    {
        _status = status;
        _reader = reader;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var connected = _status.IsConnected;
        IReadOnlyList<AntennaStat> antennas = [];

        if (connected)
        {
            try
            {
                antennas = _reader.GetAntennaStats();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query antenna status.");
                connected = false;
            }
        }

        return Ok(new ReaderStatus(connected, antennas));
    }
}
