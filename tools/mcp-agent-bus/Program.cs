using BinanceBot.AgentBus.Storage;
using BinanceBot.AgentBus.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

var traceDir = Environment.GetEnvironmentVariable("BUS_TRACE_DIR")
    ?? Path.Combine(Directory.GetCurrentDirectory(), ".ai-trace");
Directory.CreateDirectory(traceDir);

builder.Services.AddSingleton(new JsonlWriter(traceDir));
builder.Services.AddSingleton(new TaskStateStore(traceDir));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<AgentBusTools>();

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

await builder.Build().RunAsync();
