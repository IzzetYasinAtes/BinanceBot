using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.ValueObjects;

public sealed class Percentage : ValueObject
{
    public decimal Value { get; }

    private Percentage(decimal value) => Value = value;

    public static Percentage From(decimal raw)
    {
        if (raw is < 0m or > 100m)
        {
            throw new DomainException($"Percentage must be between 0 and 100 (got {raw}).");
        }

        return new Percentage(raw);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{Value:0.##}%";
}
