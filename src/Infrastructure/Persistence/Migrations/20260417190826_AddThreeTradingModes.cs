using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddThreeTradingModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Positions_Symbol_Open",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "UX_Orders_ClientOrderId",
                table: "Orders");

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "Positions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "VirtualBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    StartingBalance = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Equity = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    IterationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastResetAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResetCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualBalances", x => x.Id);
                    table.CheckConstraint("CK_VirtualBalances_ModeIdParity", "[Id] = [Mode]");
                });

            migrationBuilder.InsertData(
                table: "RiskProfiles",
                columns: new[] { "Id", "CapsAdminNote", "CircuitBreakerReason", "CircuitBreakerStatus", "CircuitBreakerTrippedAt", "ConsecutiveLosses", "CurrentDrawdownPct", "MaxConsecutiveLosses", "MaxDrawdown24hPct", "MaxDrawdownAllTimePct", "MaxPositionCap", "MaxPositionSizePct", "PeakEquity", "RealizedPnl24h", "RealizedPnlAllTime", "RiskPerTradeCap", "RiskPerTradePct", "UpdatedAt" },
                values: new object[,]
                {
                    { 2, null, null, 1, null, 0, 0m, 3, 0.05m, 0.25m, null, 0.10m, 0m, 0m, 0m, null, 0.01m, new DateTimeOffset(new DateTime(2026, 4, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 3, null, null, 1, null, 0, 0m, 3, 0.05m, 0.25m, null, 0.10m, 0m, 0m, 0m, null, 0.01m, new DateTimeOffset(new DateTime(2026, 4, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                table: "VirtualBalances",
                columns: new[] { "Id", "CurrentBalance", "Equity", "IterationId", "LastResetAt", "Mode", "ResetCount", "StartedAt", "StartingBalance", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, 100m, 100m, new Guid("11111111-1111-1111-1111-111111111111"), null, 1, 0, new DateTimeOffset(new DateTime(2026, 4, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 100m, new DateTimeOffset(new DateTime(2026, 4, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 2, 0m, 0m, new Guid("22222222-2222-2222-2222-222222222222"), null, 2, 0, new DateTimeOffset(new DateTime(2026, 4, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0m, new DateTimeOffset(new DateTime(2026, 4, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 3, 0m, 0m, new Guid("33333333-3333-3333-3333-333333333333"), null, 3, 0, new DateTimeOffset(new DateTime(2026, 4, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0m, new DateTimeOffset(new DateTime(2026, 4, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Positions_Mode_Status",
                table: "Positions",
                columns: new[] { "Mode", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Positions_Symbol_Mode_Open",
                table: "Positions",
                columns: new[] { "Symbol", "Mode" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Orders_ClientOrderId_Mode",
                table: "Orders",
                columns: new[] { "ClientOrderId", "Mode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VirtualBalances");

            migrationBuilder.DropIndex(
                name: "IX_Positions_Mode_Status",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "UX_Positions_Symbol_Mode_Open",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "UX_Orders_ClientOrderId_Mode",
                table: "Orders");

            migrationBuilder.DeleteData(
                table: "RiskProfiles",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "RiskProfiles",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "UX_Positions_Symbol_Open",
                table: "Positions",
                column: "Symbol",
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Orders_ClientOrderId",
                table: "Orders",
                column: "ClientOrderId",
                unique: true);
        }
    }
}
