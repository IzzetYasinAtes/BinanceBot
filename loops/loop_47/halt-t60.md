# Loop 47 — Halt @ t=60dk (2026-04-28 12:12 TR) — FİLTRE ÇOK SIKI

## Halt Sebebi: Signals=2 (60dk) Hedef Altı (8-12/h)

| Metrik | t30 | t60 | Δ |
|---|---|---|---|
| Cash | $499.87 | $399.77 | -$100.10 (DOGE açıldı) |
| Equity | $499.87 | $499.79 | -$0.08 |
| Realized | -$0.129 | -$0.129 | 0 |
| Unrealized | $0 | -$0.005 | -$0.005 (DOGE açık) |
| Net | -$0.129 | -$0.209 | -$0.080 |
| Komisyon | $0.150 | $0.225 | +$0.075 (DOGE entry) |
| Open Pos | 0 | 1 | +1 (DOGE) |
| Closed Pos | 1 | 1 | 0 |
| **Signals** | **1** | **2** | **+1 (toplam 60dk = 2/h)** |

## Pozisyonlar
- **BTCUSDT KAPALI** (TimeStop 12dk, -$0.129)
- **DOGEUSDT AÇIK** (entry 09:01 UTC, mark $0.0998, unrealized -$0.005, hold 11dk / MaxHold 12dk → 1dk içinde TimeStop)

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | -$0.129 | ✓ buffer $1.37 |
| 5+ ardışık SL | 1 | ✓ |
| WR < %25 (5+ trade) | 1 trade ölçüm değil | — |
| **Signals 0-3 (60dk)** | **2** | **❌ HALT (filtre çok sıkı)** |

**HALT KARARI:** Frekans hedefi (8-12/h) sağlanmadı. Filtre güçlendirme aşırı oldu.

## Loop 48 Pivot — Orta Yol Parametre

| Parametre | Loop 46 | Loop 47 (sıkı) | Loop 48 (orta) | Açıklama |
|---|---|---|---|---|
| `RsiLowerBand` | 40 | 45 | **42** | RSI bandı orta sıkı |
| `RsiUpperBand` | 65 | 60 | **63** | overbought reddi orta |
| `VolumeMultiplier` | 0.8 | 1.2 | **1.0** | hacim teyidi nötr |
| `MinAtrPct` | 0.0003 | 0.0005 | **0.0004** | volatilite eşiği orta |
| `MaxHoldMinutes` | 8 | 12 | **10** | TP'ye ulaşma penceresi orta |
| `TpAtrMultiplier` | 1.5 | 1.2 | **1.2** | korunur (Loop 47 mantığı) |

Diğer aynı: KlineInterval=1m, EmaFast=9, EmaSlow=21, RsiPeriod=14, VolumeWindow=20, AtrPeriod=14, SlAtrMultiplier=0.8, R:R 1.5:1, Cooldown=2 bar, 12 coin

Beklenen frekans: 8-15/saat (Loop 46 19/h ve Loop 47 2/h ortası)
Beklenen WR: %35-45 (filtre kalitesi orta)

## Loop 41-47 Aggregate
| Loop | Trade | Realized |
|---|---|---|
| 41 | 8 | -$1.80 |
| 42 | 2 | -$0.73 |
| 43 | 1 | -$0.45 |
| 44 | 0 | $0 |
| 45 | 2 | +$0.011 |
| 46 | 11 | -$1.563 |
| 47 | 1 (kapalı) | -$0.129 |
| **Total** | **25** | **-$4.66** |

## Sıradaki: Loop 48 Boot
1. appsettings.json patch (12 EmaScalper1m strategy → orta parametre)
2. dotnet kill + DB reset + reseed
3. API restart
4. Loop 48 boot rapor
5. ScheduleWakeup 1800s → t30

— PM 2026-04-28 Loop 47 halt @ t=60
