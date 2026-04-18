# Loop 3 — Decision: Sizing + Exit + Risk Reform (Operasyonel)

**Bound ADR:** [ADR-0011](../docs/adr/0011-equity-aware-sizing-and-risk-tracking.md)
**Yazar:** architect
**Tarih:** 2026-04-17
**Status:** Backend-dev'e devredilmeye hazir

> Bu dosya **operasyonel detay** tutar — kod sablonlari, dosya/satir listesi, agent zinciri, test planı. Normatif karar ADR-0011'de.

---

## 1. Backend-Dev Implementation Order

Her numara mantiksal commit olabilir; tek PR icinde 7 commit hedeflenir.

### Commit 1 — Slippage Config + DI

- **Yeni dosya:** `src/Infrastructure/Trading/Paper/PaperFillOptions.cs`
  ```csharp
  namespace BinanceBot.Infrastructure.Trading.Paper;

  public sealed record PaperFillOptions
  {
      public decimal FixedSlippagePct { get; init; } = 0.0005m;
  }
  ```
- **`src/Api/appsettings.json`** (yeni section ekle):
  ```json
  "PaperFill": {
    "FixedSlippagePct": 0.0005
  }
  ```
- **`src/Api/Program.cs`** veya `DependencyInjection.cs`:
  ```csharp
  services.Configure<PaperFillOptions>(configuration.GetSection("PaperFill"));
  ```
- **`src/Infrastructure/Trading/Paper/PaperFillSimulator.cs`** ctor'a `IOptions<PaperFillOptions> opts` ekle, field olarak sakla.

### Commit 2 — BUG-A Fix (PaperFillSimulator MARKET MinNotional)

