using BinanceBot.Domain.Balances.Events;
using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.Balances;

/// <summary>
/// Mode-scoped cash + equity snapshot (ADR-0008 §8.4 + Loop 92 Futures pivot ADR-0025).
/// Id == (int)Mode — singleton-per-mode seeded via migration.
///
/// Loop 92 Futures genişlemesi:
/// <list type="bullet">
///   <item><see cref="WalletBalance"/> — toplam cüzdan (USDT). CurrentBalance backward-compat alias.</item>
///   <item><see cref="AllocatedMargin"/> — açık pozisyonların margin'lerinin toplamı (locked).</item>
///   <item><see cref="UnrealizedPnl"/> — açık pozisyonların floating PnL'i (mark price bazlı).</item>
///   <item><see cref="AvailableBalance"/> — emir açabilmek için kullanılabilir = Wallet − AllocatedMargin.</item>
/// </list>
///
/// Yeni davranışlar:
/// <list type="bullet">
///   <item><see cref="AllocateMarginForPosition"/> — pozisyon açıldı, margin lock.</item>
///   <item><see cref="ReturnMarginAndApplyPnl"/> — pozisyon kapandı, margin geri + realizedPnl.</item>
///   <item><see cref="ApplyFundingFee"/> — 8h funding cycle gider/gelir.</item>
/// </list>
///
/// <c>ApplyFill</c> backward-compat shim olarak korundu — sadece WalletBalance günceller.
/// Loop 92+ flow: paper sim'den gelen <c>realizedDelta</c>'yı yine ApplyFill alacak ama
/// margin akışını ayrıca AllocateMarginForPosition/ReturnMarginAndApplyPnl tetikleyecek.
/// </summary>
public sealed class VirtualBalance : AggregateRoot<int>
{
    public TradingMode Mode { get; private set; }
    public decimal StartingBalance { get; private set; }

    /// <summary>
    /// Loop 92 — toplam cüzdan (USDT). Önceki <see cref="CurrentBalance"/> alias rolü oynar
    /// (DB kolon adı CurrentBalance kalır geriye uyumluluk için, EF mapping'te WalletBalance shadow).
    /// </summary>
    public decimal WalletBalance { get; private set; }

    /// <summary>Backward-compat alias. WalletBalance ile aynı değer.</summary>
    public decimal CurrentBalance => WalletBalance;

    public decimal Equity { get; private set; }

    /// <summary>
    /// Loop 92 — açık pozisyonların margin'lerinin toplamı. Pozisyon açıldığında
    /// <see cref="AllocateMarginForPosition"/> ile arttırılır, kapanışta düşürülür.
    /// AvailableBalance = Wallet − AllocatedMargin formülünde kullanılır.
    /// </summary>
    public decimal AllocatedMargin { get; private set; }

    /// <summary>
    /// Loop 92 — açık pozisyonların floating PnL toplamı. <see cref="ApplyUnrealized"/>
    /// her mark-to-market cycle'da günceller. Equity = Wallet + UnrealizedPnl.
    /// </summary>
    public decimal UnrealizedPnl { get; private set; }

    /// <summary>Loop 92 — yeni emir açabilmek için kullanılabilir bakiye.</summary>
    public decimal AvailableBalance => WalletBalance - AllocatedMargin;

