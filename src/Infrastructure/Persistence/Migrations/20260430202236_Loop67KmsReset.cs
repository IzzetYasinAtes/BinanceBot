using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Loop 67 — KlineMomentumSpread5m pivot. Eski 7 strateji ailesi (VwapEma /
    /// MicroScalper / AtrScalper / Donchian / BbMeanRev / EmaScalper /
    /// HybridMomentum) kod yüzeyinden silindi (CLAUDE.md altın kural #13).
    /// Bu migration <c>StrategyType</c> enum ordinali <c>1</c> tek değere
    /// indirgendiği için (önceki 7 değer 1..7) reseed güvenliği açısından
    /// strateji + sinyal + pozisyon + emir tablolarını sıfırlar.
    ///
    /// Schema değişmez; pure DML reset. Sırayla: <c>OrderFills</c> →
    /// <c>Orders</c> → <c>Positions</c> → <c>StrategySignals</c> →
    /// <c>Strategies</c> (FK bağımlı sırada). <c>StrategySeeder</c> boot'ta
    /// yeni 5 KMS girdisini ekler.
    ///
    /// Down: no-op. Geri alma anlamlı değil — silinen satırlar geri yazılamaz
    /// ve eski strateji kodları artık derlenmiyor.
    /// </summary>
    public partial class Loop67KmsReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FK bağımlı sırada DELETE — child önce, parent sonra.
            migrationBuilder.Sql("DELETE FROM OrderFills;");
            migrationBuilder.Sql("DELETE FROM Orders;");
            migrationBuilder.Sql("DELETE FROM Positions;");
            migrationBuilder.Sql("DELETE FROM StrategySignals;");
            migrationBuilder.Sql("DELETE FROM Strategies;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reset migration — geri alma desteklenmez (silinen veri kalıcı).
        }
    }
}
