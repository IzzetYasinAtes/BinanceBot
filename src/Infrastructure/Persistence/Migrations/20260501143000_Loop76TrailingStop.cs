using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Loop 76 — Trailing-stop running peak. Position aggregate'a
    /// <c>PeakMarkPrice</c> NOT NULL DEFAULT 0 <c>decimal(18,8)</c> kolonu eklenir.
    /// 0 = trailing henüz peak yatırmamış (yeni open position default'u). BE move
    /// uygulanmadığı sürece <c>UpdatePeakAndCheckTrailing</c> dormant kalır;
    /// BE applied + ilk eligible tick peak'i mark'a yatırır.
    ///
    /// Spec (binance-expert quick-win): BE move sonrası dinamik kar koruma.
    /// trailingStop = peak × (1 − TrailPct) (default 0.0015 = %0.15).
    /// MarkToMarketWorker hook BE'den SONRA çağırır (sıra kritik).
    ///
    /// Down: kolon drop (mevcut açık pozisyonların PeakMarkPrice yatırımı kaybolur,
    /// trailing kapanır). Up idempotent — pre-Loop 76 rows backfill 0m yapılır.
    /// </summary>
    public partial class Loop76TrailingStop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PeakMarkPrice",
                table: "Positions",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PeakMarkPrice",
                table: "Positions");
        }
    }
}
