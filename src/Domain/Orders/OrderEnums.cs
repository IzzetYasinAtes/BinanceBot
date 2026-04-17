namespace BinanceBot.Domain.Orders;

public enum OrderSide
{
    Buy = 1,
    Sell = 2,
}

public enum OrderType
{
    Market = 1,
    Limit = 2,
    StopLoss = 3,
    StopLossLimit = 4,
    TakeProfit = 5,
    TakeProfitLimit = 6,
    LimitMaker = 7,
}

public enum TimeInForce
{
    Gtc = 1,
    Ioc = 2,
    Fok = 3,
}

public enum OrderStatus
{
    New = 1,
    PartiallyFilled = 2,
    Filled = 3,
    Cancelled = 4,
    Rejected = 5,
    Expired = 6,
    ExpiredInMatch = 7,
}
