using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Loop 95 — Long-only emit tune. Aktif PatternComposite stratejilerinin
    /// ParametersJson'una <c>WeightOverrides</c> dictionary'si JSON_MODIFY ile
    /// merge edilir; 7 Short-bias detector ağırlığı 0.0'a çekilir
    /// (BearishEngulfing, ShootingStar, BollingerUpperReversal,
    /// BollingerSqueezeBreakDown, RsiOverboughtPullback, Ema9SlopeDown,
    /// DonchianBreakdown).
    ///
    /// Anahtarlar detector.Name (snake_case) ile eşleşir
    /// (WeightedScorePatternComposer.ResolveWeight).
    ///
    /// StrategySeeder Status=Active satırları ParametersJson update etmediği
    /// için DML migration zorunlu.
    ///
    /// Schema değişmez; pure DML. Down: WeightOverrides anahtarını siler.
    /// </summary>
    public partial class Loop95LongOnlyWeightOverrides : Migration
    {
        private const string OverridesJson =
            "{" +
                "\"bearish_engulfing\":0.0," +
                "\"shooting_star\":0.0," +
                "\"bollinger_upper_reversal\":0.0," +
                "\"bollinger_squeeze_break_down\":0.0," +
                "\"rsi_overbought_pullback\":0.0," +
                "\"ema9_slope_down\":0.0," +
                "\"donchian_breakdown\":0.0" +
            "}";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PatternComposite (Type=3) satırlarına WeightOverrides JSON'unu
            // JSON_QUERY ile object literal olarak yaz. lax mod: yoksa ekler,
            // varsa replace eder.
            migrationBuilder.Sql(
                "UPDATE Strategies " +
                "SET ParametersJson = JSON_MODIFY(" +
                    "ISNULL(ParametersJson, N'{}'), " +
                    "'$.WeightOverrides', " +
                    "JSON_QUERY(N'" + OverridesJson + "')" +
                ") " +
                "WHERE Type = 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WeightOverrides anahtarını sil (NULL set + lax mode → key removal).
            migrationBuilder.Sql(
                "UPDATE Strategies " +
                "SET ParametersJson = JSON_MODIFY(ParametersJson, '$.WeightOverrides', NULL) " +
                "WHERE Type = 3;");
        }
    }
}
