using System.Text.Json.Serialization;
using BinanceBot.Api.Endpoints;
using BinanceBot.Api.Infrastructure;
using BinanceBot.Application;
using BinanceBot.Application.Abstractions;
using BinanceBot.Infrastructure;
using BinanceBot.Infrastructure.Persistence;
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
        catch { }
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

await DatabaseInitializer.MigrateAsync(app.Services, CancellationToken.None);

app.Run();

namespace BinanceBot.Api
{
    public partial class Program { }
}
