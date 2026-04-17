using BinanceBot.Domain.Common;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.MarketData.Events;
using BinanceBot.Domain.ValueObjects;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.MarketData;

public class KlineTests
{
    private static readonly Symbol Btc = Symbol.From("BTCUSDT");
    private static readonly DateTimeOffset Open = new(2026, 4, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Close = Open.AddMinutes(1);

    [Fact]
    public void Ingest_OpenKline_RaisesOnlyIngestedEvent()
    {
        var kline = Kline.Ingest(
            Btc, KlineInterval.OneMinute, Open, Close,
            open: 100m, high: 101m, low: 99m, close: 100.5m,
            volume: 1m, quoteVolume: 100m, tradeCount: 10,
            takerBuyBase: 0.5m, takerBuyQuote: 50m, isClosed: false);

        kline.IsClosed.Should().BeFalse();
        kline.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<KlineIngestedEvent>();
    }

    [Fact]
    public void Ingest_ClosedKline_RaisesIngestedAndClosed()
    {
        var kline = Kline.Ingest(
            Btc, KlineInterval.OneMinute, Open, Close,
            100m, 101m, 99m, 100.5m, 1m, 100m, 10, 0.5m, 50m, true);

        kline.IsClosed.Should().BeTrue();
        kline.DomainEvents.Should().HaveCount(2);
        kline.DomainEvents.Should().Contain(e => e is KlineIngestedEvent);
        kline.DomainEvents.Should().Contain(e => e is KlineClosedEvent);
    }

    [Fact]
    public void Upsert_TransitionOpenToClosed_RaisesClosedEvent()
    {
        var kline = Kline.Ingest(
            Btc, KlineInterval.OneMinute, Open, Close,
            100m, 101m, 99m, 100.5m, 1m, 100m, 10, 0.5m, 50m, false);
        kline.ClearDomainEvents();

        kline.Upsert(100m, 102m, 99m, 101m, 2m, 200m, 20, 1m, 100m, true);

        kline.DomainEvents.Should().Contain(e => e is KlineClosedEvent);
    }

    [Fact]
    public void Upsert_StillOpen_NoClosedEvent()
    {
        var kline = Kline.Ingest(
            Btc, KlineInterval.OneMinute, Open, Close,
            100m, 101m, 99m, 100.5m, 1m, 100m, 10, 0.5m, 50m, false);
        kline.ClearDomainEvents();

        kline.Upsert(100m, 102m, 99m, 101m, 2m, 200m, 20, 1m, 100m, false);

        kline.DomainEvents.Should().NotContain(e => e is KlineClosedEvent);
    }

    [Fact]
    public void Ingest_NegativePrice_Throws()
    {
        var act = () => Kline.Ingest(
            Btc, KlineInterval.OneMinute, Open, Close,
            -1m, 101m, 99m, 100.5m, 1m, 100m, 10, 0.5m, 50m, false);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Ingest_HighLessThanLow_Throws()
    {
        var act = () => Kline.Ingest(
            Btc, KlineInterval.OneMinute, Open, Close,
            100m, 98m, 99m, 100.5m, 1m, 100m, 10, 0.5m, 50m, false);

        act.Should().Throw<DomainException>()
            .WithMessage("*high*");
    }

    [Fact]
    public void Ingest_CloseTimeBeforeOpenTime_Throws()
    {
        var act = () => Kline.Ingest(
            Btc, KlineInterval.OneMinute, Close, Open,
            100m, 101m, 99m, 100.5m, 1m, 100m, 10, 0.5m, 50m, false);

        act.Should().Throw<DomainException>()
            .WithMessage("*closeTime*");
    }
}
