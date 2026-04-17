using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.ValueObjects;

public sealed class Symbol : ValueObject
{
    public string Value { get; }

    private Symbol(string value) => Value = value;

    public static Symbol From(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainException("Symbol cannot be empty.");
        }

        var normalized = raw.Trim().ToUpperInvariant();
        if (normalized.Length is < 5 or > 20)
        {
            throw new DomainException($"Symbol '{normalized}' length invalid (expected 5-20).");
        }

        return new Symbol(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
