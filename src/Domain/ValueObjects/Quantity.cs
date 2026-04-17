using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.ValueObjects;

public sealed class Quantity : ValueObject
{
    public decimal Value { get; }

    private Quantity(decimal value) => Value = value;

    public static Quantity From(decimal raw)
    {
        if (raw < 0m)
        {
            throw new DomainException("Quantity cannot be negative.");
        }

        return new Quantity(raw);
    }

    public static Quantity Zero { get; } = new(0m);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString("0.########");
}
