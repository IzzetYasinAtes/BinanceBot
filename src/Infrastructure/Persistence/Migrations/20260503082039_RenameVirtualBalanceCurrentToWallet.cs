using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameVirtualBalanceCurrentToWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CurrentBalance",
                table: "VirtualBalances",
                newName: "WalletBalance");

            migrationBuilder.AddColumn<decimal>(
                name: "AllocatedMargin",
                table: "VirtualBalances",
                type: "decimal(28,10)",
                precision: 28,
                scale: 10,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnrealizedPnl",
                table: "VirtualBalances",
                type: "decimal(28,10)",
                precision: 28,
                scale: 10,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.UpdateData(
                table: "VirtualBalances",
                keyColumn: "Id",
                keyValue: 1,
                columns: new string[0],
                values: new object[0]);

            migrationBuilder.UpdateData(
                table: "VirtualBalances",
                keyColumn: "Id",
                keyValue: 2,
                columns: new string[0],
                values: new object[0]);

            migrationBuilder.UpdateData(
                table: "VirtualBalances",
                keyColumn: "Id",
                keyValue: 3,
                columns: new string[0],
                values: new object[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllocatedMargin",
                table: "VirtualBalances");

            migrationBuilder.DropColumn(
                name: "UnrealizedPnl",
                table: "VirtualBalances");

            migrationBuilder.RenameColumn(
                name: "WalletBalance",
                table: "VirtualBalances",
                newName: "CurrentBalance");
        }
    }
}
