using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Binance.Workers;

public sealed class SymbolFiltersRefresher : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<BinanceOptions> _options;
    private readonly ILogger<SymbolFiltersRefresher> _logger;

    public SymbolFiltersRefresher(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<BinanceOptions> options,
        ILogger<SymbolFiltersRefresher> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SymbolFiltersRefresher cycle failed");
            }

            try
            {
                await Task.Delay(RefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var symbols = _options.CurrentValue.Symbols;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var market = scope.ServiceProvider.GetRequiredService<IBinanceMarketData>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var dtos = await market.GetExchangeInfoAsync(symbols, ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var dto in dtos)
        {
            var symbolVo = Symbol.From(dto.Symbol);
            var existing = await ((DbSet<Instrument>)db.Instruments)
                .FirstOrDefaultAsync(i => i.Symbol == symbolVo, ct);

            var status = MapStatus(dto.Status);

            if (existing is null)
            {
                var created = Instrument.Create(
                    symbolVo, dto.BaseAsset, dto.QuoteAsset, status,
                    dto.TickSize, dto.StepSize, dto.MinNotional,
                    dto.MinQty, dto.MaxQty, now);
                db.Instruments.Add(created);
                _logger.LogInformation("Instrument registered: {Symbol}", dto.Symbol);
            }
            else
            {
                existing.UpdateFilters(
                    dto.TickSize, dto.StepSize, dto.MinNotional,
                    dto.MinQty, dto.MaxQty, status, now);
            }
        }

        var affected = await db.SaveChangesAsync(ct);
        if (affected > 0)
        {
            _logger.LogInformation("Instrument filter refresh affected {Affected} rows", affected);
        }
    }

    private static InstrumentStatus MapStatus(string raw) => raw.ToUpperInvariant() switch
    {
        "TRADING" => InstrumentStatus.Trading,
        "HALT" => InstrumentStatus.Halt,
        "BREAK" => InstrumentStatus.Break,
        "PRE_TRADING" => InstrumentStatus.PreTrading,
        "POST_TRADING" => InstrumentStatus.PostTrading,
        "END_OF_DAY" => InstrumentStatus.EndOfDay,
        "AUCTION_MATCH" => InstrumentStatus.AuctionMatch,
        _ => InstrumentStatus.Halt,
    };
}
