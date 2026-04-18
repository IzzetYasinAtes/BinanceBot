using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Risk;

/// <summary>
/// Configuration-driven defaults for the singleton-per-mode RiskProfile rows
/// (ADR-0011 §11.8 + decision-sizing.md Commit 9). Bound from "RiskProfile:Defaults".
/// </summary>
public sealed record RiskProfileDefaultsOptions
{
    public const string SectionName = "RiskProfile:Defaults";

    public decimal RiskPerTradePct { get; init; } = 0.01m;
    public decimal MaxPositionSizePct { get; init; } = 0.15m;
    public decimal MaxDrawdown24hPct { get; init; } = 0.05m;
    public decimal MaxDrawdownAllTimePct { get; init; } = 0.25m;
    public int MaxConsecutiveLosses { get; init; } = 3;
}

/// <summary>
/// Hosted service that ensures a RiskProfile row exists for every <see cref="TradingMode"/>
/// and synchronises its tunable limits with appsettings on every boot. Idempotent — a missing
/// row is created with <see cref="RiskProfile.CreateDefault"/>, an existing row is reconciled
/// via <see cref="RiskProfile.UpdateLimits"/> only when values diverge.
///
/// EF migration <c>HasData</c> seeds the rows on a fresh DB; this seeder keeps them in sync
/// across config changes without requiring a migration.
/// </summary>
public sealed class RiskProfileSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IOptions<RiskProfileDefaultsOptions> _options;
    private readonly IClock _clock;
    private readonly ILogger<RiskProfileSeeder> _logger;

    public RiskProfileSeeder(
        IServiceProvider services,
        IOptions<RiskProfileDefaultsOptions> options,
        IClock clock,
        ILogger<RiskProfileSeeder> logger)
    {
        _services = services;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var defaults = _options.Value;
        var now = _clock.UtcNow;

        var modes = new[]
        {
            TradingMode.Paper,
            TradingMode.LiveTestnet,
            TradingMode.LiveMainnet,
        };

        foreach (var mode in modes)
        {
            var id = RiskProfile.IdFor(mode);
            var existing = await db.RiskProfiles
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

            if (existing is null)
            {
                var profile = RiskProfile.CreateDefault(mode, now);
                profile.UpdateLimits(
                    defaults.RiskPerTradePct,
                    defaults.MaxPositionSizePct,
                    defaults.MaxDrawdown24hPct,
                    defaults.MaxDrawdownAllTimePct,
                    defaults.MaxConsecutiveLosses,
                    now);
                db.RiskProfiles.Add(profile);
                _logger.LogInformation(
                    "Seeded RiskProfile mode={Mode} riskPct={Risk} maxPosPct={Pos}",
                    mode, defaults.RiskPerTradePct, defaults.MaxPositionSizePct);
                continue;
            }

            var diverged =
                existing.RiskPerTradePct != defaults.RiskPerTradePct ||
                existing.MaxPositionSizePct != defaults.MaxPositionSizePct ||
                existing.MaxDrawdown24hPct != defaults.MaxDrawdown24hPct ||
                existing.MaxDrawdownAllTimePct != defaults.MaxDrawdownAllTimePct ||
                existing.MaxConsecutiveLosses != defaults.MaxConsecutiveLosses;

            if (diverged)
            {
                existing.UpdateLimits(
                    defaults.RiskPerTradePct,
                    defaults.MaxPositionSizePct,
                    defaults.MaxDrawdown24hPct,
                    defaults.MaxDrawdownAllTimePct,
                    defaults.MaxConsecutiveLosses,
                    now);
                _logger.LogInformation(
                    "Reconciled RiskProfile mode={Mode} from appsettings", mode);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