`src/Infrastructure/Trading/Paper/PaperFillSimulator.cs:87` — `FillMarket` basina (BuildLevels'tan sonra):

```csharp
private PaperFillOutcome FillMarket(
    Order order, Instrument instrument, BookTicker bookTicker,
    OrderBookSnapshot? depthSnapshot, DateTimeOffset now)
{
    var levels = BuildLevels(order.Side, bookTicker, depthSnapshot);
    if (levels.Count == 0)
    {
        order.Reject("paper_no_liquidity", now);
        return new PaperFillOutcome(false, true, "no_liquidity", 0m, 0m, 0m);
    }

    // BUG-A FIX: MARKET orders need minNotional pre-check using top-of-book.
    var topPrice = levels[0].Price;
    var notionalEstimate = order.Quantity * topPrice;
    if (notionalEstimate < instrument.MinNotional)
    {
        var reason = $"filter_MIN_NOTIONAL_{notionalEstimate}<{instrument.MinNotional}";
        order.Reject(reason, now);
        return new PaperFillOutcome(false, true, reason, 0m, 0m, 0m);
    }

    // ...mevcut fills loop, slippage adjustment ile (Commit 3)...
}
```

### Commit 3 — Paper Slippage Apply

`PaperFillSimulator.FillMarket` fills loop'unda her level price'i ayarla:

```csharp
foreach (var lvl in levels)
{
    if (remaining <= 0m) break;
    var take = Math.Min(lvl.Qty, remaining);
    if (take <= 0m) continue;

    // Apply fixed slippage: BUY pays more, SELL receives less.
    var slipPrice = order.Side == OrderSide.Buy
        ? lvl.Price * (1m + _opts.FixedSlippagePct)
        : lvl.Price * (1m - _opts.FixedSlippagePct);

    fills.Add((slipPrice, take));
    remaining -= take;
}
```

`ComputeCommission` ve `realizedCash` hesaplari `slipPrice` uzerinden zaten yapilir (loop body degismez, sadece price kaynagi degisti).

### Commit 4 — PositionSizingService

- **Yeni dosya:** `src/Application/Abstractions/Trading/IPositionSizingService.cs`
  ```csharp
  namespace BinanceBot.Application.Abstractions.Trading;

  public interface IPositionSizingService
  {
      PositionSizingResult Calculate(PositionSizingInput input);
  }

  public sealed record PositionSizingInput(
      decimal Equity,
      decimal EntryPrice,
      decimal StopDistance,
      decimal RiskPct,
      decimal MaxPositionPct,
      decimal MinNotional,
      decimal StepSize,
      decimal MinQty,
      decimal SlippagePct);

  public sealed record PositionSizingResult(
      decimal Quantity,
      decimal NotionalEstimate,
      string? SkipReason);
  ```
- **Yeni dosya:** `src/Application/Sizing/PositionSizingService.cs`
  ```csharp
  using BinanceBot.Application.Abstractions.Trading;

  namespace BinanceBot.Application.Sizing;

  public sealed class PositionSizingService : IPositionSizingService
  {
      public PositionSizingResult Calculate(PositionSizingInput i)
      {
          if (i.Equity <= 0m)
              return new(0m, 0m, "equity_zero");
          if (i.EntryPrice <= 0m)
              return new(0m, 0m, "entry_invalid");

          var effectiveEntry = i.EntryPrice * (1m + i.SlippagePct);
          var riskAmount = i.Equity * i.RiskPct;
          var qtyByRisk = i.StopDistance > 0m
              ? riskAmount / i.StopDistance
              : decimal.MaxValue;
          var notionalCap = i.Equity * i.MaxPositionPct;
          var qtyByCap = notionalCap / effectiveEntry;
          var qtyRaw = Math.Min(qtyByRisk, qtyByCap);

          var qtyStepped = i.StepSize > 0m
              ? Math.Floor(qtyRaw / i.StepSize) * i.StepSize
              : qtyRaw;

          if (qtyStepped < i.MinQty)
              return new(0m, qtyStepped * effectiveEntry, "qty_below_min_qty");

          var notional = qtyStepped * effectiveEntry;
          if (notional < i.MinNotional)
              return new(0m, notional, "min_notional_floor");

          return new(qtyStepped, notional, null);
      }
  }
  ```
- **DI:** `services.AddSingleton<IPositionSizingService, PositionSizingService>();` (pure, stateless → singleton).

### Commit 5 — IEquitySnapshotProvider

- **Yeni dosya:** `src/Application/Abstractions/Trading/IEquitySnapshotProvider.cs`
  ```csharp
  using BinanceBot.Domain.Common;

  namespace BinanceBot.Application.Abstractions.Trading;

  public interface IEquitySnapshotProvider
  {
      Task<decimal> GetEquityAsync(TradingMode mode, CancellationToken ct);
  }
  ```
- **Yeni dosya:** `src/Infrastructure/Trading/EquitySnapshotProvider.cs`
  ```csharp
  using BinanceBot.Application.Abstractions;
  using BinanceBot.Application.Abstractions.Trading;
  using BinanceBot.Domain.Common;
  using Microsoft.EntityFrameworkCore;

  namespace BinanceBot.Infrastructure.Trading;

  public sealed class EquitySnapshotProvider : IEquitySnapshotProvider
  {
      private readonly IApplicationDbContext _db;

      public EquitySnapshotProvider(IApplicationDbContext db) => _db = db;

      public async Task<decimal> GetEquityAsync(TradingMode mode, CancellationToken ct)
      {
          if (mode == TradingMode.LiveMainnet) return 0m;

          var balance = await _db.VirtualBalances
              .AsNoTracking()
              .FirstOrDefaultAsync(b => b.Id == (int)mode, ct);
          if (balance is null) return 0m;

          // Paper: Equity field tracked by VirtualBalance.RecordEquity (ADR-0008 §8.4)
          // LiveTestnet: 0 until Binance account sync wired in (Loop 4+)
          return balance.Equity > 0m ? balance.Equity : balance.CurrentBalance;
      }
  }
  ```
- **DI:** `services.AddScoped<IEquitySnapshotProvider, EquitySnapshotProvider>();`

### Commit 6 — StrategySignalToOrderHandler Refactor (BUG-B Fix + Sizing Wiring)

**`src/Infrastructure/Strategies/StrategySignalToOrderHandler.cs`** — tam rewrite (mevcut 89 satir, yeni ~150 satir):

```csharp
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Application.Orders.Commands.PlaceOrder;
using BinanceBot.Application.Positions.Commands.ClosePosition;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.RiskProfiles;
using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.Strategies.Events;
using BinanceBot.Infrastructure.Trading.Paper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Strategies;

public sealed class StrategySignalToOrderHandler : INotificationHandler<StrategySignalEmittedEvent>
{
    private static readonly TradingMode[] AllModes =
        { TradingMode.Paper, TradingMode.LiveTestnet, TradingMode.LiveMainnet };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StrategySignalToOrderHandler> _logger;

    public StrategySignalToOrderHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<StrategySignalToOrderHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Handle(StrategySignalEmittedEvent n, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var mediator = sp.GetRequiredService<IMediator>();
        var db = sp.GetRequiredService<IApplicationDbContext>();
        var sizing = sp.GetRequiredService<IPositionSizingService>();
        var equityProvider = sp.GetRequiredService<IEquitySnapshotProvider>();
        var paperOpts = sp.GetRequiredService<IOptions<PaperFillOptions>>().Value;

        var barUnix = n.BarOpenTime.ToUnixTimeSeconds();

        // Branch: Exit signal -> ClosePositionCommand per mode
        if (n.Direction == StrategySignalDirection.Exit)
        {
            foreach (var mode in AllModes)
            {
                var cidPrefix = $"sig-{n.StrategyId}-{barUnix}";
                try
                {
                    var result = await mediator.Send(
                        new ClosePositionCommand(n.Symbol, n.StrategyId, mode, "exit_signal", cidPrefix), ct);
                    if (!result.IsSuccess && result.Status != Ardalis.Result.ResultStatus.NotFound)
                    {
                        _logger.LogWarning("Close rejected mode={Mode} {Symbol}: {Errors}",
                            mode, n.Symbol, string.Join(";", result.Errors));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Close exception mode={Mode} {Symbol}", mode, n.Symbol);
                }
            }
            return;
        }

        // Entry path
        var side = n.Direction == StrategySignalDirection.Long ? OrderSide.Buy : OrderSide.Sell;

        var instrument = await db.Instruments.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Symbol.Value == n.Symbol, ct);
        if (instrument is null)
        {
            _logger.LogWarning("Instrument not registered: {Symbol}", n.Symbol);
            return;
        }

        var ticker = await db.BookTickers.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Symbol.Value == n.Symbol, ct);
        if (ticker is null)
        {
            _logger.LogWarning("BookTicker missing: {Symbol}", n.Symbol);
            return;
        }

        var entry = side == OrderSide.Buy ? ticker.AskPrice : ticker.BidPrice;

        // SuggestedStopPrice from notification context — per evaluator outputs
        // For now reuse latest signal record (or accept evaluator passes it via event payload extension)
        // MVP: stopDistance from a follow-up DB read of latest StrategySignal.SuggestedStopPrice
        var latestSig = await db.StrategySignals.AsNoTracking()
            .Where(s => s.StrategyId == n.StrategyId && s.Symbol.Value == n.Symbol)
            .OrderByDescending(s => s.EmittedAt)
            .FirstOrDefaultAsync(ct);
        var stopDistance = latestSig?.SuggestedStopPrice is decimal stop
            ? Math.Abs(entry - stop)
            : 0m;

        foreach (var mode in AllModes)
        {
            var cid = $"sig-{n.StrategyId}-{barUnix}-{mode.ToCidSuffix()}";

            var risk = await db.RiskProfiles.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == RiskProfile.IdFor(mode), ct);
            if (risk is null)
            {
                _logger.LogWarning("Risk profile missing for mode {Mode}", mode);
                continue;
            }
            if (risk.CircuitBreakerStatus == CircuitBreakerStatus.Tripped)
            {
                _logger.LogInformation("CB tripped mode={Mode}, signal skipped {Cid}", mode, cid);
                continue;
            }

            var equity = await equityProvider.GetEquityAsync(mode, ct);
            if (equity <= 0m)
            {
                _logger.LogInformation("Equity <= 0 mode={Mode}, signal skipped {Cid}", mode, cid);
                continue;
            }

            var slip = mode == TradingMode.Paper ? paperOpts.FixedSlippagePct : 0m;
            var sizingResult = sizing.Calculate(new PositionSizingInput(
                equity, entry, stopDistance,
                risk.RiskPerTradePct, risk.MaxPositionSizePct,
                instrument.MinNotional, instrument.StepSize, instrument.MinQty,
                slip));

            if (sizingResult.Quantity == 0m)
            {
                _logger.LogInformation(
                    "Sizing skipped mode={Mode} {Symbol} reason={Reason} notional={Not}",
                    mode, n.Symbol, sizingResult.SkipReason, sizingResult.NotionalEstimate);
                continue;
            }

            var cmd = new PlaceOrderCommand(
                cid, n.Symbol, side.ToString(),
                OrderType.Market.ToString(), TimeInForce.Ioc.ToString(),
                sizingResult.Quantity, null, null, n.StrategyId, mode);

            try
            {
                var result = await mediator.Send(cmd, ct);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning("Order rejected mode={Mode} {Cid}: {Errors}",
                        mode, cid, string.Join(";", result.Errors));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order exception mode={Mode} {Cid}", mode, cid);
            }
        }
    }
}
```

> Not: `db.StrategySignals` IDbSet'i `IApplicationDbContext`'te yoksa `binance-expert`/`backend-dev` ya event payload'una `SuggestedStopPrice` ekler (Commit 6.1) ya da DB lookup eklenir. **Tercih:** `StrategySignalEmittedEvent`'i `(StrategyId, Symbol, Direction, BarOpenTime, SuggestedStopPrice)` olacak sekilde genislet — domain event yeniden yayilirsa no-op (geri uyumlu).

### Commit 6.1 — StrategySignalEmittedEvent SuggestedStopPrice

`src/Domain/Strategies/Events/StrategyEvents.cs`:
```csharp
public sealed record StrategySignalEmittedEvent(
    long StrategyId,
    string Symbol,
    StrategySignalDirection Direction,
    DateTimeOffset BarOpenTime,
    decimal? SuggestedStopPrice = null) : DomainEventBase;
```

`Strategy.EmitSignal(...)` veya raise edilen yere `signal.SuggestedStopPrice` parametresi gec; default null geriye uyumlu.

### Commit 7 — ClosePositionCommand

- **Yeni klasor:** `src/Application/Positions/Commands/ClosePosition/`
- **Dosya:** `ClosePositionCommand.cs`
  ```csharp
  using Ardalis.Result;
  using BinanceBot.Application.Abstractions;
  using BinanceBot.Application.Orders.Commands.PlaceOrder;
  using BinanceBot.Domain.Common;
  using BinanceBot.Domain.Orders;
  using BinanceBot.Domain.Positions;
  using MediatR;
  using Microsoft.EntityFrameworkCore;

  namespace BinanceBot.Application.Positions.Commands.ClosePosition;

  public sealed record ClosePositionCommand(
      string Symbol,
      long? StrategyId,
      TradingMode Mode,
      string Reason,
      string CorrelationCidPrefix) : IRequest<Result<ClosedPositionDto>>;

  public sealed record ClosedPositionDto(
      long PositionId, decimal RealizedPnl, string Reason);

  public sealed class ClosePositionCommandHandler
      : IRequestHandler<ClosePositionCommand, Result<ClosedPositionDto>>
  {
      private readonly IApplicationDbContext _db;
      private readonly IMediator _mediator;

      public ClosePositionCommandHandler(IApplicationDbContext db, IMediator mediator)
      {
          _db = db;
          _mediator = mediator;
      }

      public async Task<Result<ClosedPositionDto>> Handle(
          ClosePositionCommand req, CancellationToken ct)
      {
          var position = await _db.Positions
              .AsNoTracking()
              .FirstOrDefaultAsync(p =>
                  p.Symbol.Value == req.Symbol &&
                  p.Mode == req.Mode &&
                  p.Status == PositionStatus.Open, ct);
          if (position is null)
              return Result.NotFound($"No open position for {req.Symbol} mode={req.Mode}");

          var reverseSide = position.Side == PositionSide.Long ? OrderSide.Sell : OrderSide.Buy;
          var cid = $"{req.CorrelationCidPrefix}-x-{req.Mode.ToCidSuffix()}";

          var placeResult = await _mediator.Send(new PlaceOrderCommand(
              cid, req.Symbol, reverseSide.ToString(),
              OrderType.Market.ToString(), TimeInForce.Ioc.ToString(),
              position.Quantity, null, null, req.StrategyId, req.Mode), ct);

          if (!placeResult.IsSuccess)
              return Result<ClosedPositionDto>.Error(string.Join(";", placeResult.Errors));

          // Position close happens via OrderFilledPositionUpdater (§11.6) reactively.
          // Returned PnL is approximate (handler-level estimate), real close logged via PositionClosedEvent.
          return Result.Success(new ClosedPositionDto(
              position.Id, position.UnrealizedPnl, req.Reason));
      }
  }
  ```

### Commit 8 — OrderFilledPositionUpdater

- **Yeni dosya:** `src/Infrastructure/Positions/OrderFilledPositionUpdater.cs`
  ```csharp
  using BinanceBot.Application.Abstractions;
  using BinanceBot.Domain.Orders;
  using BinanceBot.Domain.Orders.Events;
  using BinanceBot.Domain.Positions;
  using MediatR;
  using Microsoft.EntityFrameworkCore;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Logging;

  namespace BinanceBot.Infrastructure.Positions;

  public sealed class OrderFilledPositionUpdater : INotificationHandler<OrderFilledEvent>
  {
      private readonly IServiceScopeFactory _scopeFactory;
      private readonly IClock _clock;
      private readonly ILogger<OrderFilledPositionUpdater> _logger;

      public OrderFilledPositionUpdater(
          IServiceScopeFactory scopeFactory,
          IClock clock,
          ILogger<OrderFilledPositionUpdater> logger)
      {
          _scopeFactory = scopeFactory;
          _clock = clock;
          _logger = logger;
      }

      public async Task Handle(OrderFilledEvent e, CancellationToken ct)
      {
          await using var scope = _scopeFactory.CreateAsyncScope();
          var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
          var now = _clock.UtcNow;

          var order = await db.Orders.AsNoTracking()
              .FirstOrDefaultAsync(o => o.Id == e.OrderId, ct);
          if (order is null) return;

          var openPos = await db.Positions
              .FirstOrDefaultAsync(p =>
                  p.Symbol == order.Symbol &&
                  p.Mode == order.Mode &&
                  p.Status == PositionStatus.Open, ct);

          var fillPrice = order.AverageFillPrice ?? order.Price ?? 0m;
          var fillQty = order.ExecutedQuantity;
          if (fillPrice <= 0m || fillQty <= 0m) return;

          if (openPos is null)
          {
              if (order.Side == OrderSide.Buy)
              {
                  var pos = Position.Open(order.Symbol, PositionSide.Long,
                      fillQty, fillPrice, order.StrategyId, order.Mode, now);
                  db.Positions.Add(pos);
              }
              else
              {
                  // Naked sell — track as Short open
                  var pos = Position.Open(order.Symbol, PositionSide.Short,
                      fillQty, fillPrice, order.StrategyId, order.Mode, now);
                  db.Positions.Add(pos);
              }
          }
          else
          {
              var sameSide = (openPos.Side == PositionSide.Long && order.Side == OrderSide.Buy)
                          || (openPos.Side == PositionSide.Short && order.Side == OrderSide.Sell);
              if (sameSide)
              {
                  openPos.AddFill(fillQty, fillPrice, now);
              }
              else
              {
                  // Reduce / close
                  if (fillQty >= openPos.Quantity)
                  {
                      openPos.Close(fillPrice, $"order_{order.ClientOrderId}", now);
                      var leftover = fillQty - openPos.Quantity;
                      if (leftover > 0m)
                      {
                          var newSide = order.Side == OrderSide.Buy ? PositionSide.Long : PositionSide.Short;
                          var flip = Position.Open(order.Symbol, newSide,
                              leftover, fillPrice, order.StrategyId, order.Mode, now);
                          db.Positions.Add(flip);
                      }
                  }
                  else
                  {
                      // Partial close: not yet supported in Position aggregate (only AddFill);
                      // log + approximate via mark-to-market
                      _logger.LogWarning(
                          "Partial close not yet supported pos={Pos} order={Cid}",
                          openPos.Id, order.ClientOrderId);
                  }
              }
          }

          await db.SaveChangesAsync(ct);
      }
  }
  ```
- **DI:** MediatR scan picks it up via `INotificationHandler<>` registration (mevcut `services.AddMediatR(...)` cagrisi yeterli).

### Commit 9 — RiskProfileSeeder

- **Yeni dosya:** `src/Infrastructure/Risk/RiskProfileSeeder.cs`
  ```csharp
  using BinanceBot.Application.Abstractions;
  using BinanceBot.Domain.Common;
  using BinanceBot.Domain.RiskProfiles;
  using Microsoft.EntityFrameworkCore;
  using Microsoft.Extensions.Hosting;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Options;

  namespace BinanceBot.Infrastructure.Risk;

  public sealed record RiskProfileDefaultsOptions
  {
      public decimal RiskPerTradePct { get; init; } = 0.01m;
      public decimal MaxPositionSizePct { get; init; } = 0.15m;
      public decimal MaxDrawdown24hPct { get; init; } = 0.05m;
      public decimal MaxDrawdownAllTimePct { get; init; } = 0.25m;
      public int MaxConsecutiveLosses { get; init; } = 3;
  }

  public sealed class RiskProfileSeeder : IHostedService
  {
      private readonly IServiceProvider _sp;
      private readonly IOptions<RiskProfileDefaultsOptions> _opts;
      private readonly IClock _clock;
      private readonly ILogger<RiskProfileSeeder> _logger;

      public RiskProfileSeeder(
          IServiceProvider sp,
          IOptions<RiskProfileDefaultsOptions> opts,
          IClock clock,
          ILogger<RiskProfileSeeder> logger)
      {
          _sp = sp;
          _opts = opts;
          _clock = clock;
          _logger = logger;
      }

      public async Task StartAsync(CancellationToken ct)
      {
          using var scope = _sp.CreateScope();
          var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
          var o = _opts.Value;
          var now = _clock.UtcNow;

          foreach (var mode in new[] {
              TradingMode.Paper, TradingMode.LiveTestnet, TradingMode.LiveMainnet })
          {
              var id = RiskProfile.IdFor(mode);
              var existing = await db.RiskProfiles.FirstOrDefaultAsync(r => r.Id == id, ct);
              if (existing is not null) continue;

              var profile = RiskProfile.CreateDefault(mode, now);
              profile.UpdateLimits(
                  o.RiskPerTradePct, o.MaxPositionSizePct,
                  o.MaxDrawdown24hPct, o.MaxDrawdownAllTimePct,
                  o.MaxConsecutiveLosses, now);
              db.RiskProfiles.Add(profile);
              _logger.LogInformation("Seeded RiskProfile mode={Mode}", mode);
          }
          await db.SaveChangesAsync(ct);
      }

      public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
  }
  ```
- **`appsettings.json`:**
  ```json
  "RiskProfile": {
    "Defaults": {
      "RiskPerTradePct": 0.01,
      "MaxPositionSizePct": 0.15,
      "MaxDrawdown24hPct": 0.05,
      "MaxDrawdownAllTimePct": 0.25,
      "MaxConsecutiveLosses": 3
    }
  }
  ```
- **DI:**
  ```csharp
  services.Configure<RiskProfileDefaultsOptions>(configuration.GetSection("RiskProfile:Defaults"));
  services.AddHostedService<RiskProfileSeeder>();
  ```
- **Sira:** `RiskProfileSeeder` `KlineBackfillWorker`'dan once veya bagimsiz baslamali; hosted service registration sirasi onemli degil cunku RiskProfile yokken sizing zaten `if (risk is null) continue` der.

---

## 2. Test Plan

### Unit (Domain.Tests / Application.Tests)
- **`PositionSizingServiceTests`** (en az 12 senaryo):
  - happy_btc_with_stop / happy_bnb_no_stop / happy_xrp_step_align
  - skip_min_notional_floor / skip_qty_below_min_qty / skip_equity_zero
  - cap_clamps_qty_when_risk_too_large
  - slippage_increases_effective_price
  - step_floor_truncates_correctly
  - zero_stop_distance_falls_back_to_cap
- **`PaperFillSimulator_MarketMinNotionalTests`** — BUG-A regression.
- **`StrategySignalToOrderHandlerTests`** — entry happy path 3 mode + exit path 3 mode + CB tripped skip + equity 0 skip.

### Integration (Api.IntegrationTests)
- Boot + seeder → `RiskProfile.Id IN (1,2,3)` rows mevcut.
- `POST /api/internal/test-emit-signal` (test-only endpoint) Long → `Order` 3 row + `Position` 1 row Paper'da.
- Exit signal sonrasi `Position.Status == Closed`, `RiskProfile.RealizedPnl24h` updated.

### Playwright (tester)
- Dashboard'da 3 mode rozeti hala goruluyor.
- Manuel sinyal tetikleme sonrasi `Open Positions` listesinde 1 satir, 3 mode icin 3 order row.
- Exit sinyali (zaman bekle veya manuel) → `Closed Positions` 1 satir, `RealizedPnl` non-zero.

---

## 3. Reviewer Kontrol Listesi

1. **Dependency rule:** `IPositionSizingService` Application/Abstractions; impl Application/Sizing — Infra'ya sızmadi. `IEquitySnapshotProvider` Application/Abstractions, impl Infrastructure — uyumlu.
2. **Domain saf:** `Position`, `RiskProfile`, `Instrument`, `Strategy` davranislari degismedi (sadece `StrategySignalEmittedEvent` payload extension).
3. **Result<T> kullanimi:** `ClosePositionCommandHandler` `Result.NotFound`/`Result.Error`/`Result.Success` doner — exception-for-flow yok.
4. **Anemic kontrolu:** Sizing Application servisi pure; `RiskProfile.RecordTradeOutcome` davranisi aggregate icinde kalir.
5. **`AsNoTracking()`:** Read path'lerde mevcut (handler'da iki yerde kullanildi).
6. **CorrelationId:** PlaceOrderCommand handler `_correlation` zaten kullaniyor; ClosePosition fan-out'unda `cidPrefix` cid suffix patterni korur (ADR-0008 §8.2).
7. **Idempotency:** Cid suffix `-x-{mode}` exit icin; entry `-{mode}` mevcut. UNIQUE(ClientOrderId, Mode) ihlali yok.
8. **CB skip log:** `LogInformation` seviyesinde, ayri SystemEvent'e gerek yok (ileride dashboard isterse `trade.cb_skipped` event).
9. **Slippage sadece Paper:** `mode == Paper ? opts.FixedSlippagePct : 0m` — testnet/mainnet branch'lerinde `0m`.
10. **BUG-A test:** `PaperFillSimulator_MarketMinNotionalTests.Below5UsdtNotional_Rejects` zorunlu.
11. **BUG-B test:** `StrategySignalToOrderHandlerTests.NoHardcodedQuantity_UsesSizingResult` zorunlu (mock `IPositionSizingService` `0.05m` return → cmd.Quantity == 0.05m).
12. **ADR-0010 uyumluluk:** Backfill `KlineClosedEvent` susturuldugu icin `StrategySignalEmittedEvent` backfill sirasinda raise olmaz → fan-out tetiklenmez. Sizing/exit hep WS-driven gercek bar'larda.

