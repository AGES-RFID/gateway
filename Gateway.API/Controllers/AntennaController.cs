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

    public AntennaController(ReaderService reader) => _reader = reader;

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
        catch (Exception)
        {
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
        catch (Exception)
        {
            return StatusCode(503, "Reader is not available.");
        }
    }

    [HttpPut("{portNumber:int}")]
    public IActionResult Update(int portNumber, [FromBody] AntennaUpdateRequest request)
    {
        if (portNumber < 1) return BadRequest("Port number must be positive.");
        try
        {
            var settings = _reader.QuerySettings();
            var antenna = settings.Antennas.GetAntenna((ushort)portNumber);
            if (antenna is null) return NotFound();

            if (request.TxPowerInDbm.HasValue)        antenna.TxPowerInDbm       = request.TxPowerInDbm.Value;
            if (request.RxSensitivityInDbm.HasValue)  antenna.RxSensitivityInDbm = request.RxSensitivityInDbm.Value;

            _reader.ApplySettingsWithoutReset(settings);
            return NoContent();
        }
        catch (Exception)
        {
            return StatusCode(503, "Reader is not available.");
        }
    }

    private static AntennaResponse ToResponse(AntennaConfig config, Status status)
    {
        OctaneAntennaStatus? antennaStatus = null;
        try { antennaStatus = status.Antennas.GetAntenna(config.PortNumber); } catch { }

        return new AntennaResponse(
            PortNumber: config.PortNumber,
            TxPowerInDbm: config.TxPowerInDbm,
            RxSensitivityInDbm: config.RxSensitivityInDbm,
            IsConnected: antennaStatus?.IsConnected ?? false
        );
    }
}
