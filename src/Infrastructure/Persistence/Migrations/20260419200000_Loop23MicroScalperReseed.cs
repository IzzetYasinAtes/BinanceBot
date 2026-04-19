using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0018 §18.14 — MicroScalper reseed. Schema is unchanged
    /// (ParametersJson is opaque nvarchar(max) + StrategyType enum value
    /// <c>MicroScalperVwapEma30s = 2</c> re-uses the existing int column); this
    /// migration only clears the ADR-0016 VwapEma rows so the
    /// <c>StrategySeeder</c> repopulates the four BTC/ETH/BNB/XRP MicroScalper
    /// seeds on next boot.
    ///
    /// Loop 22c halted with open BNB position, so the <c>Positions</c> UPDATE
    /// null-out is a correctness requirement this time (FK to Strategies).
    /// The sequence matches ADR-0015 §15.9 + ADR-0016 §16.7 + ADR-0017 reset
    /// pattern so operators see a uniform reseed shape across loops.
    /// </summary>
    public partial class Loop23MicroScalperReseed : Migration
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
            // path must restore from a pre-Loop-23 backup.
        }
    }
}
