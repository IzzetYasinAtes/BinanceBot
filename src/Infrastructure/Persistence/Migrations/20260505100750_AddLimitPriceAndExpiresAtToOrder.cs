using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLimitPriceAndExpiresAtToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LimitPrice",
                table: "Orders",
                type: "decimal(28,10)",
                precision: 28,
                scale: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Pending_Limit_Filtered",
                table: "Orders",
                columns: new[] { "Status", "Type", "ExpiresAt" },
                filter: "[Status] = 1 AND [Type] = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_Pending_Limit_Filtered",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LimitPrice",
                table: "Orders");
        }
    }
}
