using System.Text.Json;
using RfidGateway.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ReaderService>();
builder.Services.AddSingleton<ReaderStatusService>();
builder.Services.AddHostedService<RfidReaderWorker>();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();

app.MapControllers();

await app.RunAsync();
