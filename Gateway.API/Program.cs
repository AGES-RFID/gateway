using RfidGateway.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IReaderService, ReaderService>();
builder.Services.AddSingleton<ReaderStatusService>();
builder.Services.AddHttpClient<IGatewayPublisher, GatewayPublisher>();
builder.Services.AddHttpClient<IAntennaConfigurationClient, AntennaConfigurationClient>();
builder.Services.AddHostedService<RfidReaderWorker>();
builder.Services.AddHostedService<AntennaConfigurationSyncService>();

var app = builder.Build();

await app.RunAsync();
