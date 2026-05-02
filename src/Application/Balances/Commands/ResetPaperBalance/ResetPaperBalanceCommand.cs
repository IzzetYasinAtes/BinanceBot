using System.Text.Json;
using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Balances;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.RiskProfiles;
using BinanceBot.Domain.SystemEvents;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Application.Balances.Commands.ResetPaperBalance;

public sealed record ResetPaperBalanceCommand(decimal? StartingBalance) : IRequest<Result<ResetPaperBalanceDto>>;

public sealed record ResetPaperBalanceDto(
    Guid IterationId,
    decimal StartingBalance,
    int ResetCount,
    int ForceClosedPositionCount,
    int DeletedPositionCount,
    int DeletedOrderCount,
    int DeletedSystemEventCount,
    DateTimeOffset StartedAt);

public sealed class ResetPaperBalanceCommandValidator : AbstractValidator<ResetPaperBalanceCommand>
{
    public ResetPaperBalanceCommandValidator()
    {
        RuleFor(c => c.StartingBalance).GreaterThan(0m).When(c => c.StartingBalance.HasValue);
    }
}

public sealed class ResetPaperBalanceCommandHandler
    : IRequestHandler<ResetPaperBalanceCommand, Result<ResetPaperBalanceDto>>
{
    private const decimal DefaultStartingBalance = 100m;

    /// <summary>
    /// Loop 81 — full paper iteration reset purges the trade history that fed the UI
    /// "Toplam Net K/Z" hero. The previous reset only wiped <see cref="VirtualBalance"/>;
    /// closed Positions from the prior iteration survived, so
    /// <see cref="Infrastructure.Trading.EquitySnapshotProvider.GetRealizedEquityAsync"/>
    /// (which sums <c>Positions.RealizedPnl</c>) reported a phantom +$171 over an actual
    /// -$9.66 trade ledger (60 trades, 15W/45L) the moment the cash baseline was re-anchored.
    /// Trade-related <see cref="SystemEvent"/> rows from the prior iteration are also
    /// pruned so the "Sistem Olayları" UI tab does not stitch deleted Positions into the
    /// new iteration's narrative. Audit row <c>VirtualBalanceFullReset</c> survives so the
    /// reset itself is traceable.
    /// </summary>
    private static readonly IReadOnlyList<string> TradeSystemEventTypes = new List<string>
    {
        nameof(SystemEventType.SignalEmitted),
        nameof(SystemEventType.SignalSkipped),
        nameof(SystemEventType.OrderPlaced),
        nameof(SystemEventType.OrderFilled),
        nameof(SystemEventType.OrderCanceled),
        nameof(SystemEventType.PositionOpened),
        nameof(SystemEventType.PositionClosed),
        nameof(SystemEventType.RiskAlert),
    };

    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly ILogger<ResetPaperBalanceCommandHandler> _logger;

    public ResetPaperBalanceCommandHandler(
        IApplicationDbContext db,
        IClock clock,
        ICorrelationIdAccessor correlation,
        ILogger<ResetPaperBalanceCommandHandler> logger)
    {
        _db = db;
        _clock = clock;
        _correlation = correlation;
        _logger = logger;
    }

    public async Task<Result<ResetPaperBalanceDto>> Handle(
        ResetPaperBalanceCommand request, CancellationToken ct)
    {
        var starting = request.StartingBalance ?? DefaultStartingBalance;
        var now = _clock.UtcNow;

        // 1. Force-close all open Paper positions at markPrice (or averageEntryPrice as fallback).
        // Loop 81 — kept as a domain-event-emitting step BEFORE bulk delete so any in-flight
        // PositionClosed handlers (e.g. cash settlement) get one final chance to run before
        // the row vanishes. The forced-close events themselves are also pruned from
        // SystemEvents below so the new iteration starts with an empty trade narrative.
        var openPaperPositions = await _db.Positions
            .Where(p => p.Mode == TradingMode.Paper && p.Status == PositionStatus.Open)
            .ToListAsync(ct);

        var closedCount = 0;
        foreach (var pos in openPaperPositions)
        {
            var exit = pos.MarkPrice > 0m ? pos.MarkPrice : pos.AverageEntryPrice;
            try
            {
                pos.Close(exit, "paper_reset_force_close", now);
                closedCount++;
            }
            catch (DomainException ex)
            {
                _logger.LogWarning(ex, "Force-close failed for paper position {PosId}", pos.Id);
            }
        }

        // 2. Reset VirtualBalance(Paper).
        var paper = await _db.VirtualBalances
            .FirstOrDefaultAsync(b => b.Id == (int)TradingMode.Paper, ct);
        if (paper is null)
        {
            return Result<ResetPaperBalanceDto>.NotFound(
                "Paper VirtualBalance seed missing.");
        }

        try
        {
            paper.ResetForIteration(starting, now);
        }
        catch (DomainException ex)
        {
            return Result<ResetPaperBalanceDto>.Error(ex.Message);
        }

        // 3. Reset RiskProfile counters so EquityPeakTracker / CB do not carry the prior
        // iteration's PeakEquity into the new baseline. Tunable limits (RiskPerTradePct etc.)
        // are intentionally untouched — those belong to RiskProfileSeeder/appsettings.
        var paperProfile = await _db.RiskProfiles
            .FirstOrDefaultAsync(r => r.Id == RiskProfile.IdFor(TradingMode.Paper), ct);
        if (paperProfile is not null)
        {
            try
            {
                paperProfile.ResetForPaperIteration(now);
            }
            catch (DomainException ex)
            {
                return Result<ResetPaperBalanceDto>.Error(ex.Message);
            }
        }

        // 4. Purge prior-iteration trade history. Order: Positions (no FK from Positions) +
        // Orders (OrderFills cascade via OrderConfiguration's DeleteBehavior.Cascade) + the
        // trade-related SystemEvents enumerated above. Mode-scoped — LiveTestnet/LiveMainnet
        // rows survive (Paper reset must not touch live trading history).
        var paperPositions = await _db.Positions
            .Where(p => p.Mode == TradingMode.Paper)
            .ToListAsync(ct);
        _db.Positions.RemoveRange(paperPositions);

        var paperOrders = await _db.Orders
            .Where(o => o.Mode == TradingMode.Paper)
            .ToListAsync(ct);
        _db.Orders.RemoveRange(paperOrders);

        // SystemEvents do not carry a TradingMode column — Paper-only deployments are the
        // only writer of these event types today, so a type-scoped + occurred-before-reset
        // filter is correct. When LiveTestnet starts emitting trade events we will need to
        // embed mode in the payload and filter on it here.
        var tradeEventsToPurge = await _db.SystemEvents
            .Where(e => TradeSystemEventTypes.Contains(e.EventType) && e.OccurredAt < now)
            .ToListAsync(ct);
        _db.SystemEvents.RemoveRange(tradeEventsToPurge);

        var deletedPositionCount = paperPositions.Count;
        var deletedOrderCount = paperOrders.Count;
        var deletedSystemEventCount = tradeEventsToPurge.Count;

        // 5. System event for audit/UI — written AFTER the purge filter window (occurredAt = now
        // matches the reset boundary; the purge filter is strictly `<` so this row survives).
        _db.SystemEvents.Add(SystemEvent.Record(
            eventType: "VirtualBalanceFullReset",
            severity: SystemEventSeverity.Info,
            payloadJson: JsonSerializer.Serialize(new
            {
                paper.IterationId,
                StartingBalance = starting,
                paper.ResetCount,
                ForceClosedPositions = closedCount,
                DeletedPositions = deletedPositionCount,
                DeletedOrders = deletedOrderCount,
                DeletedSystemEvents = deletedSystemEventCount,
                Ts = now,
            }),
            source: "ResetPaperBalanceCommand",
            correlationId: _correlation.CorrelationId == Guid.Empty ? null : _correlation.CorrelationId,
            occurredAt: now));

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Paper balance full reset iter={Iter} starting={Start} forceClosed={Closed} "
            + "deletedPositions={DelPos} deletedOrders={DelOrd} deletedEvents={DelEvt}",
            paper.ResetCount, starting, closedCount,
            deletedPositionCount, deletedOrderCount, deletedSystemEventCount);

        return Result.Success(new ResetPaperBalanceDto(
            paper.IterationId,
            paper.StartingBalance,
            paper.ResetCount,
            closedCount,
            deletedPositionCount,
            deletedOrderCount,
            deletedSystemEventCount,
            paper.StartedAt));
    }
}
