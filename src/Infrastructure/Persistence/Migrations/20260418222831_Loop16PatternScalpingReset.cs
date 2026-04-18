using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0014 Loop 16 — pattern-based scalping reform.
    /// (a) adds <c>Positions.MaxHoldDurationSeconds</c> for the pattern time stop, and
    /// (b) wipes the legacy strategy surface (Grid / TrendFollowing / MeanReversion)
    /// so the new <c>StrategyConfigSeeder</c> can seed the 3 PatternScalper rows on
    /// boot. Open positions detach from the deleted strategy via <c>StrategyId = NULL</c>;
    /// historical signals are dropped (cascade-equivalent — old rows reference vanished
    /// strategy ids).
    /// </summary>
    public partial class Loop16PatternScalpingReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // (a) Pattern-based time stop column. TimeSpan? in domain is persisted as
            // bigint? seconds via the configured value converter (PositionConfiguration).
            migrationBuilder.AddColumn<long>(
                name: "MaxHoldDurationSeconds",
                table: "Positions",
                type: "bigint",
                nullable: true);

            // (b) ADR-0014 §14.1: strategy surface reset. Old enum values (Grid=1,
            // TrendFollowing=2, MeanReversion=3) are removed; PatternScalping reuses 1.
            // Wipe history first so the seeder can re-insert clean rows on next boot.
            migrationBuilder.Sql("DELETE FROM StrategySignals;");
            migrationBuilder.Sql("UPDATE Positions SET StrategyId = NULL;");
            migrationBuilder.Sql("DELETE FROM Strategies;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Symmetric drop — the SQL data wipe is intentionally not reversed; the
            // legacy Grid/Trend/MeanRev evaluators no longer exist in code, so re-
            // seeding their rows would create orphaned strategies. Operators that
            // need a downgrade must manually restore from a pre-Loop-16 backup.
            migrationBuilder.DropColumn(
                name: "MaxHoldDurationSeconds",
                table: "Positions");
        }
    }
}
