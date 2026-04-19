using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0015 §15.9 — strategy surface reset (second pass, supersedes ADR-0014
    /// Loop16PatternScalpingReset). Enum value <c>1</c> is reused
    /// (<c>PatternScalping</c> → <c>VwapEmaHybrid</c>); existing
    /// <c>Strategies</c>/<c>StrategySignals</c> rows refer to the obsolete
    /// evaluator and must not survive the migration. Open positions detach via
    /// <c>StrategyId = NULL</c>; historical signals are dropped.
    ///
    /// Schema is untouched — model snapshot deltas are intentionally empty; all
    /// work is data-layer SQL.
    /// </summary>
    public partial class Loop19VwapEmaHybridReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM StrategySignals;");
            migrationBuilder.Sql("UPDATE Positions SET StrategyId = NULL;");
            migrationBuilder.Sql("DELETE FROM Strategies;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Destructive data wipe is not reversed — the legacy PatternScalping
            // evaluator + 14 detector family have been deleted from the code
            // surface, so re-seeding those rows would produce orphans. Operators
            // needing a downgrade path must restore from a pre-Loop-19 backup.
        }
    }
}
