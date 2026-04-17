using System.Text.Json;
using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Balances;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
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

        // 3. System event for audit/UI.
        _db.SystemEvents.Add(SystemEvent.Record(
            eventType: $"paper.reset.iter.{paper.ResetCount}",
            severity: SystemEventSeverity.Info,
            payloadJson: JsonSerializer.Serialize(new
            {
                paper.IterationId,
                StartingBalance = starting,
                paper.ResetCount,
                ForceClosedPositions = closedCount,
            }),
            source: "ResetPaperBalanceCommand",
            correlationId: _correlation.CorrelationId == Guid.Empty ? null : _correlation.CorrelationId,
            occurredAt: now));

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Paper balance reset iter={Iter} starting={Start} forceClosed={Closed}",
            paper.ResetCount, starting, closedCount);

        return Result.Success(new ResetPaperBalanceDto(
            paper.IterationId,
            paper.StartingBalance,
            paper.ResetCount,
            closedCount,
            paper.StartedAt));
    }
}
