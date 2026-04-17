using BinanceBot.Api.Endpoints;
using BinanceBot.Api.Infrastructure;
using BinanceBot.Application;
using BinanceBot.Application.Abstractions;
using BinanceBot.Infrastructure;
using BinanceBot.Infrastructure.Persistence;
using Microsoft.Extensions.FileProviders;
using Serilog;

var contentRoot = Directory.GetCurrentDirectory();
var frontendRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", "Frontend"));

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot,
    WebRootPath = Directory.Exists(frontendRoot) ? frontendRoot : null,
});

builder.Host.UseSerilog((ctx, sp, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(sp)
       .Enrich.FromLogContext();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

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

await DatabaseInitializer.MigrateAsync(app.Services, CancellationToken.None);

app.Run();

namespace BinanceBot.Api
{
    public partial class Program { }
}
