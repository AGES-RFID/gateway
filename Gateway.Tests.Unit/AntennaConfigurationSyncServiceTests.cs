using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RfidGateway.Models;
using RfidGateway.Services;

namespace Gateway.Tests.Unit;

public class AntennaConfigurationSyncServiceTests
{
    private readonly IReaderService _readerService = Substitute.For<IReaderService>();
    private readonly IAntennaConfigurationClient _configurationClient = Substitute.For<IAntennaConfigurationClient>();
    private readonly AntennaConfigurationSyncService _service;

    public AntennaConfigurationSyncServiceTests()
    {
        _service = new AntennaConfigurationSyncService(
            BuildConfig(),
            Substitute.For<ILogger<AntennaConfigurationSyncService>>(),
            _readerService,
            _configurationClient);
    }

    [Fact]
    public async Task SyncOnceAsync_WhenReaderIsDisconnected_DoesNotCallBackend()
    {
        _readerService.IsConnected.Returns(false);

        await _service.SyncOnceAsync(CancellationToken.None);

        await _configurationClient.DidNotReceive()
            .CheckForDesiredConfigurationAsync(Arg.Any<AntennaConfiguration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncOnceAsync_WhenConfigurationIsCurrent_DoesNotApplyConfiguration()
    {
        var currentConfiguration = new AntennaConfiguration(23.5, -70.0);
        _readerService.IsConnected.Returns(true);
        _readerService.GetAntennaConfiguration().Returns(currentConfiguration);
        _configurationClient
            .CheckForDesiredConfigurationAsync(currentConfiguration, Arg.Any<CancellationToken>())
            .Returns((AntennaConfiguration?)null);

        await _service.SyncOnceAsync(CancellationToken.None);

        _readerService.DidNotReceive()
            .ApplyAntennaConfigurationToAll(Arg.Any<AntennaConfiguration>());
    }

    [Fact]
    public async Task SyncOnceAsync_WhenBackendReturnsDesiredConfiguration_AppliesConfigurationToAllAntennas()
    {
        var currentConfiguration = new AntennaConfiguration(20.0, -65.0);
        var desiredConfiguration = new AntennaConfiguration(25.0, -70.0);
        _readerService.IsConnected.Returns(true);
        _readerService.GetAntennaConfiguration().Returns(currentConfiguration);
        _configurationClient
            .CheckForDesiredConfigurationAsync(currentConfiguration, Arg.Any<CancellationToken>())
            .Returns(desiredConfiguration);

        await _service.SyncOnceAsync(CancellationToken.None);

        _readerService.Received(1)
            .ApplyAntennaConfigurationToAll(desiredConfiguration);
    }

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:AntennaConfigurationSyncIntervalSeconds"] = "60"
            })
            .Build();
}
