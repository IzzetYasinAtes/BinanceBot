using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Loop 75 — Break-even SL move audit timestamp. Position aggregate'a
    /// <c>BreakEvenAppliedAt</c> nullable <c>datetimeoffset</c> kolonu eklenir.
    /// Null = BE move henüz uygulanmadı (yeni open position default'u). Set ise
    /// <see cref="BinanceBot.Domain.Positions.Position.MoveStopToBreakEven"/> bir
    /// daha tetiklenmez (aggregate-side idempotency).
    ///
    /// Spec (binance-expert): Loop 72-73 timestop slow-bleed pattern fix.
    /// MarkToMarketWorker hook'u markPrice ≥ entry × (1+TriggerPct) olduğunda
    /// StopPrice'ı entry × (1+OffsetPct)'e taşır; mevcut açık pozisyonlar yeni
    /// kolon nullable=true olduğu için backfill gerektirmez.
    ///
    /// Down: kolon drop (geri alınabilir — veri kaybı sadece audit timestamp).
    /// </summary>
    public partial class Loop75BreakEvenSL : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BreakEvenAppliedAt",
                table: "Positions",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BreakEvenAppliedAt",
                table: "Positions");
        }
    }
}
