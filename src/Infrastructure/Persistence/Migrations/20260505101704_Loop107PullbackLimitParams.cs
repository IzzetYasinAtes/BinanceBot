using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Loop 107 / ADR-0026 §A — Pullback Limit Order parametre seed (Strategies
    /// ParametersJson DML). PatternComposite (Type=3) tüm aktif stratejilerin
    /// ParametersJson'una <c>PullbackOffsetPct</c> + <c>PullbackTimeoutMinutes</c>
    /// alanları JSON_MODIFY ile merge edilir.
    ///
    /// StrategySeeder Status=Active satırlarını güncellemediği için (UpdateParameters
    /// "Cannot update parameters while Active" guard) DML migration zorunlu.
    /// Schema değişmez; pure DML.
    /// </summary>
    public partial class Loop107PullbackLimitParams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PullbackOffsetPct (0.001 = %0.10) — Loop 107 default.
            migrationBuilder.Sql(
                "UPDATE Strategies " +
                "SET ParametersJson = JSON_MODIFY(" +
                    "ISNULL(ParametersJson, N'{}'), " +
                    "'$.PullbackOffsetPct', " +
                    "CAST(0.001 AS float)" +
                ") " +
                "WHERE Type = 3;");

            // PullbackTimeoutMinutes (5dk) — Loop 107 default.
            migrationBuilder.Sql(
                "UPDATE Strategies " +
                "SET ParametersJson = JSON_MODIFY(" +
                    "ISNULL(ParametersJson, N'{}'), " +
                    "'$.PullbackTimeoutMinutes', " +
                    "5" +
                ") " +
                "WHERE Type = 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE Strategies " +
                "SET ParametersJson = JSON_MODIFY(ParametersJson, '$.PullbackOffsetPct', NULL) " +
                "WHERE Type = 3;");

            migrationBuilder.Sql(
                "UPDATE Strategies " +
                "SET ParametersJson = JSON_MODIFY(ParametersJson, '$.PullbackTimeoutMinutes', NULL) " +
                "WHERE Type = 3;");
        }
    }
}
