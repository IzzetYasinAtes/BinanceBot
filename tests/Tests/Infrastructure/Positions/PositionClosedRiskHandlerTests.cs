using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Application.RiskProfiles.Commands.RecordTradeOutcome;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions.Events;
using BinanceBot.Infrastructure.Positions;
using BinanceBot.Tests.Infrastructure.Strategies;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BinanceBot.Tests.Infrastructure.Positions;

/// <summary>
/// Loop 5 t90 + Loop 13 boot CB-bug regression: <see cref="PositionClosedRiskHandler"/>
/// must hand <see cref="RecordTradeOutcomeCommand"/> the realized-only equity snapshot
/// (cash balance via <see cref="IEquitySnapshotProvider.GetRealizedEquityAsync"/>) —
/// not the realized+unrealized PnL sum (Loop 5) and not MTM equity (Loop 12/13). The
/// close-time snapshot must align with EquityPeakTrackerService's periodic ratchet,
/// which is also realized-only, so peak/current are comparable.
/// </summary>
public class PositionClosedRiskHandlerTests
{
    private static (PositionClosedRiskHandler Handler,
                    Mock<IMediator> Mediator,
                    Mock<IEquitySnapshotProvider> Equity)
        BuildHarness(decimal equityFromProvider)
    {
        var sc = new ServiceCollection();
        sc.AddDbContext<StubDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        sc.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<StubDbContext>());

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator
            .Setup(m => m.Send(It.IsAny<RecordTradeOutcomeCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ardalis.Result.Result.Success());
        sc.AddScoped(_ => mediator.Object);

        var equity = new Mock<IEquitySnapshotProvider>(MockBehavior.Strict);
        equity
            .Setup(e => e.GetRealizedEquityAsync(It.IsAny<TradingMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(equityFromProvider);
        sc.AddScoped(_ => equity.Object);

        var sp = sc.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var handler = new PositionClosedRiskHandler(
            scopeFactory,
            NullLogger<PositionClosedRiskHandler>.Instance);
        return (handler, mediator, equity);
    }

    [Fact]
    public async Task Handle_ForwardsRealizedEquitySnapshot_NotMtm_ToRecordTradeOutcome()
    {
        // Arrange: Paper $112.27 realized cash balance, +$2.27 close.
        // (Loop 13: realized-only — open-position unrealized PnL is excluded so the
        // close-time snapshot matches EquityPeakTrackerService's ratchet.)
        var (handler, mediator, equity) = BuildHarness(equityFromProvider: 112.27m);
        var evt = new PositionClosedEvent(
            PositionId: 42,
            Symbol: "BTCUSDT",
            RealizedPnl: 2.27m,
            Reason: "TakeProfit",
            Mode: TradingMode.Paper);

        // Act
        await handler.Handle(evt, CancellationToken.None);

        // Assert: realized equity flowed through unchanged.
        equity.Verify(e => e.GetRealizedEquityAsync(TradingMode.Paper, It.IsAny<CancellationToken>()), Times.Once);
        mediator.Verify(m => m.Send(
                It.Is<RecordTradeOutcomeCommand>(c =>
                    c.Mode == TradingMode.Paper &&
                    c.RealizedPnl == 2.27m &&
                    c.EquityAfter == 112.27m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ZeroEquity_StillRecordsOutcome_AsSoftSkipForCallerToHandle()
    {
        // LiveTestnet without account-sync → provider returns 0 (ADR-0011 §11.3).
        // Handler must still pass through; the command/handler is the policy point.
        var (handler, mediator, _) = BuildHarness(equityFromProvider: 0m);
        var evt = new PositionClosedEvent(
            PositionId: 7, Symbol: "ETHUSDT",
            RealizedPnl: -1m, Reason: "StopLoss",
            Mode: TradingMode.LiveTestnet);

        await handler.Handle(evt, CancellationToken.None);

        mediator.Verify(m => m.Send(
                It.Is<RecordTradeOutcomeCommand>(c => c.EquityAfter == 0m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
