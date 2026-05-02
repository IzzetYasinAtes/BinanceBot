using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Loop 81 — Pattern-composite scalping pivot (ADR-0024 supersedes ADR-0014).
    /// KMS (StrategyType=1) + BBR (StrategyType=2) silindi; tek strateji ailesi
    /// PatternComposite (=3) kalır. Önceki strateji satırları artık derlenmeyen
    /// enum değerlerine refer ettiği için DML reset zorunlu (Loop67KmsReset
    /// pattern'ı tekrar uygulanır).
    ///
    /// Schema değişmez; pure DML. Sırayla: OrderFills → Orders → Positions →
    /// StrategySignals → Strategies (FK-bağımlı sırada). StrategySeeder boot'ta
    /// 5 yeni PatternComposite girdisini ekler.
    ///
    /// Down: no-op. Geri alma anlamlı değil (silinen veri kalıcı).
    /// </summary>
    public partial class Loop81PatternPivot : Migration
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
