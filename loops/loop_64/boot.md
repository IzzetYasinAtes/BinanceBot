# Loop 64 Boot — BB MeanRev v2 + EMA200 + 5 Coin (2026-04-30 12:33 TR)

## Pivot Sebebi
Loop 63 t60: 5 ardışık SL → RiskAlert otomatik halt. EmaScalper bu downtrend rejiminde **3. defa** başarısız (Loop 56, 61, 63 hepsi WR <%30).

binance-expert kararı (önceki AR-GE): **A — EMA200 trend filter geri + 5 coin**.

## Strateji: BB MeanRev v2 + EMA200 + 5 Coin

5 coin (BTC, ETH, XRP, SOL, ADA) BB MeanRev15m + EMA200 trend filter (kod-level, commit `e5fb921`):
- Sadece `currentClose > Ema200_15m` (uptrend) → emit
- Downtrend skip (anti-disaster, Loop 58 +$3.95 zararı önler)

### Param (Loop 60 v2'den)
| Parametre | Değer |
|---|---|
| BBstd | 2.0 (Loop 60 orta yol) |
| RsiOversoldThreshold | 35 |
| VolumeZScoreThreshold | 0.3 |
| TpAtr | 2.5× |
| SlAtr | 0.7× |
| MaxHold | 90dk |
| Cooldown | 8 bar |
| MinAtrPct | 0.0005 |

R:R = 3.57:1, BE WR ~%22.

### RiskProfile (sıkı)
- MaxOpenPositions: 5 → **3** (korelasyon riski)
- MaxConsecutiveLosses: 5 → **3** (erken halt)
- MaxDrawdown24hPct: %3 ($15 limit)

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| Cash / Equity | $500 / $500 (DB reset) |
| Active | 5 (BTC/ETH/XRP/SOL/ADA BB MeanRev15m) |
| MaxOpenPositions | 3 |
| API Port | 5188 |

## Beklenti
Eğer BTC uptrend'e dönerse 5 coin emit verir (her biri trend uyumlu olduğu zaman). Downtrend'de skip — kullanıcı kuralı "0 emit yasak" ama aynı zamanda "sermaye koruma yasak" çelişiyor. EMA200 ile orta yol: trend uyumlu coin'lerden emit alır, diğerleri skip.

binance-expert beklenti: 4-8 emit/gün (tüm 5 coin). Tatil 5. günü sonu hedef BE veya hafif kar.

## Halt Eşikleri (sıkı)
- Realized < -$1.50 → Loop 65 binance-expert
- 3+ ardışık SL → otomatik halt (RiskProfile)
- WR < %30 (10+ trade) → Loop 65
- RiskAlert tetiklenirse → DB reset + Loop 65

## Loop 41-63 Aggregate
| Cumulative | 67 trade, ~$11.18 net loss, %14 WR |

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (13:03 TR)**

— PM 2026-04-30 Loop 64 boot
