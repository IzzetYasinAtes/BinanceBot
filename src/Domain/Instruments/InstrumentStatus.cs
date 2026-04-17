namespace BinanceBot.Domain.Instruments;

public enum InstrumentStatus
{
    PreTrading = 0,
    Trading = 1,
    PostTrading = 2,
    EndOfDay = 3,
    Halt = 4,
    AuctionMatch = 5,
    Break = 6,
}
