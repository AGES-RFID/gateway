using System.Text.Json;
using RfidGateway.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IReaderService, ReaderService>();
builder.Services.AddSingleton<ReaderStatusService>();
builder.Services.AddHttpClient<IGatewayPublisher, GatewayPublisher>();
builder.Services.AddHostedService<RfidReaderWorker>();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();

app.MapControllers();

await app.RunAsync();
