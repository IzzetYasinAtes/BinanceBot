using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles.Events;

namespace BinanceBot.Domain.RiskProfiles;

public sealed class RiskProfile : AggregateRoot<int>
{
    /// <summary>
    /// RiskProfile is singleton-per-mode (ADR-0008):
    /// Id 1=Paper, 2=LiveTestnet, 3=LiveMainnet. See <see cref="IdFor"/>.
    /// </summary>
    public static int IdFor(TradingMode mode) => (int)mode;

    public decimal RiskPerTradePct { get; private set; }
    public decimal MaxPositionSizePct { get; private set; }
    public decimal MaxDrawdown24hPct { get; private set; }
    public decimal MaxDrawdownAllTimePct { get; private set; }
    public int MaxConsecutiveLosses { get; private set; }

    /// <summary>
    /// Loop 92 — Futures kaldıraç (ADR-0025). Default 1x (margin = full notional);
    /// max bot için 3x. Bot boot'ta her sembol için POST /fapi/v1/leverage
    /// çağırır.
    /// </summary>
    public int Leverage { get; private set; }

    /// <summary>
    /// Loop 92 — Liquidation alarm eşiği. marginRatio bu değerin üstüne çıkarsa
    /// MarkToMarketWorker force close dispatcher'ını tetikler. Default 0.80 (%80).
    /// </summary>
    public decimal MaintenanceMarginRatio { get; private set; }

    /// <summary>
    /// Loop 92 — saatlik max funding fee toleransı. 8h funding cycle'da bu
    /// değerin üstünde net ödeme yapılırsa pozisyon kapatma kararı verilir.
    /// Default 0.001 (%0.1 saatte = %0.8 günlük).
    /// </summary>
    public decimal MaxFundingFeePerHour { get; private set; }

    /// <summary>
    /// Loop 14 (research-paper-live-and-sizing.md §B/C5): hard ceiling on the number
    /// of simultaneously-open positions for this trading mode. The fan-out handler
    /// counts open positions per mode and skips new entries once this limit is reached,
    /// so a single strategy storm cannot overwhelm a $100 paper account by stacking
    /// $40 notionals across symbols. Range [1, 10]; default 2.
    /// </summary>
    public int MaxOpenPositions { get; private set; }

    public decimal? RiskPerTradeCap { get; private set; }
    public decimal? MaxPositionCap { get; private set; }
    public string? CapsAdminNote { get; private set; }

    public CircuitBreakerStatus CircuitBreakerStatus { get; private set; }
    public DateTimeOffset? CircuitBreakerTrippedAt { get; private set; }
    public string? CircuitBreakerReason { get; private set; }

