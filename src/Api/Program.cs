using System.Text.Json.Serialization;
using BinanceBot.Api.Endpoints;
using BinanceBot.Api.Infrastructure;
using BinanceBot.Application;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.SystemEvents.Events;
using BinanceBot.Infrastructure;
using BinanceBot.Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.FileProviders;
using Serilog;

static string? ResolveFrontendRoot()
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "..", "Frontend"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Frontend"),
        Path.Combine(AppContext.BaseDirectory, "Frontend"),
        Path.Combine(Directory.GetCurrentDirectory(), "src", "Frontend"),
        Path.Combine(Directory.GetCurrentDirectory(), "Frontend"),
    };
    foreach (var c in candidates)
    {
        try
        {
            var full = Path.GetFullPath(c);
            if (Directory.Exists(full) && File.Exists(Path.Combine(full, "index.html")))
            {
                return full;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Frontend root probe failed for {Candidate}", c);
        }
    }
    return null;
}

var frontendRoot = ResolveFrontendRoot();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = frontendRoot,
});

Console.WriteLine($"[boot] frontend root: {(frontendRoot ?? "(not found — UI disabled)")}");

builder.Host.UseSerilog((ctx, sp, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(sp)
       .Enrich.FromLogContext();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddScoped<ICorrelationIdAccessor, CorrelationIdAccessor>();
builder.Services.AddSingleton<AdminAuthFilter>();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapKlineEndpoints();
app.MapMarketEndpoints();
app.MapInstrumentEndpoints();
app.MapOrderEndpoints();
app.MapPositionEndpoints();
app.MapStrategyEndpoints();
app.MapRiskEndpoints();
app.MapSystemEndpoints();
app.MapBacktestEndpoints();
app.MapBalanceEndpoints();
app.MapPortfolioEndpoints();

await DatabaseInitializer.MigrateAsync(app.Services, CancellationToken.None);

// ADR-0016 §16.9.6 — AppStarted/AppStopping publish is handled by
// AppLifecycleHostedService (IHostedService.StartAsync/StopAsync) so the
// shutdown path runs under the framework's async drain with no .Wait() /
// .GetAwaiter().GetResult() (CLAUDE.md rule 9).

app.Run();

namespace BinanceBot.Api
{
    public partial class Program { }
}
