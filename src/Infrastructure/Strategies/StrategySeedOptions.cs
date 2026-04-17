using System.ComponentModel.DataAnnotations;

namespace BinanceBot.Infrastructure.Strategies;

public sealed class StrategySeedOptions
{
    public const string SectionName = "Strategies";

    public StrategySeedEntry[] Seed { get; init; } = [];
}

public sealed class StrategySeedEntry
{
    [Required]
    public string Name { get; init; } = default!;

    [Required]
    public string Type { get; init; } = default!;

    [Required]
    [MinLength(1)]
    public string[] Symbols { get; init; } = [];

    public string ParametersJson { get; init; } = "{}";

    public bool Activate { get; init; } = true;
}
