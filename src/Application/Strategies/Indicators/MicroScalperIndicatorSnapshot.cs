namespace BinanceBot.Application.Strategies.Indicators;

/// <summary>
/// ADR-0018 §18.11 — 30sn bar bazında hesaplanmış mikro-scalper indicator snapshot.
/// Ayrı tip tutulması SRP + null-safety gerekçeli: eski <see cref="MarketIndicatorSnapshot"/>
/// (1m VWAP + 1h EMA21 + SwingHigh20) ile <see cref="MicroScalperIndicatorSnapshot"/>
/// (30s VWAP + 30s EMA20 + VolumeSMA20) farklı buffer + warmup + bar semantiklerine
/// bağlı — tek snapshot'ta nullable alan koleksiyonu tutmak evaluator caller'ı
/// "hangi alan null olabilir?" branch'ına sokar (ADR-0018 §18.11 Alt-J reddi).
/// </summary>
/// <param name="Vwap">Rolling 15-bar (7.5 dk) typical-price VWAP.</param>
/// <param name="PrevBarClose">Kapalı son iki 30s bar'ından önceki bar kapanışı.</param>
/// <param name="LastBarClose">Kapalı son 30s bar kapanışı — entry referansı.</param>
/// <param name="LastBarVolume">Son 30s bar hacmi — volume filtresine girer.</param>
/// <param name="VolumeSma20">Son 20 bar (10 dk) volume SMA.</param>
/// <param name="Ema20Now">Son bar EMA20 değeri.</param>
/// <param name="Ema20Prev">Bir önceki bar EMA20 değeri — slope gate için.</param>
/// <param name="AsOf">Son kapalı 30s bar close time.</param>
public sealed record MicroScalperIndicatorSnapshot(
    decimal Vwap,
    decimal PrevBarClose,
    decimal LastBarClose,
    decimal LastBarVolume,
    decimal VolumeSma20,
    decimal Ema20Now,
    decimal Ema20Prev,
    DateTimeOffset AsOf);
