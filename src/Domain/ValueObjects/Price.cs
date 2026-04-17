using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.ValueObjects;

public sealed class Price : ValueObject
{
    public decimal Value { get; }

    private Price(decimal value) => Value = value;

    public static Price From(decimal raw)
    {
        if (raw < 0m)
        {
            throw new DomainException("Price cannot be negative.");
        }

        return new Price(raw);
    }

    public static Price Zero { get; } = new(0m);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString("0.##########");
}
