using FluentValidation;

namespace BinanceBot.Application.MarketData.Queries.GetKlines;

public sealed class GetKlinesQueryValidator : AbstractValidator<GetKlinesQuery>
{
    private static readonly HashSet<string> AllowedIntervals =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "1m", "3m", "5m", "15m", "30m", "1h", "4h", "1d"
        };

    public GetKlinesQueryValidator()
    {
        RuleFor(q => q.Symbol)
            .NotEmpty()
            .Matches("^[A-Z0-9]{5,20}$")
            .WithMessage("Symbol must be uppercase alphanumeric, 5-20 chars.");

        RuleFor(q => q.Interval)
            .Must(i => AllowedIntervals.Contains(i))
            .WithMessage($"Interval must be one of: {string.Join(",", AllowedIntervals)}");

        RuleFor(q => q.Limit)
            .InclusiveBetween(1, 1000);

        RuleFor(q => q)
            .Must(q => q.EndTime is null || q.StartTime is null || q.EndTime >= q.StartTime)
            .WithMessage("EndTime must be >= StartTime.");
    }
}
