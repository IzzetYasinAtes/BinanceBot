# Loop 60 Boot — BB MeanRev v2 + EMA200 + Orta Yol Gevşek (2026-04-30 01:36 TR)

## Pivot Sebebi
Loop 59 8h 0 emit → sermaye %100 korundu ama atıl. binance-expert orta yol gevşetme önerdi (B): EMA200 trend filter KORU, BB+RSI+volZ orta yola çek.

## Loop 60 Parametreler

| Parametre | Loop 59 | **Loop 60** |
|---|---|---|
| `BbStdMultiplier` | 2.2 | **2.0** |
| `RsiOversoldThreshold` | 30 | **35** |
| `VolumeZScoreThreshold` | 0.5 | **0.3** |
| EMA200 trend | KORU ✓ | **KORU ✓** |
| Coin | BTC-only | **BTC-only** |
| MaxOpenPositions | 1 | 1 |
| TpAtr / SlAtr | 2.5× / 0.7× | aynı (R:R 3.57:1) |

## RiskProfile Sıkılaştırma (Tatil Güvencesi)

| Parametre | Eski | **Loop 60** |
|---|---|---|
| `MaxConsecutiveLosses` | 8 | **3** |
| `MaxDrawdown24hPct` | 0.20 | **0.05** ($25/24h) |
| `MaxDrawdownAllTimePct` | 0.40 | **0.10** ($50 toplam) |

3 ardışık kayıp veya $25 günlük kayıp → otomatik halt.

## Beklenti
- Frekans: günde 2-5 sinyal (Loop 59'un 0'dan)
- WR: BE WR %22, hedef %30+
- EMA200 anti-disaster koruması aktif

## Boot State (DB Reset YOK)
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500 |
| Equity | **$500** ✓ (Loop 59 sermaye korundu) |
| Active | 1 (BTC-BbMeanRev15m-v2 orta yol param) |
| API Port | 5188 |

## Halt Eşikleri
- Realized < -$0.50 (24h DD %5 = $25 limit, çok daha sıkı) → halt
- 3+ ardışık SL → otomatik halt (RiskProfile)
- WR < %25 (5+ trade) → Loop 61 binance-expert
- 8h 0 emit → Loop 59 deneyimi tekrar (ama orta yol param ile beklenmez)

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=60dk (02:36 TR)**

— PM 2026-04-30 Loop 60 boot
