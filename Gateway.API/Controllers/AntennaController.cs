using Impinj.OctaneSdk;
using Microsoft.AspNetCore.Mvc;
using RfidGateway.Models;
using RfidGateway.Services;
using OctaneAntennaStatus = Impinj.OctaneSdk.AntennaStatus;

namespace RfidGateway.Controllers;

[ApiController]
[Route("antennas")]
public sealed class AntennaController : ControllerBase
{
    private readonly ReaderService _reader;
    private readonly ILogger<AntennaController> _logger;

    public AntennaController(ReaderService reader, ILogger<AntennaController> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        try
        {
            var settings = _reader.QuerySettings();
            var status = _reader.QueryReaderStatus();
            var results = new List<AntennaResponse>();
            foreach (AntennaConfig config in settings.Antennas)
                results.Add(ToResponse(config, status));
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query reader.");
            return StatusCode(503, "Reader is not available.");
        }
    }

    [HttpGet("{portNumber:int}")]
    public IActionResult Get(int portNumber)
    {
        if (portNumber < 1) return BadRequest("Port number must be positive.");
        try
        {
            var settings = _reader.QuerySettings();
            var status = _reader.QueryReaderStatus();
            var config = settings.Antennas.GetAntenna((ushort)portNumber);
            return config is null ? NotFound() : Ok(ToResponse(config, status));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query reader.");
            return StatusCode(503, "Reader is not available.");
        }
    }

    [HttpPut("{portNumber:int}")]
    public IActionResult Update(int portNumber, [FromBody] AntennaUpdateRequest request)
    {
        if (portNumber < 1) return BadRequest("Port number must be positive.");
        if (!request.TxPower.HasValue && !request.RxSensitivity.HasValue)
            return BadRequest("At least one field (txPower or rxSensitivity) must be provided.");
        try
        {
            var found = _reader.UpdateAntenna((ushort)portNumber, antenna =>
            {
                if (request.TxPower.HasValue)       antenna.TxPowerInDbm       = request.TxPower.Value;
                if (request.RxSensitivity.HasValue) antenna.RxSensitivityInDbm = request.RxSensitivity.Value;
            });
            return found ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update antenna {Port}.", portNumber);
            return StatusCode(503, "Reader is not available.");
        }
    }

    private AntennaResponse ToResponse(AntennaConfig config, Status status)
    {
        OctaneAntennaStatus? antennaStatus = null;
        try { antennaStatus = status.Antennas.GetAntenna(config.PortNumber); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not get status for antenna {Port}.", config.PortNumber); }

        return new AntennaResponse(
            Port: config.PortNumber,
            TxPower: config.TxPowerInDbm,
            RxSensitivity: config.RxSensitivityInDbm,
            Connected: antennaStatus?.IsConnected ?? false
        );
    }
}
