using Microsoft.AspNetCore.Mvc;
using RfidGateway.Models;
using RfidGateway.Services;
using OctaneAntennaStatus = Impinj.OctaneSdk.AntennaStatus;

namespace RfidGateway.Controllers;

[ApiController]
[Route("status")]
public sealed class StatusController : ControllerBase
{
    private readonly ReaderStatusService _status;
    private readonly ReaderService _reader;

    public StatusController(ReaderStatusService status, ReaderService reader)
    {
        _status = status;
        _reader = reader;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var connected = _status.IsConnected;
        var antennas = new List<AntennaStat>();

        if (connected)
        {
            try
            {
                var sdkStatus = _reader.QueryReaderStatus();
                foreach (OctaneAntennaStatus a in sdkStatus.Antennas)
                    antennas.Add(new AntennaStat(a.PortNumber, a.IsConnected));
            }
            catch { }
        }

        return Ok(new ReaderStatus(connected, antennas));
    }
}
