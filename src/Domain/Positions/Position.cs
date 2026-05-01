using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions.Events;
using BinanceBot.Domain.ValueObjects;

namespace BinanceBot.Domain.Positions;

public sealed class Position : AggregateRoot<long>
{
    public Symbol Symbol { get; private set; } = default!;
    public PositionSide Side { get; private set; }
    public PositionStatus Status { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal AverageEntryPrice { get; private set; }
    public decimal? ExitPrice { get; private set; }
    public decimal MarkPrice { get; private set; }

    /// <summary>
    /// Optional risk-management stop level captured at open time (ADR-0012 §12.4).
    /// Persisted alongside the aggregate so <see cref="Infrastructure.Trading.StopLossMonitorService"/>
    /// can soft-trigger an exit when mark price crosses this threshold without depending on
    /// any in-process cache.
    /// </summary>
    public decimal? StopPrice { get; private set; }

    /// <summary>
    /// Optional profit-taking level captured at open time (Loop 10 fix — realize gains).
    /// Persisted alongside the aggregate so <c>TakeProfitMonitorService</c> can soft-trigger
    /// an exit when mark price reaches this threshold. Symmetric to <see cref="StopPrice"/>:
    /// long positions exit when price &gt;= TakeProfit; short positions when price &lt;= TakeProfit.
    /// Stored independently — strategies compute TP per evaluator (ATR multiple, mean band, etc.).
    /// </summary>
    public decimal? TakeProfit { get; private set; }

    /// <summary>
    /// Optional pattern-based time stop (ADR-0014 §14.5). When set,
    /// <see cref="Infrastructure.Trading.StopLossMonitorService"/> dispatches a
    /// <c>CloseSignalPositionCommand</c> when <c>Now - OpenedAt &gt; MaxHoldDuration</c>.
    /// Persisted as <c>MaxHoldDurationSeconds</c> (long?) — null means no time stop.
    /// Pattern detectors return per-pattern <c>MaxHoldBars</c>; the handler converts
    /// it to <see cref="TimeSpan"/> via 1 bar = 1 minute (BinanceBot 1m bars).
    /// </summary>
    public TimeSpan? MaxHoldDuration { get; private set; }

    public decimal UnrealizedPnl { get; private set; }
    public decimal RealizedPnl { get; private set; }

    /// <summary>
    /// ADR-0020 §20.6 — entry-leg commission expressed in quote (USDT) currency.
    /// Accumulates across partial fills via <see cref="AddFill"/>. Never negative.
    /// Used by <see cref="Close"/> to compute net <see cref="RealizedPnl"/>.
    /// </summary>
    public decimal EntryCommission { get; private set; }

    /// <summary>
    /// ADR-0020 §20.6 — exit-leg commission expressed in quote (USDT) currency.
    /// Captured on <see cref="Close"/>. Zero while the position is open. Never negative.
    /// </summary>
    public decimal ExitCommission { get; private set; }

    /// <summary>
    /// Loop 75 — break-even SL move audit timestamp (idempotency anchor). Null
    /// while the position has never had its stop pulled to break-even; once
    /// <see cref="MoveStopToBreakEven"/> succeeds the field is set and any further
    /// invocation is a defensive no-op (caller does not need to read this field —
    /// the method itself enforces "apply once").
    ///
    /// Spec (binance-expert): timestop slow-bleed pattern (Loop 72-73 t90 +UPnl
    /// → t120 −UPnl roundtrip) çözülür. Service hook UPnl &gt;= entry × (1 + BE_TriggerPct)
    /// görür ve <c>StopPrice</c>'ı entry × (1 + BE_OffsetPct)'e taşır — bir daha tetiklemez.
    /// </summary>
    public DateTimeOffset? BreakEvenAppliedAt { get; private set; }

    /// <summary>
    /// Loop 76 — trailing-stop running peak. Sadece BE applied sonrası
    /// (<see cref="BreakEvenAppliedAt"/> non-null) değerlendirilir; ondan önce
    /// her tick mark'ın yeni high olup olmadığını kontrol etmek anlamsız (BE move
    /// trailing'i "arm" eder).
    ///
    /// Default 0 — yeni open position için yatırılır. İlk
    /// <see cref="UpdatePeakAndCheckTrailing"/> çağrısında BE applied + mark &gt; 0
    /// koşulu mutlaka sağlanır (mark &gt; PeakMarkPrice = 0), peak hemen
    /// güncellenir. Long-only kontrat (KMS Long-only spot); Short eklenirse
    /// trough mark'a simetrik olarak burada genişler.
    ///
    /// Spec (binance-expert): BE move sonra trailing aktif. trailingStop =
    /// PeakMarkPrice × (1 − TrailPct). Mark &lt; trailingStop görüldüğünde
    /// <see cref="PositionTrailingExitTriggeredEvent"/> raise edilir; aggregate
    /// kendisi close olmaz, MarkToMarketWorker dispatch CloseSignalPositionCommand.
    /// </summary>
    public decimal PeakMarkPrice { get; private set; }

    public long? StrategyId { get; private set; }
    public TradingMode Mode { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Position() { }

    public static Position Open(
        Symbol symbol,
        PositionSide side,
        decimal quantity,
        decimal entryPrice,
        decimal? stopPrice,
        long? strategyId,
        TradingMode mode,
        DateTimeOffset now,
        decimal entryCommission = 0m,
        decimal? takeProfit = null,
        TimeSpan? maxHoldDuration = null)
    {
        if (quantity <= 0m)
        {
            throw new DomainException("Position quantity must be positive.");
        }
        if (entryPrice <= 0m)
        {
            throw new DomainException("Entry price must be positive.");
        }
        if (stopPrice is decimal s && s <= 0m)
        {
            throw new DomainException("Stop price must be positive when set.");
        }
        if (takeProfit is decimal tp && tp <= 0m)
        {
            throw new DomainException("Take-profit must be positive when set.");
        }
        if (maxHoldDuration is TimeSpan d && d <= TimeSpan.Zero)
        {
            throw new DomainException("Max hold duration must be positive when set.");
        }
        if (entryCommission < 0m)
        {
            // ADR-0020 §20.6 — negative fees are never valid (Binance fee credit is
            // not modelled). Admin-open / test paths pass 0m; paper simulator passes
            // the quote-equivalent fee from PaperFillOutcome.QuoteCommissionTotal.
            throw new DomainException("Entry commission must be non-negative.");
        }

        var position = new Position
        {
            Symbol = symbol,
            Side = side,
            Status = PositionStatus.Open,
            Quantity = quantity,
            AverageEntryPrice = entryPrice,
            MarkPrice = entryPrice,
            StopPrice = stopPrice,
            TakeProfit = takeProfit,
            MaxHoldDuration = maxHoldDuration,
            EntryCommission = entryCommission,
            ExitCommission = 0m,
            StrategyId = strategyId,
            Mode = mode,
            OpenedAt = now,
            UpdatedAt = now,
        };

        position.RaiseDomainEvent(new PositionOpenedEvent(
            position.Id, symbol.Value, side, entryPrice, quantity, mode));
        return position;
    }

    public void AddFill(decimal addQuantity, decimal addPrice, DateTimeOffset now, decimal addCommission = 0m)
    {
        EnsureOpen();
        if (addQuantity <= 0m || addPrice <= 0m)
        {
            throw new DomainException("Quantity and price must be positive.");
        }
        if (addCommission < 0m)
        {
            throw new DomainException("Fill commission must be non-negative.");
        }

        var totalCost = (AverageEntryPrice * Quantity) + (addPrice * addQuantity);
        Quantity += addQuantity;
        AverageEntryPrice = totalCost / Quantity;
        // ADR-0020 §20.6 — cumulative fee ledger (not weighted average).
        EntryCommission += addCommission;
        UpdatedAt = now;

        RaiseDomainEvent(new PositionUpdatedEvent(Id, Symbol.Value, Quantity, AverageEntryPrice));
    }

    public void MarkToMarket(decimal markPrice, DateTimeOffset now)
    {
        EnsureOpen();
        if (markPrice <= 0m)
        {
            throw new DomainException("Mark price must be positive.");
        }

        MarkPrice = markPrice;
        UnrealizedPnl = Side == PositionSide.Long
            ? (markPrice - AverageEntryPrice) * Quantity
            : (AverageEntryPrice - markPrice) * Quantity;
        UpdatedAt = now;

        RaiseDomainEvent(new PositionMarkedToMarketEvent(Id, Symbol.Value, markPrice, UnrealizedPnl));
    }

    /// <summary>
    /// Loop 75 — break-even SL move. Caller (typically
    /// <see cref="Infrastructure.Positions.MarkToMarketWorker"/>) decides triggering
    /// (mark price ≥ entry × (1 + BE_TriggerPct)); domain only enforces invariants
    /// and idempotency.
    /// </summary>
    /// <param name="newStop">Target stop level (must improve current stop for Long;
    /// pre-computed by caller as entry × (1 + BE_OffsetPct)).</param>
    /// <param name="asOf">Clock-driven event time.</param>
    /// <returns>
    /// <see cref="MoveStopResult.Applied"/> on first successful move,
    /// <see cref="MoveStopResult.AlreadyApplied"/> if BE was already applied
    /// (defensive no-op so the worker can stay idempotent without an explicit guard),
    /// <see cref="MoveStopResult.NotImproving"/> if the requested stop is not
    /// strictly better than the current one (Long: not higher, Short: not lower).
    /// </returns>
    /// <remarks>
    /// Throws <see cref="DomainException"/> only on hard invariant violations
    /// (closed position, non-positive stop). Idempotency / no-improve are
    /// expected paths — Result-style enum return keeps the caller exception-free.
    /// </remarks>
    public MoveStopResult MoveStopToBreakEven(decimal newStop, DateTimeOffset asOf)
    {
        EnsureOpen();
        if (newStop <= 0m)
        {
            throw new DomainException("Break-even stop price must be positive.");
        }

        if (BreakEvenAppliedAt is not null)
        {
            return MoveStopResult.AlreadyApplied;
        }

        // Long: yeni stop entry üstüne taşınıyor → mevcut stop (varsa) altında olmalı.
        // Short: yeni stop entry altına taşınıyor → mevcut stop (varsa) üstünde olmalı.
        // null current stop (StopPrice never set) → her improvement geçerli.
        if (StopPrice is decimal current)
        {
            var improves = Side == PositionSide.Long
                ? newStop > current
                : newStop < current;
            if (!improves)
            {
                return MoveStopResult.NotImproving;
            }
        }

        var previous = StopPrice ?? 0m;
        StopPrice = newStop;
        BreakEvenAppliedAt = asOf;
        UpdatedAt = asOf;

        RaiseDomainEvent(new PositionStopMovedEvent(
            Id, Symbol.Value, previous, newStop, "break_even_move"));

        return MoveStopResult.Applied;
    }

    /// <summary>
    /// Loop 76 — trailing-stop tick. Caller (MarkToMarketWorker) BE move'dan
    /// SONRA çağırır; BE applied değilse method dormant kalır (NotEligible).
    /// İki davranış: (1) yeni high → <see cref="PeakMarkPrice"/> güncellenir,
    /// (2) mark trail seviyesinin altına düştü → exit event raise.
    ///
    /// Long-only kontrat: peak = max(prev, mark); trailingStop =
    /// peak × (1 − trailPct); mark &lt; trailingStop → exit. Short eklenirse
    /// trough mark + (1 + trailPct) yönlü simetri burada genişler.
    /// </summary>
    /// <param name="markPrice">Mid-price tick (positive); aggregate doğrular.</param>
    /// <param name="trailPct">Trail genişliği oransal (0.0015 = %0.15).
    /// Pozitif olmalı; aksi halde aggregate <see cref="DomainException"/> atar
    /// (config invariant ihlali).</param>
    /// <param name="asOf">Clock-driven event time. Peak güncellenirse
    /// <see cref="UpdatedAt"/>'a yazılır.</param>
    /// <returns>
    /// <see cref="TrailingResult.NotEligible"/> — BE not yet applied;
    /// <see cref="TrailingResult.PeakUpdated"/> — yeni high (veya ilk tick) → peak yazıldı, exit yok;
    /// <see cref="TrailingResult.ExitTriggered"/> — mark &lt; peak × (1 − trailPct) → event raised.
    /// </returns>
    /// <remarks>
    /// Throws <see cref="DomainException"/> sadece hard invariant ihlallerinde
    /// (closed position, non-positive mark, non-positive trailPct). NotEligible /
    /// PeakUpdated / ExitTriggered üçü de "expected paths" — Result-style enum
    /// caller'ı exception'sız tutar (CLAUDE.md root rule #5).
    /// </remarks>
    public TrailingResult UpdatePeakAndCheckTrailing(
        decimal markPrice, decimal trailPct, DateTimeOffset asOf)
    {
        EnsureOpen();
        if (markPrice <= 0m)
        {
            throw new DomainException("Mark price must be positive.");
        }
        if (trailPct <= 0m)
        {
            throw new DomainException("Trail percentage must be positive.");
        }

        // Trailing BE move'a "armed" — BE applied değilse dormant kal. Aggregate
        // bu kuralı içselleştiriyor; caller tek bir check'e güvenebilir.
        if (BreakEvenAppliedAt is null)
        {
            return TrailingResult.NotEligible;
        }

        // Long-only path. Yeni high → peak refresh. Default PeakMarkPrice=0
        // olduğundan ilk eligible tick mutlaka bu dala düşer ve peak'i mark'a yatırır.
        if (markPrice > PeakMarkPrice)
        {
            PeakMarkPrice = markPrice;
            UpdatedAt = asOf;
            return TrailingResult.PeakUpdated;
        }

        // Mevcut peak korunuyor; trail seviyesi altına inilmiş mi kontrol et.
        var trailingStop = PeakMarkPrice * (1m - trailPct);
        if (markPrice < trailingStop)
        {
            RaiseDomainEvent(new PositionTrailingExitTriggeredEvent(
                Id, Symbol.Value, PeakMarkPrice, markPrice, trailPct));
            return TrailingResult.ExitTriggered;
        }

        // Peak değişmedi, trail seviyesinin üstünde — exit yok, peak da yenilenmedi.
        // binance-expert spec: "else → PeakUpdated" (üç-state enum). Pratikte
        // "still tracking, no action" anlamı taşır; UpdatedAt'a dokunmuyoruz
        // çünkü aggregate state mutate olmadı.
        return TrailingResult.PeakUpdated;
    }

    public void Close(decimal exitPrice, string reason, DateTimeOffset now, decimal exitCommission = 0m)
    {
        EnsureOpen();
        if (exitPrice <= 0m)
        {
            throw new DomainException("Exit price must be positive.");
        }
        if (exitCommission < 0m)
        {
            throw new DomainException("Exit commission must be non-negative.");
        }

        ExitPrice = exitPrice;
        ExitCommission = exitCommission;

        // ADR-0020 §20.6 — RealizedPnl is net of entry + exit commissions (quote USDT).
        // Pre-ADR-0020 callers that don't provide a commission still get the gross
        // behaviour because EntryCommission and the default exitCommission are both 0m.
        var gross = Side == PositionSide.Long
            ? (exitPrice - AverageEntryPrice) * Quantity
            : (AverageEntryPrice - exitPrice) * Quantity;
        RealizedPnl = gross - EntryCommission - ExitCommission;
        UnrealizedPnl = 0m;
        Status = PositionStatus.Closed;
        ClosedAt = now;
        UpdatedAt = now;

        RaiseDomainEvent(new PositionClosedEvent(Id, Symbol.Value, RealizedPnl, reason, Mode));
    }

    private void EnsureOpen()
    {
        if (Status != PositionStatus.Open)
        {
            throw new DomainException($"Position {Id} is not open (Status={Status}).");
        }
    }
}
