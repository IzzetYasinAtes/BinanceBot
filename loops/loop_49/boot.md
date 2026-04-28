# Loop 49 Boot — BB MeanRev 15m Geri Dönüş (Gevşetilmiş, 5 coin) (2026-04-28 12:57 TR)

## Pivot Sebebi
Loop 46-47-48 EmaScalper1m **3 farklı parametre setiyle başarısız** (gevşek/sıkı/orta). Frekans 19→1→0 monoton düşüş. Strateji mimari sorun: 1m EMA crossover crypto'da TP/fee ratio'sunu yenemiyor.

binance-expert kararı: **D — BB MeanRev 15m geri dön**, gevşetilmiş parametreler.

**Kök sorun teşhisi:** Round-trip fee %0.15 (BNB). 1m ATR mesafesi ile TP %0.30-0.50 → fiyat 8-10dk'da yetişemiyor. **15m ATR çok daha büyük** → TP %0.5-1.2 ulaşılır → fee'yi rahatlıkla geçer.

## Loop 49 BB MeanRev 15m — Gevşetilmiş Parametreler

| Parametre | Loop 45 (önceki) | Loop 49 (yeni) | Gerekçe |
|---|---|---|---|
| `BbStdMultiplier` | 1.8 | **2.0** | Standart 2σ — daha az false dip |
| `RsiOversoldThreshold` | 35 | **38** | Daha geniş, fırsat artırır |
| `VolumeZScoreThreshold` | 0.8 | **0.5** | Düşük vol rejimde de tetikleme |
| `TpAtrMultiplier` | 1.5 | **1.8** | TP %0.5-1.0+ → fee 6× clearance |
| `SlAtrMultiplier` | 1.0 | **0.9** | Hafif daralma → R:R 2:1 |
| `MinTpPct` | 0.004 | **0.005** | TP floor yükseldi |
| `MaxTpPct` | 0.010 | **0.012** | TP cap yükseldi |
| `MaxHoldMinutes` | 90 | **120** | 8 bar → bounce 90dk geçebiliyor |
| `MinAtrPct` | 0.0007 | **0.0005** | Düşük vol saatlerde aktif |
| `CooldownBarsAfterSignal` | 4 | **3** | 60dk → 45dk frekans artışı |

R:R = 1.8/0.9 = **2:1** (BE WR ~%33.3)

## 5 Aktif Coin (Loop 41-45 ile aynı)
BTC, ETH, XRP, SOL, ADA

12 EmaScalper1m + 12 DonchianBO15m + 12 AtrSwing tümü Activate=false.

## Beklenti (binance-expert)
| Senaryo | WR | Trade/gün | Net/gün | 7 gün |
|---|---|---|---|---|
| Kötü (bear sürer) | %25 | 3 | -$0.30 | -$2.10 |
| Orta (yan piyasa) | %45 | 4 | +$0.15 | +$1.05 |
| İyi (volatile) | %60 | 5 | +$0.60 | +$4.20 |

Hedef tatil: BE veya az kâr. Halt eşiği 10 trade'de -$3.00.

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500.0000 |
| CurrentCash | $500.0000 |
| Equity | $500.0000 |
| Active Strategies | 5 (BB MeanRev15m gevşek) |
| API Port | 5188 |
| Branch | development |

## Halt Eşikleri
- Realized < -$1.50 → Loop 50 (radikal pivot, binance-expert tetikle)
- 5+ ardışık SL/TimeStop → Loop 50
- 4h boyunca 0 sinyal → filtre daha gevşet (RsiOversoldThreshold 38→42, VolumeZ 0.5→0.3)
- WR < %20 (10+ trade sonrası) → Loop 50

## Loop 41-48 Aggregate
| Loop | Strateji | Trade | Realized |
|---|---|---|---|
| 41-43 | Donchian 15m | 11 | -$2.97 |
| 44-45 | BB MeanRev 15m sıkı/gevşek | 2 | +$0.011 |
| 46-48 | EmaScalper1m (3 config) | 12 | -$1.69 |
| **Total** | — | **25** | **-$4.66** |

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=60dk (13:57 TR)**

15m bar stratejisi → erken kontrol gereksiz, 60dk normal cycle.

— PM 2026-04-28 Loop 49 boot
