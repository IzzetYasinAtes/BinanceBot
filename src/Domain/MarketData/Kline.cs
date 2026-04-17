using BinanceBot.Domain.Common;
using BinanceBot.Domain.MarketData.Events;
using BinanceBot.Domain.ValueObjects;

namespace BinanceBot.Domain.MarketData;

public sealed class Kline : AggregateRoot<long>
{
    public Symbol Symbol { get; private set; } = default!;
    public KlineInterval Interval { get; private set; }
    public DateTimeOffset OpenTime { get; private set; }
    public DateTimeOffset CloseTime { get; private set; }
    public decimal OpenPrice { get; private set; }
    public decimal HighPrice { get; private set; }
    public decimal LowPrice { get; private set; }
    public decimal ClosePrice { get; private set; }
    public decimal Volume { get; private set; }
    public decimal QuoteVolume { get; private set; }
    public int TradeCount { get; private set; }
    public decimal TakerBuyBaseVolume { get; private set; }
    public decimal TakerBuyQuoteVolume { get; private set; }
    public bool IsClosed { get; private set; }

    private Kline() { }

    public static Kline Ingest(
        Symbol symbol,
        KlineInterval interval,
        DateTimeOffset openTime,
        DateTimeOffset closeTime,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume,
        decimal quoteVolume,
        int tradeCount,
        decimal takerBuyBase,
        decimal takerBuyQuote,
        bool isClosed)
    {
        Validate(open, high, low, close, volume, tradeCount, openTime, closeTime);

        var kline = new Kline
        {
            Symbol = symbol,
            Interval = interval,
            OpenTime = openTime,
            CloseTime = closeTime,
            OpenPrice = open,
            HighPrice = high,
            LowPrice = low,
            ClosePrice = close,
            Volume = volume,
            QuoteVolume = quoteVolume,
            TradeCount = tradeCount,
            TakerBuyBaseVolume = takerBuyBase,
            TakerBuyQuoteVolume = takerBuyQuote,
            IsClosed = isClosed,
        };

        kline.RaiseDomainEvent(new KlineIngestedEvent(symbol.Value, interval, openTime, isClosed));
        if (isClosed)
        {
            kline.RaiseDomainEvent(new KlineClosedEvent(symbol.Value, interval, openTime, closeTime, close));
        }

        return kline;
    }

    public void Upsert(
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume,
        decimal quoteVolume,
        int tradeCount,
        decimal takerBuyBase,
        decimal takerBuyQuote,
        bool isClosed)
    {
        Validate(open, high, low, close, volume, tradeCount, OpenTime, CloseTime);

        var justClosed = !IsClosed && isClosed;

        OpenPrice = open;
        HighPrice = high;
        LowPrice = low;
        ClosePrice = close;
        Volume = volume;
        QuoteVolume = quoteVolume;
        TradeCount = tradeCount;
        TakerBuyBaseVolume = takerBuyBase;
        TakerBuyQuoteVolume = takerBuyQuote;
        IsClosed = isClosed;

        RaiseDomainEvent(new KlineIngestedEvent(Symbol.Value, Interval, OpenTime, isClosed));
        if (justClosed)
        {
            RaiseDomainEvent(new KlineClosedEvent(Symbol.Value, Interval, OpenTime, CloseTime, close));
        }
    }

    private static void Validate(
        decimal open, decimal high, decimal low, decimal close,
        decimal volume, int tradeCount,
        DateTimeOffset openTime, DateTimeOffset closeTime)
    {
        if (open < 0m || high < 0m || low < 0m || close < 0m)
        {
            throw new DomainException("Kline prices cannot be negative.");
        }

        if (volume < 0m)
        {
            throw new DomainException("Kline volume cannot be negative.");
        }

        if (tradeCount < 0)
        {
            throw new DomainException("Kline trade count cannot be negative.");
        }

        if (closeTime <= openTime)
        {
            throw new DomainException("Kline closeTime must be greater than openTime.");
        }

        if (high < low)
        {
            throw new DomainException("Kline high must be >= low.");
        }
    }
}
