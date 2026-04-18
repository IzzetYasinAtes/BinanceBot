using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Loop 14 (research-paper-live-and-sizing.md §C5): adds the per-mode
    /// MaxOpenPositions throttle to the RiskProfile aggregate. Default 2 is
    /// applied at column-level so any newly-inserted row picks the same ceiling
    /// even if the application skips the seeder; existing Paper / LiveTestnet /
    /// LiveMainnet singletons are explicitly backfilled below.
    /// </summary>
    public partial class AddMaxOpenPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxOpenPositions",
                table: "RiskProfiles",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.UpdateData(
                table: "RiskProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "MaxOpenPositions",
                value: 2);

            migrationBuilder.UpdateData(
                table: "RiskProfiles",
                keyColumn: "Id",
                keyValue: 2,
                column: "MaxOpenPositions",
                value: 2);

            migrationBuilder.UpdateData(
                table: "RiskProfiles",
                keyColumn: "Id",
                keyValue: 3,
                column: "MaxOpenPositions",
                value: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxOpenPositions",
                table: "RiskProfiles");
        }
    }
}