    public int ConsecutiveLosses { get; private set; }
    public decimal RealizedPnl24h { get; private set; }
    public decimal RealizedPnlAllTime { get; private set; }
    public decimal PeakEquity { get; private set; }
    public decimal CurrentDrawdownPct { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private RiskProfile() { }

    public static RiskProfile CreateDefault(TradingMode mode, DateTimeOffset now) =>
        new()
        {
            Id = IdFor(mode),
            RiskPerTradePct = 0.01m,
            MaxPositionSizePct = 0.10m,
            MaxDrawdown24hPct = 0.05m,
            MaxDrawdownAllTimePct = 0.25m,
            MaxConsecutiveLosses = 3,
            MaxOpenPositions = 2,
            // Loop 92 — Futures defaults (ADR-0025).
            Leverage = 1,
            MaintenanceMarginRatio = 0.80m,
            MaxFundingFeePerHour = 0.001m,
            CircuitBreakerStatus = CircuitBreakerStatus.Healthy,
            PeakEquity = 0m,
            UpdatedAt = now,
        };

    /// <summary>
    /// Loop 92 — Futures kaldıraç ayarla. Range [1, 3] (bot için max). Bot boot'tan
    /// önce her sembol için exchange'e gönderilir (BinanceFuturesClient.SetLeverageAsync).
    /// </summary>
    public void SetLeverage(int leverage, DateTimeOffset now)
    {
        if (leverage < 1 || leverage > 3)
        {
            throw new DomainException($"Leverage must be in [1, 3] (got {leverage}).");
        }
        Leverage = leverage;
        UpdatedAt = now;
    }

    /// <summary>
    /// Loop 92 — Maintenance margin ratio eşiği. (0, 1) aralığında. Bot için
    /// MarkToMarketWorker force close threshold'u — Binance'in gerçek likidasyon
    /// seviyesinden önce devreye girer.
    /// </summary>
    public void SetMaintenanceMarginRatio(decimal ratio, DateTimeOffset now)
    {
        if (ratio <= 0m || ratio >= 1m)
        {
            throw new DomainException($"MaintenanceMarginRatio must be in (0, 1) (got {ratio}).");
        }
        MaintenanceMarginRatio = ratio;
        UpdatedAt = now;
    }

    /// <summary>
    /// Loop 92 — 8h funding cycle limiti. Saatlik birim: %0.001 = %0.1 saatte
    /// = %0.8 günlük tolerans.
    /// </summary>
    public void ApplyFundingFeeAndCheck(decimal hourlyRate, DateTimeOffset now)
    {
        if (hourlyRate < 0m)
        {
            throw new DomainException("Hourly funding rate must be non-negative.");
        }
        // Bu method audit hook'u — gerçek karar FundingRateWorker'da. Şimdilik
        // sadece UpdatedAt güncelliyoruz; future use için raise event eklenebilir.
        UpdatedAt = now;
    }

    /// <summary>
    /// Loop 14 (research-paper-live-and-sizing.md §C2): risk envelope widened to make
    /// $100 paper accounts viable — riskPerTradePct upper bound 2% → 5%, maxPositionSizePct
    /// 20% → 60% (so a single $40 notional on a $100 equity is allowed). The MaxOpenPositions
    /// parameter is the new fan-out throttle (range [1, 10]).
    /// </summary>
    public void UpdateLimits(
        decimal riskPerTradePct,
        decimal maxPositionSizePct,
        decimal maxDrawdown24hPct,
        decimal maxDrawdownAllTimePct,
        int maxConsecutiveLosses,
        int maxOpenPositions,
        DateTimeOffset now)
    {
        if (riskPerTradePct is <= 0m or > 0.05m)
        {
            throw new DomainException("RiskPerTradePct must be (0, 0.05].");
        }
        if (maxPositionSizePct is <= 0m or > 0.60m)
        {
            throw new DomainException("MaxPositionSizePct must be (0, 0.60].");
        }
        if (maxDrawdown24hPct is <= 0m or > 0.30m)
        {
            throw new DomainException("MaxDrawdown24hPct must be (0, 0.30].");
        }
        if (maxDrawdownAllTimePct is <= 0m or > 0.60m)
        {
            throw new DomainException("MaxDrawdownAllTimePct must be (0, 0.60].");
        }
        if (maxConsecutiveLosses is < 1 or > 15)
        {
            throw new DomainException("MaxConsecutiveLosses must be [1, 15].");
        }
        if (maxOpenPositions is < 1 or > 10)
        {
            throw new DomainException("MaxOpenPositions must be [1, 10].");
        }

        RiskPerTradePct = riskPerTradePct;
        MaxPositionSizePct = maxPositionSizePct;
        MaxDrawdown24hPct = maxDrawdown24hPct;
        MaxDrawdownAllTimePct = maxDrawdownAllTimePct;
        MaxConsecutiveLosses = maxConsecutiveLosses;
        MaxOpenPositions = maxOpenPositions;
        UpdatedAt = now;

        RaiseDomainEvent(new RiskProfileUpdatedEvent(
            riskPerTradePct, maxPositionSizePct,
            maxDrawdown24hPct, maxDrawdownAllTimePct,
            maxConsecutiveLosses, maxOpenPositions));
    }

    public void OverrideCaps(
        decimal riskPerTradeCap,
        decimal maxPositionCap,
        string adminNote,
        DateTimeOffset now)
    {
        if (riskPerTradeCap <= 0m || maxPositionCap <= 0m)
        {
            throw new DomainException("Caps must be positive.");
        }
        if (string.IsNullOrWhiteSpace(adminNote))
        {
            throw new DomainException("AdminNote required for cap override.");
        }

        RiskPerTradeCap = riskPerTradeCap;
        MaxPositionCap = maxPositionCap;
        CapsAdminNote = adminNote.Trim();
        UpdatedAt = now;

        RaiseDomainEvent(new RiskCapsOverriddenEvent(riskPerTradeCap, maxPositionCap, CapsAdminNote));
    }

    public void TripCircuitBreaker(string reason, decimal observedDrawdownPct, DateTimeOffset now)
    {
        if (CircuitBreakerStatus == CircuitBreakerStatus.Tripped)
        {
            return;
        }
        CircuitBreakerStatus = CircuitBreakerStatus.Tripped;
        CircuitBreakerReason = reason;
        CircuitBreakerTrippedAt = now;
        UpdatedAt = now;
        RaiseDomainEvent(new CircuitBreakerTrippedEvent(reason, observedDrawdownPct, now));
    }

    public void ResetCircuitBreaker(string adminNote, DateTimeOffset now)
    {
        if (CircuitBreakerStatus == CircuitBreakerStatus.Healthy)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(adminNote))
        {
            throw new DomainException("AdminNote required for CB reset.");
        }

        CircuitBreakerStatus = CircuitBreakerStatus.Healthy;
        CircuitBreakerReason = null;
        CircuitBreakerTrippedAt = null;
        ConsecutiveLosses = 0;
        UpdatedAt = now;
        RaiseDomainEvent(new CircuitBreakerResetEvent(adminNote.Trim(), now));
    }

