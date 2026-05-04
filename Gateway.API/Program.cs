using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RfidGateway.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<RfidReaderWorker>();

var app = builder.Build();
await app.RunAsync();