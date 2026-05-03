using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskProfileFuturesFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Leverage",
                table: "RiskProfiles",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "MaintenanceMarginRatio",
                table: "RiskProfiles",
                type: "decimal(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0.80m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxFundingFeePerHour",
                table: "RiskProfiles",
                type: "decimal(10,6)",
                precision: 10,
                scale: 6,
                nullable: false,
                defaultValue: 0.001m);

            migrationBuilder.UpdateData(
                table: "RiskProfiles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Leverage", "MaintenanceMarginRatio", "MaxFundingFeePerHour" },
                values: new object[] { 1, 0.80m, 0.001m });

            migrationBuilder.UpdateData(
                table: "RiskProfiles",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Leverage", "MaintenanceMarginRatio", "MaxFundingFeePerHour" },
                values: new object[] { 1, 0.80m, 0.001m });

            migrationBuilder.UpdateData(
                table: "RiskProfiles",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Leverage", "MaintenanceMarginRatio", "MaxFundingFeePerHour" },
                values: new object[] { 1, 0.80m, 0.001m });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Leverage",
                table: "RiskProfiles");

            migrationBuilder.DropColumn(
                name: "MaintenanceMarginRatio",
                table: "RiskProfiles");

            migrationBuilder.DropColumn(
                name: "MaxFundingFeePerHour",
                table: "RiskProfiles");
        }
    }
}
