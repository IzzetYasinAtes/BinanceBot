using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPositionStrategyRiskBacktest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BacktestRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StrategyId = table.Column<long>(type: "bigint", nullable: false),
                    FromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InitialBalance = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    FinalBalance = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Sharpe = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    MaxDrawdownPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    WinRate = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientOrderId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ExchangeOrderId = table.Column<long>(type: "bigint", nullable: true),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    TimeInForce = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    StopPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    ExecutedQuantity = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    CumulativeQuoteQty = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StrategyId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Positions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    AverageEntryPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    ExitPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    MarkPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    UnrealizedPnl = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    RealizedPnl = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    StrategyId = table.Column<long>(type: "bigint", nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    RiskPerTradePct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    MaxPositionSizePct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    MaxDrawdown24hPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    MaxDrawdownAllTimePct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    MaxConsecutiveLosses = table.Column<int>(type: "int", nullable: false),
                    RiskPerTradeCap = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    MaxPositionCap = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    CapsAdminNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CircuitBreakerStatus = table.Column<int>(type: "int", nullable: false),
                    CircuitBreakerTrippedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CircuitBreakerReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConsecutiveLosses = table.Column<int>(type: "int", nullable: false),
                    RealizedPnl24h = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    RealizedPnlAllTime = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    PeakEquity = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    CurrentDrawdownPct = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Strategies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SymbolsCsv = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Strategies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BacktestTrades",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BacktestRunId = table.Column<long>(type: "bigint", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Pnl = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestTrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BacktestTrades_BacktestRuns_BacktestRunId",
                        column: x => x.BacktestRunId,
                        principalTable: "BacktestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderFills",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    ExchangeTradeId = table.Column<long>(type: "bigint", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    CommissionAsset = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FilledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderFills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderFills_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StrategySignals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StrategyId = table.Column<long>(type: "bigint", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BarOpenTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    SuggestedQuantity = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    SuggestedPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    SuggestedStopPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    ContextJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategySignals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategySignals_Strategies_StrategyId",
                        column: x => x.StrategyId,
                        principalTable: "Strategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "RiskProfiles",
                columns: new[] { "Id", "CapsAdminNote", "CircuitBreakerReason", "CircuitBreakerStatus", "CircuitBreakerTrippedAt", "ConsecutiveLosses", "CurrentDrawdownPct", "MaxConsecutiveLosses", "MaxDrawdown24hPct", "MaxDrawdownAllTimePct", "MaxPositionCap", "MaxPositionSizePct", "PeakEquity", "RealizedPnl24h", "RealizedPnlAllTime", "RiskPerTradeCap", "RiskPerTradePct", "UpdatedAt" },
                values: new object[] { 1, null, null, 1, null, 0, 0m, 3, 0.05m, 0.25m, null, 0.10m, 0m, 0m, 0m, null, 0.01m, new DateTimeOffset(new DateTime(2026, 4, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.CreateIndex(
                name: "IX_BacktestRuns_Status",
                table: "BacktestRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BacktestRuns_Strategy_Started",
                table: "BacktestRuns",
                columns: new[] { "StrategyId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_BacktestTrades_Run_Seq",
                table: "BacktestTrades",
                columns: new[] { "BacktestRunId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_OrderFills_Order_ExchangeTrade",
                table: "OrderFills",
                columns: new[] { "OrderId", "ExchangeTradeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ExchangeOrderId",
                table: "Orders",
                column: "ExchangeOrderId",
                filter: "[ExchangeOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StrategyId",
                table: "Orders",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Symbol_Status_Updated",
                table: "Orders",
                columns: new[] { "Symbol", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Orders_ClientOrderId",
                table: "Orders",
                column: "ClientOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Positions_Status_Updated",
                table: "Positions",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Positions_StrategyId",
                table: "Positions",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "UX_Positions_Symbol_Open",
                table: "Positions",
                column: "Symbol",
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Strategies_Status",
                table: "Strategies",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_Strategies_Name",
                table: "Strategies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategySignals_Strategy_Emitted",
                table: "StrategySignals",
                columns: new[] { "StrategyId", "EmittedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_StrategySignals_Bar",
                table: "StrategySignals",
                columns: new[] { "StrategyId", "BarOpenTime", "Symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BacktestTrades");

            migrationBuilder.DropTable(
                name: "OrderFills");

            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "RiskProfiles");

            migrationBuilder.DropTable(
                name: "StrategySignals");

            migrationBuilder.DropTable(
                name: "BacktestRuns");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Strategies");
        }
    }
}