    /// <summary>
    /// Loop 80 — bot startup auto-reset for the consecutive-loss streak counter.
    /// <see cref="ResetCircuitBreaker"/> already zeroes <see cref="ConsecutiveLosses"/>
    /// but exits early when the CB is <c>Healthy</c>; that meant a stale streak
    /// (e.g. 4 from Loop 78) was persisted across bot restarts and could trip
    /// the CB on the very first new losing trade. This method is the dedicated
    /// streak reset path: it leaves <see cref="CircuitBreakerStatus"/>,
    /// <see cref="CircuitBreakerReason"/>, drawdown and PnL state untouched and
    /// only zeroes the streak counter. Idempotent — calling with an already-zero
    /// counter is a no-op (no <see cref="UpdatedAt"/> bump, no event noise) so
    /// the seeder can call it on every boot without manufacturing churn.
    /// </summary>
    public void ResetConsecutiveLossCounter(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Reason required for ConsecutiveLosses reset.");
        }
        if (ConsecutiveLosses == 0)
        {
            return;
        }

        ConsecutiveLosses = 0;
        UpdatedAt = now;
    }

    /// <summary>
    /// Loop 81 — paper iteration full reset companion to <see cref="VirtualBalance.ResetForIteration"/>.
    /// When <c>POST /api/papertrade/reset</c> wipes Positions + cash, the per-mode
    /// risk accounting (24h/all-time realized PnL, peak equity, drawdown, streak)
    /// must zero alongside; otherwise <see cref="EquityPeakTrackerService"/> keeps
    /// computing drawdown against a stale peak from the prior iteration and can
    /// trip the CB on an artificially small dip ("UI Loop 81 boot trace —
    /// PeakEquity $171 vs new starting $100"). Also resets
    /// <see cref="CircuitBreakerStatus"/>/<see cref="CircuitBreakerReason"/> so a
    /// CB tripped in the prior iteration does not survive a full reset (operator
    /// intent on <c>/papertrade/reset</c> is "fresh slate").
    ///
    /// Paper-only — mirrors <see cref="VirtualBalance.ResetForIteration"/>'s same
    /// guard. Tunable limits (<see cref="RiskPerTradePct"/>,
    /// <see cref="MaxPositionSizePct"/> etc.) are intentionally preserved — they
    /// belong to <see cref="UpdateLimits"/> / appsettings, not to a per-iteration
    /// reset.
    /// </summary>
    public void ResetForPaperIteration(DateTimeOffset now)
    {
        if (Id != IdFor(TradingMode.Paper))
        {
            throw new DomainException(
                $"ResetForPaperIteration not allowed for profile id {Id}; only Paper (id=1) is resettable.");
        }

        ConsecutiveLosses = 0;
        RealizedPnl24h = 0m;
        RealizedPnlAllTime = 0m;
        PeakEquity = 0m;
        CurrentDrawdownPct = 0m;
        CircuitBreakerStatus = CircuitBreakerStatus.Healthy;
        CircuitBreakerReason = null;
        CircuitBreakerTrippedAt = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Loop 6 → Loop 7 bug #17 fix: PeakEquity must follow the live equity stream
    /// (cash + unrealized PnL), not just realized closes. Otherwise an intraday spike
    /// like Loop 6 t30 ($195 unrealized peak → t90 $56 close) computes drawdown
    /// against a stale $99 peak and trips the CB at -43% when the real intraday
    /// drawdown was -71% from a peak that was never recorded.
    ///
    /// Loop 7 → Loop 8 bug #19 fix: this method now also evaluates the drawdown
    /// circuit-breaker. Previously trip evaluation lived only on
    /// <see cref="RecordTradeOutcome"/>, so an intraday equity slide that crossed the
    /// drawdown ceiling without a closed trade would never trip the CB. The tracker
    /// is the only path that observes such slides, so it must own the drawdown trip.
    /// Consecutive-losses trip remains exclusive to <see cref="RecordTradeOutcome"/>.
    /// </summary>
    public void RecordPeakEquitySnapshot(decimal currentEquity, DateTimeOffset now)
    {
        if (currentEquity <= 0m)
        {
            return;
        }

        if (currentEquity > PeakEquity)
        {
            PeakEquity = currentEquity;
            CurrentDrawdownPct = 0m;
        }
        else if (PeakEquity > 0m)
        {
            CurrentDrawdownPct = (PeakEquity - currentEquity) / PeakEquity;
        }

        UpdatedAt = now;

        TripIfDrawdownBreached(now);
    }

    public void RecordTradeOutcome(decimal realizedPnl, decimal equityAfter, DateTimeOffset now)
    {
        if (realizedPnl < 0m)
        {
            ConsecutiveLosses++;
        }
        else if (realizedPnl > 0m)
        {
            ConsecutiveLosses = 0;
        }

        RealizedPnl24h += realizedPnl;
        RealizedPnlAllTime += realizedPnl;

        if (equityAfter > PeakEquity)
        {
            PeakEquity = equityAfter;
            CurrentDrawdownPct = 0m;
        }
        else if (PeakEquity > 0m)
        {
            CurrentDrawdownPct = (PeakEquity - equityAfter) / PeakEquity;
        }

        UpdatedAt = now;
        RaiseDomainEvent(new TradeOutcomeRecordedEvent(realizedPnl, ConsecutiveLosses));

        if (CircuitBreakerStatus == CircuitBreakerStatus.Healthy
            && ConsecutiveLosses >= MaxConsecutiveLosses)
        {
            TripCircuitBreaker(
                $"consecutive_losses={ConsecutiveLosses}",
                CurrentDrawdownPct,
                now);
            return;
        }

        TripIfDrawdownBreached(now);
    }

    /// <summary>
    /// Loop 8 bug #19: evaluates whether <see cref="CurrentDrawdownPct"/> has breached
    /// the tighter of the 24h / all-time drawdown ceilings and trips the CB if so.
    /// Both <see cref="RecordTradeOutcome"/> and <see cref="RecordPeakEquitySnapshot"/>
    /// route through here so trip semantics stay identical regardless of the path that
    /// observed the breach (trade close vs. intraday equity slide).
    /// </summary>
    private void TripIfDrawdownBreached(DateTimeOffset now)
    {
        if (CircuitBreakerStatus != CircuitBreakerStatus.Healthy)
        {
            return;
        }

        // Use the strictest configured ceiling. 24h is normally smaller (e.g. 5%) than
        // the all-time fuse (e.g. 25%), so 24h fires first; but we keep the all-time
        // check too in case a profile is reconfigured with a tighter all-time fuse.
        var ceiling24h = MaxDrawdown24hPct;
        var ceilingAll = MaxDrawdownAllTimePct;
        var effectiveCeiling = ceiling24h < ceilingAll ? ceiling24h : ceilingAll;
        var breachedScope = ceiling24h < ceilingAll ? "24h" : "all_time";

        if (CurrentDrawdownPct >= effectiveCeiling)
        {
            var reason = $"drawdown_{breachedScope}={CurrentDrawdownPct:P2}>={effectiveCeiling:P2}";
            TripCircuitBreaker(reason, CurrentDrawdownPct, now);
        }
    }
}
