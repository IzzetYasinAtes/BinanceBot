# Loop 47 Boot — EmaScalper1m Filtre Güçlendirme (2026-04-28 11:08 TR)

## Pivot Sebebi
Loop 46 t60 halt: **Realized -$1.5628 < -$1.50 eşiği**. 11 closed trade (3 win + 8 loss = WR %27.27). HFS frekansı başarılı (19 sinyal/h ≈ binance-expert beklenti) ama kalite eksik — TimeStop dominant (10/11), TP'ye ulaşamıyor.

Root cause: TP %0.30-0.80 mesafesi 1m bar 8dk için zor; mark genelde flat/down, fee baskın.

## Filtre Güçlendirme — Kalite Önceliği

| Parametre | Loop 46 | Loop 47 | Etki |
|---|---|---|---|
| `RsiLowerBand` | 40 | **45** | oversold reddi sıkı |
| `RsiUpperBand` | 65 | **60** | overbought reddi sıkı |
| `VolumeMultiplier` | 0.8 | **1.2** | gerçek momentum hacim teyidi |
| `MinAtrPct` | 0.0003 | **0.0005** | sessiz coin reddi |
| `MaxHoldMinutes` | 8 | **12** | TP'ye ulaşma şansı artsın |
| `TpAtrMultiplier` | 1.5 | **1.2** | TP daha yakın, ulaşılabilir |

Diğer parametreler aynı:
- KlineInterval=1m, EmaFast=9, EmaSlow=21, RsiPeriod=14
- VolumeWindow=20, AtrPeriod=14
- SlAtrMultiplier=0.8 (R:R = 1.2/0.8 = 1.5:1, BE WR ~%40)
- MinTpPct=0.003, MaxTpPct=0.008, MinSlPct=0.002, MaxSlPct=0.005
- CooldownBarsAfterSignal=2

12 coin aktif: BTC, ETH, BNB, XRP, SOL, ADA, DOGE, LINK, DOT, AVAX, LTC, TRX

## Beklenti
- Frekans 19/h → **8-12/h** (filtre sıkıldı, yarı yarıya iner)
- WR %27 → **%35-45** hedef (kalite öncelikli)
- TimeStop oranı %91 → **%60-70** (12dk MaxHold ile TP ulaşma şansı artsın)
- BE WR R:R 1.5:1 → %40

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500.0000 |
| CurrentCash | $500.0000 |
| Equity | $500.0000 |
| Active Strategies | 12 (EmaScalper1m güçlendirilmiş) |
| API Port | 5188 |
| Branch | development (altın kural #10) |

## Halt Eşikleri (Loop 47 için)
- Realized < -$1.50 → Loop 48
- 5+ ardışık SL/TimeStop → Loop 48
- WR < %20 (15+ trade sonrası) → Loop 48
- Open pos 0 + Realized < -$1.20 → Loop 48
- Sinyal akmıyor (>60dk, 1m strateji için) → filtre çok sıkı → Loop 48

## Loop 41-46 Aggregate
| Loop | Strateji | Trade | Realized | Sebep |
|---|---|---|---|---|
| 41 | Donchian BO 15m | 8 | -$1.80 | LTC whipsaw (cooldown yok) |
| 42 | + cooldown | 2 | -$0.73 | XRP+SOL eş-SL |
| 43 | + filtre gevşetme | 1 | -$0.45 | ADA SL, DOGE stale |
| 44 | BB MeanRev 15m sıkı | 0 | $0 | 0 sinyal halt |
| 45 | BB MeanRev gevşek | 2 | +$0.011 | XRP TimeStop +%85 TP, BTC TimeStop -fee |
| **46** | **EmaScalper1m HFS** | **11** | **-$1.563** | **WR %27, TimeStop dominant** |
| **Total** | — | **24** | **-$4.51** | %16.7 WR |

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (11:38 TR)**

Erken kontrol:
- Sinyal akış kontrolü (filtre güçlendirme aşırı sıkı mı?)
- WR ölçümü
- Halt eşikleri

— PM 2026-04-28 Loop 47 boot
