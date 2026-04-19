using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0016 §16.7 — VwapEmaHybrid V2 parameter tuning re-seed. Schema is
    /// unchanged (ParametersJson is opaque nvarchar(max)); this migration only
    /// clears the ADR-0015 rows so the <c>StrategySeeder</c> repopulates the
    /// three BTC/BNB/XRP seeds with the new slope/zone/TP fields on next boot.
    ///
    /// Loop 20 closed with zero open positions, so the <c>Positions</c> UPDATE
    /// is a safety net rather than a correctness requirement. The sequence is
    /// identical to ADR-0015 §15.9 / Loop19VwapEmaHybridReset.
    /// </summary>
    public partial class Loop21VwapEmaHybridReseed : Migration
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
            // Destructive data wipe is not reversed. Operators needing a rollback
            // path must restore from a pre-Loop-21 backup.
        }
    }
}
