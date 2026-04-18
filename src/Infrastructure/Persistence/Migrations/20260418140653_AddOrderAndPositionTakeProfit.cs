using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderAndPositionTakeProfit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SuggestedTakeProfit",
                table: "StrategySignals",
                type: "decimal(28,10)",
                precision: 28,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TakeProfit",
                table: "Positions",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TakeProfit",
                table: "Orders",
                type: "decimal(18,8)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuggestedTakeProfit",
                table: "StrategySignals");

            migrationBuilder.DropColumn(
                name: "TakeProfit",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "TakeProfit",
                table: "Orders");
        }
    }
}
