using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Loop 92 — Spot → Futures pivot reset (ADR-0025). Önceki Spot mode trade
    /// state'ini temizler. Loop 91 zaten DB'yi sıfırlamıştı (commit c3b2fe6),
    /// fakat bu migration idempotent — boş tablo durumunda no-op.
    ///
    /// Silinen tablolar:
    /// <list type="bullet">
    ///   <item>OrderFills (FK Orders → cascade güvenli, önce sil)</item>
    ///   <item>Orders (Position/Strategy referansları)</item>
    ///   <item>Positions (mark/exit/PnL hepsi)</item>
    ///   <item>StrategySignals</item>
    ///   <item>SystemEvents trade-related (eventType LIKE 'order.%' OR 'position.%' OR 'strategy.signal%')</item>
    /// </list>
    ///
    /// VirtualBalance NOT touched — wallet/margin reset Loop 92 boot'ta
    /// ResetForIteration komutu üzerinden yapılır (admin endpoint).
    /// RiskProfile counter reset için ayrıca CB reset endpoint var.
    /// </summary>
    public partial class Loop92FuturesPivotReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM OrderFills;");
            migrationBuilder.Sql("DELETE FROM Orders;");
            migrationBuilder.Sql("DELETE FROM Positions;");
            migrationBuilder.Sql("DELETE FROM StrategySignals;");
            migrationBuilder.Sql(
                "DELETE FROM SystemEvents WHERE EventType LIKE 'order.%' " +
                "OR EventType LIKE 'position.%' " +
                "OR EventType LIKE 'strategy.signal%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Veri silici migration — rollback yok. Yeni Loop 93+ migration eklemeli.
        }
    }
}
