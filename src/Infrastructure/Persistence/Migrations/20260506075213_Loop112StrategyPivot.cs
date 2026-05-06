using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Loop 112 — ADR-0027 strateji ailesi pivot.
    ///
    /// İki DML adım:
    /// <list type="number">
    ///   <item>
    ///     PatternComposite (Type=3) tüm Strategy satırlarının Status'ünü
    ///     Active (3) → Paused (2)'ye çek. ADR-0027 §27.7 Karar B: kod silinmez,
    ///     altyapı korunur, Status flip ile re-aktivasyon hazır.
    ///   </item>
    ///   <item>
    ///     5 yeni SwingTrade (Type=4) Strategy seed (Aile A). 5 coin (BTCUSDT,
    ///     ETHUSDT, XRPUSDT, SOLUSDT, ADAUSDT). Status=Active (3). Idempotent
    ///     guard: aynı Name varsa skip.
    ///   </item>
    /// </list>
    ///
    /// ParametersJson defaults binance-expert §7 / spec-binance-expert-strategies.md:
    /// EmaShortPeriod=20, EmaLongPeriod=50, Rsi/Atr/VolumeSma periyotları,
    /// VolumeSurgeMultiplier=1.5, RsiLongMin/Max=40/65, RsiShortMin/Max=35/60,
    /// SlAtrMul=1.5, RrRatio=2 (TpAtrMul=3), MaxHoldHours=8, BeMoveTriggerPct=0.01,
    /// TimeExitMinProfitPct=0.005.
    ///
    /// Veri silmez: StrategySignals/Positions/Orders audit korunur.
    /// </summary>
    public partial class Loop112StrategyPivot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) PatternComposite (Type=3) Active → Paused. Loop 81+ seed satırları.
            //    Diğer Status (Draft) zaten emit etmez — sadece Active'leri pause et.
            migrationBuilder.Sql(
                "UPDATE Strategies SET Status = 2 " +
                "WHERE Type = 3 AND Status = 3;");

            // 2) 5 yeni SwingTrade (Type=4) Strategy seed. Idempotent: aynı Name
            //    varsa INSERT atılmaz (re-run safe).
            //
            // ParametersJson tek satır JSON (CRLF/LF güvenliği için inline):
            //   - EmaShortPeriod=20, EmaLongPeriod=50
            //   - VolumeSmaPeriod=20, VolumeSurgeMultiplier=1.5
            //   - RsiPeriod=14, RsiLongMin=40, RsiLongMax=65, RsiShortMin=35, RsiShortMax=60
            //   - AtrPeriod=14, SlAtrMultiplier=1.5, TpAtrMultiplier=3.0 (R:R 1:2)
            //   - MaxHoldHours=8 (2 × 4h bar)
            //   - BeMoveTriggerPct=0.01 (%1 kar → BE)
            //   - BeMoveOffsetPct=0.001 (BE üstüne %0.10 buffer)
            //   - TimeExitMinProfitPct=0.005 (%0.5 kar + 8h hold ⇒ close)
            //   - CooldownBarsAfterSignal=1 (4h × 1 = 4h bar başına bir emit)
            const string SwingDefaults =
                "{\"EmaShortPeriod\":20,\"EmaLongPeriod\":50," +
                "\"VolumeSmaPeriod\":20,\"VolumeSurgeMultiplier\":1.5," +
                "\"RsiPeriod\":14,\"RsiLongMin\":40,\"RsiLongMax\":65," +
                "\"RsiShortMin\":35,\"RsiShortMax\":60," +
                "\"AtrPeriod\":14,\"SlAtrMultiplier\":1.5,\"TpAtrMultiplier\":3.0," +
                "\"MaxHoldHours\":8,\"BeMoveTriggerPct\":0.01,\"BeMoveOffsetPct\":0.001," +
                "\"TimeExitMinProfitPct\":0.005,\"CooldownBarsAfterSignal\":1}";

            string Insert(string name, string symbol) =>
                "IF NOT EXISTS (SELECT 1 FROM Strategies WHERE Name = N'" + name + "') " +
                "INSERT INTO Strategies (Name, Type, Status, SymbolsCsv, ParametersJson, CreatedAt, UpdatedAt, ActivatedAt) " +
                "VALUES (N'" + name + "', 4, 3, N'" + symbol + "', " +
                "N'" + SwingDefaults + "', SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME());";

            migrationBuilder.Sql(Insert("BTC-Swing", "BTCUSDT"));
            migrationBuilder.Sql(Insert("ETH-Swing", "ETHUSDT"));
            migrationBuilder.Sql(Insert("XRP-Swing", "XRPUSDT"));
            migrationBuilder.Sql(Insert("SOL-Swing", "SOLUSDT"));
            migrationBuilder.Sql(Insert("ADA-Swing", "ADAUSDT"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down: yeni SwingTrade satırlarını sil (Down rollback senaryosu),
            // PatternComposite Status'ünü Active'e geri çek.
            migrationBuilder.Sql(
                "DELETE FROM Strategies WHERE Type = 4 AND Name IN " +
                "(N'BTC-Swing', N'ETH-Swing', N'XRP-Swing', N'SOL-Swing', N'ADA-Swing');");
            migrationBuilder.Sql(
                "UPDATE Strategies SET Status = 3 WHERE Type = 3 AND Status = 2;");
        }
    }
}