    public Guid IterationId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? LastResetAt { get; private set; }
    public int ResetCount { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private VirtualBalance() { }

    public static VirtualBalance CreateDefault(
        TradingMode mode,
        decimal startingBalance,
        DateTimeOffset now)
    {
        if (startingBalance < 0m)
        {
            throw new DomainException("StartingBalance must be >= 0.");
        }

        return new VirtualBalance
        {
            Id = RiskProfileIdOf(mode),
            Mode = mode,
            StartingBalance = startingBalance,
            WalletBalance = startingBalance,
            Equity = startingBalance,
            AllocatedMargin = 0m,
            UnrealizedPnl = 0m,
            IterationId = Guid.NewGuid(),
            StartedAt = now,
            LastResetAt = null,
            ResetCount = 0,
            UpdatedAt = now,
        };
    }

    public void ResetForIteration(decimal startingBalance, DateTimeOffset now)
    {
        if (Mode != TradingMode.Paper)
        {
            throw new DomainException(
                $"Reset not allowed for mode {Mode}; only Paper is resettable.");
        }
        if (startingBalance <= 0m)
        {
            throw new DomainException("StartingBalance must be positive on reset.");
        }

        IterationId = Guid.NewGuid();
        StartingBalance = startingBalance;
        WalletBalance = startingBalance;
        Equity = startingBalance;
        AllocatedMargin = 0m;
        UnrealizedPnl = 0m;
        StartedAt = now;
        LastResetAt = now;
        ResetCount++;
        UpdatedAt = now;

        RaiseDomainEvent(new VirtualBalanceResetEvent(Mode, IterationId, startingBalance, now));
    }

    /// <summary>
    /// Paper-only. Realized delta (can be +/-) adjusts wallet balance.
    /// Backward-compat: paper sim'den gelen cash delta'yı WalletBalance'a uygular.
    /// Loop 92'de margin akışı ayrıca <see cref="AllocateMarginForPosition"/> +
    /// <see cref="ReturnMarginAndApplyPnl"/> ile yönetilir.
    /// </summary>
    public void ApplyFill(decimal realizedDelta, DateTimeOffset now)
    {
        if (Mode != TradingMode.Paper)
        {
            throw new DomainException(
                $"VirtualBalance.ApplyFill only valid for Paper mode (was {Mode}).");
        }

        WalletBalance += realizedDelta;
        Equity = WalletBalance + UnrealizedPnl;
        UpdatedAt = now;

        RaiseDomainEvent(new VirtualBalanceFillAppliedEvent(Mode, realizedDelta, WalletBalance, now));
    }

    /// <summary>
    /// Loop 92 — Pozisyon açıldı, margin allocate. Paper-only (LiveTestnet/Mainnet
    /// API'den gerçek balance çeker). Negatif margin kabul etmez.
    /// </summary>
    public void AllocateMarginForPosition(decimal margin, DateTimeOffset now)
    {
        if (Mode != TradingMode.Paper)
        {
            throw new DomainException(
                $"AllocateMarginForPosition only valid for Paper mode (was {Mode}).");
        }
        if (margin < 0m)
        {
            throw new DomainException("Margin must be non-negative.");
        }

        AllocatedMargin += margin;
        UpdatedAt = now;

        RaiseDomainEvent(new PositionMarginAllocatedEvent(Mode, margin, AllocatedMargin, now));
    }

    /// <summary>
    /// Loop 92 — Pozisyon kapandı: margin geri + realizedPnl wallet'a yansıt.
    /// Paper-only. Net wallet etkisi = +margin + realizedPnl. AllocatedMargin
    /// 0 altına düşmez (defansif clamp).
    /// </summary>
    public void ReturnMarginAndApplyPnl(decimal margin, decimal realizedPnl, DateTimeOffset now)
    {
        if (Mode != TradingMode.Paper)
        {
            throw new DomainException(
                $"ReturnMarginAndApplyPnl only valid for Paper mode (was {Mode}).");
        }
        if (margin < 0m)
        {
            throw new DomainException("Margin must be non-negative.");
        }

        AllocatedMargin -= margin;
        if (AllocatedMargin < 0m) AllocatedMargin = 0m;  // defensive clamp
        WalletBalance += realizedPnl;  // realized P&L wallet'a yansıt
        Equity = WalletBalance + UnrealizedPnl;
        UpdatedAt = now;

        RaiseDomainEvent(new PositionMarginReturnedEvent(Mode, margin, realizedPnl, WalletBalance, now));
    }

    /// <summary>
    /// Loop 92 — 8h Funding fee uygulamasi. <c>fundingDelta</c> negatif ⇒ ödeme,
    /// pozitif ⇒ tahsilat. Paper-only. Wallet'a doğrudan etki, AllocatedMargin
    /// değişmez.
    /// </summary>
    public void ApplyFundingFee(decimal fundingDelta, DateTimeOffset now)
    {
        if (Mode != TradingMode.Paper)
        {
            throw new DomainException(
                $"ApplyFundingFee only valid for Paper mode (was {Mode}).");
        }

        WalletBalance += fundingDelta;
        Equity = WalletBalance + UnrealizedPnl;
        UpdatedAt = now;

        RaiseDomainEvent(new FundingFeeAppliedEvent(Mode, fundingDelta, WalletBalance, now));
    }

    /// <summary>
    /// Mark-to-market equity refresh. Loop 92'de UnrealizedPnl ayrı field'da
    /// tutulur, Equity = Wallet + UnrealizedPnl. Allowed for all modes.
    /// </summary>
    public void ApplyUnrealized(decimal unrealizedPnl, DateTimeOffset now)
    {
        UnrealizedPnl = unrealizedPnl;
        Equity = WalletBalance + unrealizedPnl;
        UpdatedAt = now;
        RaiseDomainEvent(new VirtualBalanceUpdatedEvent(Mode, WalletBalance, Equity));
    }

    private static int RiskProfileIdOf(TradingMode mode) => (int)mode;
}