---

## 4. Agent Zinciri

```
architect (bu doc + ADR-0011) ✓
  ↓
backend-dev (Commit 1-9, 7 dosya yeni + 2 dosya update)
  ↓
reviewer (dependency rule + Result + anemic + idempotency + cid kontrolu)
  ↓
tester (Playwright dashboard 3 mode + open/closed positions)
  ↓
PM (commit + push + Loop 3 cycle baslat)
```

---

## 5. ADR Cakisma Notu

ADR-0011 `Cakisma Kontrolu` bolumu (ADR-0011 sonu): ADR-0005/0006/0008/0009/0010 ile uyumlu. Tek dikkat:

- **ADR-0005 §5.3 server-side stop** — Loop 3'te yapilmaz; `ADR-0011 §11.12` bunu **acik kabul** eder. Loop 4 ADR-0012 (Spot OCO) ile kapatilir.
- **ADR-0005 §5.2 MaxPositionSizePct default %10** — ADR-0011 §11.8 bunu **%15**'e cikariyor (research-sizing.md gerekce: 100 USDT portfoyde min 5 USDT notional uyumu icin); `RiskProfile.UpdateLimits` cap `0.20` izin verir → uyumlu, ADR-0005 spirit ihlali yok.

---

## 6. Done Definition (PM checkpoint icin)

- [ ] ADR-0011 yazildi ve commit'lendi.
- [ ] `loop_3/decision-sizing.md` yazildi.
- [ ] backend-dev 9 commit + 1 PR.
- [ ] Reviewer "ready" verdi.
- [ ] Tester Playwright "3 mode rozeti + open + closed positions + risk-profile.realizedPnl24h non-zero" kanitladi.
- [ ] DB drop + Loop 3 normal 4h cycle basladi.
