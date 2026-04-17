using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookTickers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BidPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    BidQuantity = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    AskPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    AskQuantity = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    UpdateId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookTickers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Instruments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BaseAsset = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    QuoteAsset = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TickSize = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    StepSize = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    MinNotional = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    MinQty = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    MaxQty = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instruments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Klines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Interval = table.Column<int>(type: "int", nullable: false),
                    OpenTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CloseTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OpenPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    HighPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    LowPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    ClosePrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Volume = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    QuoteVolume = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    TradeCount = table.Column<int>(type: "int", nullable: false),
                    TakerBuyBaseVolume = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    TakerBuyQuoteVolume = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Klines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderBookSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastUpdateId = table.Column<long>(type: "bigint", nullable: false),
                    BidsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AsksJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderBookSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_BookTickers_Symbol",
                table: "BookTickers",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Instruments_Symbol",
                table: "Instruments",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Klines_ReadPath",
                table: "Klines",
                columns: new[] { "Symbol", "Interval", "IsClosed", "OpenTime" });

            migrationBuilder.CreateIndex(
                name: "UX_Klines_Symbol_Interval_OpenTime",
                table: "Klines",
                columns: new[] { "Symbol", "Interval", "OpenTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderBookSnapshots_Symbol_CapturedAt",
                table: "OrderBookSnapshots",
                columns: new[] { "Symbol", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_EventType_OccurredAt",
                table: "SystemEvents",
                columns: new[] { "EventType", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_OccurredAt",
                table: "SystemEvents",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookTickers");

            migrationBuilder.DropTable(
                name: "Instruments");

            migrationBuilder.DropTable(
                name: "Klines");

            migrationBuilder.DropTable(
                name: "OrderBookSnapshots");

            migrationBuilder.DropTable(
                name: "SystemEvents");
        }
    }
}
