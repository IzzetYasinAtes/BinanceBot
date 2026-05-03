using Ardalis.Result;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.Common;

namespace BinanceBot.Application.Abstractions.Exchange;

/// <summary>
/// Loop 92 — Spot trading port (<c>IBinanceTrading</c>) yerini alan Futures-aware
/// exchange client port (ADR-0025). Spot-only (BUY=long open, SELL=long close)
/// yerine USDT-M Futures simetrik order flow + Futures-only setup
/// (leverage/marginType/positionSide/dual). Result-based hata kanalı (Ardalis).
///
/// Tek implementation: <c>BinanceFuturesClient</c> (Infrastructure). Test mock
/// veya altrenatif borsa entegrasyonu bu interface'i implement eder.
/// </summary>
public interface IExchangeClient
{
    /// <summary>
    /// MARKET veya LIMIT/STOP_MARKET emir gönderir. <c>request.Direction</c> Long/Short
    /// hint'i, <c>request.Side</c> BUY/SELL exchange protokol değeri. Futures one-way
    /// modda <c>positionSide=BOTH</c> kullanılır (spec §5).
    /// </summary>
    Task<Result<LiveOrderResponse>> PlaceLiveOrderAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Test order — gerçekte execute edilmez (Spot'tan miras, Futures testnet'te de kullanılır).
    /// </summary>
    Task<Result<TestOrderResponse>> PlaceTestOrderAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Açık emir iptal eder. Futures: <c>DELETE /fapi/v1/order?symbol=&amp;origClientOrderId=</c>.
    /// </summary>
    Task<Result<CancelOrderResponse>> CancelLiveOrderAsync(
        string symbol,
        string clientOrderId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Test mode iptal — gerçek borsada çalışmaz, test harness için.
    /// </summary>
    Task<Result<CancelOrderResponse>> CancelTestOrderAsync(
        string symbol,
        string clientOrderId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Futures-spesifik: sembol için kaldıraç ayarla (POST /fapi/v1/leverage).
    /// Bot tavsiye 1-3x. Bot boot'ta her aktif sembol için bir kez çağrılır.
    /// </summary>
    Task<Result> SetLeverageAsync(
        string symbol,
        int leverage,
        CancellationToken cancellationToken);

    /// <summary>
    /// Futures-spesifik: sembol için margin tipi ayarla (POST /fapi/v1/marginType).
    /// Bot tavsiye ISOLATED. Idempotent değil — tekrar çağrı zaten ayarlıysa
    /// hata döndürür; implementation hata code'unu yutar.
    /// </summary>
    Task<Result> SetMarginTypeAsync(
        string symbol,
        MarginType marginType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Futures-spesifik: pozisyon modunu One-way veya Hedge olarak ayarla
    /// (POST /fapi/v1/positionSide/dual). Bot tavsiye One-way (dualSidePosition=false).
    /// </summary>
    Task<Result> SetPositionModeAsync(
        bool dualSidePosition,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hesap bakiyesini sorgular (GET /fapi/v3/balance). USDT walletBalance + crossUnPnl
    /// + availableBalance döner. Boot ve mark-to-market refresh cycle için.
    /// </summary>
    Task<Result<ExchangeAccountInfo>> GetAccountInfoAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Açık pozisyonları sorgular (GET /fapi/v3/positionRisk). One-way modda
    /// her sembol için tek pozisyon (positionSide=BOTH); positionAmt &gt; 0 long,
    /// &lt; 0 short, 0 flat.
    /// </summary>
    Task<Result<IReadOnlyList<ExchangePositionDto>>> GetOpenPositionsAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Loop 92 — Futures hesap özeti DTO. Spot'tan farklı: AllocatedMargin +
/// UnrealizedPnl ayrı alanlar. Bot WalletBalance üzerinden sizing yapar
/// (sermaye), AllocatedMargin pozisyon başına locked, UnrealizedPnl
/// floating equity.
/// </summary>
/// <param name="WalletBalance">Toplam wallet (USDT). Spot CurrentBalance karşılığı.</param>
/// <param name="AvailableBalance">Yeni emir açabilmek için kullanılabilir
/// (WalletBalance − AllocatedMargin − pendingOrderMargin).</param>
/// <param name="UnrealizedPnl">Açık pozisyonların net floating PnL (mark price bazlı).</param>
public sealed record ExchangeAccountInfo(
    decimal WalletBalance,
    decimal AvailableBalance,
    decimal UnrealizedPnl);

/// <summary>
/// Loop 92 — Futures açık pozisyon DTO. Bot'un kendi <c>Position</c> aggregate'i
/// ile reconcile için kullanılır (boot drift detection).
/// </summary>
/// <param name="Symbol">Trade sembolü (BTCUSDT).</param>
/// <param name="Direction">Long: positionAmt &gt; 0; Short: positionAmt &lt; 0.</param>
/// <param name="Quantity">Pozisyon büyüklüğü mutlak değer (base asset).</param>
/// <param name="EntryPrice">Ortalama giriş fiyatı.</param>
/// <param name="MarkPrice">Anlık mark price.</param>
/// <param name="UnrealizedPnl">Floating PnL (USDT).</param>
/// <param name="LiquidationPrice">Tasfiye fiyatı (Futures-only, 0 = N/A).</param>
/// <param name="IsolatedMargin">ISOLATED modda pozisyon margin'i (0 = CROSSED).</param>
public sealed record ExchangePositionDto(
    string Symbol,
    TradeDirection Direction,
    decimal Quantity,
    decimal EntryPrice,
    decimal MarkPrice,
    decimal UnrealizedPnl,
    decimal LiquidationPrice,
    decimal IsolatedMargin);
