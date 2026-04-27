namespace BinanceBot.Application.Strategies.Cooldowns;

/// <summary>
/// Loop 42 P0 fix — evaluator-level signal cooldown enforcement abstraction.
///
/// Loop 41 t=210 HALT root cause (LTC 7 ardışık SL whipsaw): cooldown V1
/// "stateless" tasarım kararı evaluator'da koruma bırakmamıştı, her 1m kline
/// tetiğinde aynı 15m bar üst kırılım koşulları matematiksel olarak yeniden
/// karşılandığında sinyal tekrar üretildi (entry $56.13-15 / SL $56.11-12 sabit).
/// MaxOpenPositions sliding window pozisyon SL'de kapanır kapanmaz tekrar
/// serbest kaldı; risk profili tek başına yeterli olmadı.
///
/// Bu abstraction <see cref="BinanceBot.Application.Strategies.Evaluation.IStrategyEvaluator"/>
/// implementation'larının (Infrastructure tarafında) sinyal koşulları sağlandıktan
/// HEMEN ÖNCE ve sonra çağırdığı protokoldür:
///   - <see cref="IsCooldown"/> — true ise sinyal SKIP (no-op log + null evaluation).
///   - <see cref="RecordSignal"/> — sinyal yayınlandıktan sonra last-emitted-at
///     timestamp'ini kaydeder.
///
/// Implementation Infrastructure'da singleton in-memory dictionary olarak yaşar;
/// kalıcılık beklenmez (process restart sonrası ilk bar pencereyi resetler — bu
/// kabul edilebilir, çünkü cooldown sliding window 60-90dk seviyesindedir ve
/// boot sonrası warmup zaten gecikme yaratır).
/// </summary>
public interface ICooldownService
{
    /// <summary>
    /// Verilen (strategyId, symbol) için son sinyal zamanından beri geçen süre
    /// <c>cooldownBars × barMinutes</c> dakikadan azsa <c>true</c> döner ve
    /// evaluator sinyali skip etmelidir. Hiç sinyal kaydedilmemişse <c>false</c>.
    /// </summary>
    /// <param name="strategyId">Strateji aggregate id.</param>
    /// <param name="symbol">Trade sembolü (örn. "LTCUSDT") — case-insensitive
    /// karşılaştırma için implementation upper-case normalize eder.</param>
    /// <param name="cooldownBars">Cooldown bar sayısı (parametre).
    /// 0 veya negatif ⇒ cooldown disable (her zaman <c>false</c>).</param>
    /// <param name="barMinutes">Bir bar'ın süresi (15m strateji için 15).</param>
    /// <param name="now">Şu anki zaman (clock injection için parametre).</param>
    bool IsCooldown(
        long strategyId,
        string symbol,
        int cooldownBars,
        int barMinutes,
        DateTimeOffset now);

    /// <summary>
    /// Sinyal yayını (Emit) sonrası last-signal-at timestamp'ini günceller.
    /// Sonraki <see cref="IsCooldown"/> çağrıları bu zamanı baz alır.
    /// </summary>
    void RecordSignal(long strategyId, string symbol, DateTimeOffset now);
}
