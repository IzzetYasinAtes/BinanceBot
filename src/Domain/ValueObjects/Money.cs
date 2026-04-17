using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.ValueObjects;

public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money From(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new DomainException("Currency cannot be empty.");
        }

        return new Money(amount, currency.Trim().ToUpperInvariant());
    }

    public static Money Usd(decimal amount) => From(amount, "USDT");

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:0.########} {Currency}";
}
