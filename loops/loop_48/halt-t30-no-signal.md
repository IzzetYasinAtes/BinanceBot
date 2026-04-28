# Loop 48 — Halt @ t=30dk (2026-04-28 12:45 TR) — 0 SİNYAL

## Halt Sebebi
Orta yol parametre (RSI 42-63, Vol×1.0, MinAtr 0.0004, MaxHold 10dk) **30dk'da 0 sinyal**. Filtre değil, daha derin sorun.

## EmaScalper1m 3-Konfigürasyon Karşılaştırma

| Loop | RSI | Vol× | MinAtr | MaxHold | TpAtr | Sinyal/30dk | Saat (TR) |
|---|---|---|---|---|---|---|---|
| 46 (gevşek) | 40-65 | 0.8 | 0.0003 | 8 | 1.5 | **10** | 10:35 |
| 47 (sıkı) | 45-60 | 1.2 | 0.0005 | 12 | 1.2 | **1** | 11:40 |
| 48 (orta) | 42-63 | 1.0 | 0.0004 | 10 | 1.2 | **0** | 12:45 |

3 farklı parametre seti, 3 farklı saat → frekans **10 → 1 → 0** monoton düşüş.

**İki olası açıklama:**
1. **Saat etkisi:** Avrupa açılışı sonrası flash spike (10:00 TR'de) → piyasa konsolidasyon (11:00-13:00 TR). EmaScalper crossover bu rejimde dead zone.
2. **Strateji mimarisi sorunlu:** EmaScalper1m hangi parametre seti olursa olsun production-grade değil. Loop 41-47 25 trade %16 WR — strateji başarısızlığı kanıtlandı.

## Halt Kriter
| Kriter | Durum |
|---|---|
| Realized < -$1.50 | $0 ✓ |
| Signals 0 (30dk) | ❌ HALT |
| 5+ ardışık SL | 0 ✓ |
| WR < %25 | — |

**HALT KARARI:** EmaScalper1m bırakılacak. binance-expert alternatif önerecek.

## Loop 41-48 Aggregate
| Loop | Strateji | Trade | Realized |
|---|---|---|---|
| 41-43 | Donchian 15m | 11 | -$2.97 |
| 44-45 | BB MeanRev 15m | 2 | +$0.011 (Loop 45 +$0.011, Loop 44 0) |
| 46-48 | EmaScalper1m (3 config) | 12 | -$1.69 |
| **Total** | — | **25** | **-$4.66** |

binance-expert tetikleniyor → cevap geldikten sonra Loop 49 boot.

— PM 2026-04-28 Loop 48 halt @ t=30
