using Microsoft.AspNetCore.Mvc;
using RfidGateway.Models;
using RfidGateway.Services;

namespace RfidGateway.Controllers;

[ApiController]
[Route("antennas")]
public sealed class AntennaController : ControllerBase
{
    private readonly IReaderService _reader;
    private readonly ILogger<AntennaController> _logger;

    public AntennaController(IReaderService reader, ILogger<AntennaController> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        try
        {
            return Ok(_reader.GetAntennas());
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
            var antenna = _reader.GetAntenna((ushort)portNumber);
            return antenna is null ? NotFound() : Ok(antenna);
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
        if (!request.Power.HasValue && !request.Sensitivity.HasValue)
            return BadRequest("At least one field (power or sensitivity) must be provided.");
        try
        {
            var found = _reader.UpdateAntenna((ushort)portNumber, request.Power, request.Sensitivity);
            return found ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update antenna {Port}.", portNumber);
            return StatusCode(503, "Reader is not available.");
        }
    }
}
