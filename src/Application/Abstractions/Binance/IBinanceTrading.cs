namespace BinanceBot.Application.Abstractions.Binance;

public interface IBinanceTrading
{
    Task<TestOrderResponse> PlaceTestOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken);
    Task<LiveOrderResponse> PlaceLiveOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken);
    Task<CancelOrderResponse> CancelTestOrderAsync(string symbol, string clientOrderId, CancellationToken cancellationToken);
    Task<CancelOrderResponse> CancelLiveOrderAsync(string symbol, string clientOrderId, CancellationToken cancellationToken);
}

public sealed record PlaceOrderRequest(
    string Symbol,
    string Side,
    string Type,
    string TimeInForce,
    decimal Quantity,
    decimal? Price,
    decimal? StopPrice,
    string ClientOrderId);

public sealed record TestOrderResponse(bool Accepted, string? ErrorCode, string? ErrorMessage);
public sealed record CancelOrderResponse(bool Accepted, string? ErrorCode, string? ErrorMessage);

public sealed record LiveOrderResponse(
    bool Accepted,
    long? ExchangeOrderId,
    string Status,
    decimal ExecutedQuantity,
    decimal CummulativeQuoteQty,
    IReadOnlyList<LiveFillDto> Fills,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record LiveFillDto(
    long TradeId,
    decimal Price,
    decimal Quantity,
    decimal Commission,
    string CommissionAsset);
