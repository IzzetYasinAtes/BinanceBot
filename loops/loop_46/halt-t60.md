# Loop 46 — Halt @ t=60dk (2026-04-28 11:07 TR) — REALIZED < -$1.50

## Halt Sebebi: Realized -$1.5628 (eşik -$1.50 aşıldı $0.063 ile)

| Metrik | Boot | t30 | t60 | Δ (t30→t60) |
|---|---|---|---|---|
| Cash | $500 | $298.66 | $498.44 | +$199.78 (2 pos kapandı) |
| Equity | $500 | $499.24 | $498.44 | -$0.80 |
| Realized | $0 | -$0.996 | **-$1.563** | -$0.567 (yeni 7 closed) |
| Unrealized | $0 | +$0.386 | $0 | -$0.386 |
| Net | $0 | -$0.610 | -$1.563 | -$0.953 |
| Komisyon (toplam) | $0 | $0.750 | $1.649 | +$0.899 |
| Open Pos | 0 | 2 | **0** | -2 |
| Closed Pos | 0 | 4 | **11** | +7 |
| Signals (toplam) | 0 | 10 | 19 | +9 (60dk = ~19/h trade frekansı) |
| WinRate | — | %0 (0/4) | **%27.27 (3/11)** | iyileşti ama eşik altı |

## 11 Closed Trade Tablosu

| # | Coin | Hold | Realized | Tip |
|---|---|---|---|---|
| 1 | XRP | 8.1dk | -$0.249 | TimeStop |
| 2 | ADA | 3.6dk | -$0.373 | SL |
| 3 | DOGE | 8.1dk | -$0.311 | TimeStop |
| 4 | ETH | 8.1dk | -$0.063 | TimeStop |
| 5 | ADA | 8.1dk | **+$0.155** ✓ | TimeStop (mark up) |
| 6 | BTC | 8.1dk | **+$0.043** ✓ | TimeStop (mark up) |
| 7 | DOT | 8.1dk | -$0.170 | TimeStop |
| 8 | AVAX | 8.1dk | -$0.278 | TimeStop |
| 9 | ETH | 8.1dk | **+$0.026** ✓ | TimeStop (mark up) |
| 10 | BTC | 8.1dk | -$0.137 | TimeStop |
| 11 | SOL | 8.1dk | -$0.205 | TimeStop |

3 winning ($0.224 total), 8 losing (-$1.786 total). Komisyon $1.649. Net **-$1.563**.

## Halt Kriter (final)
| Kriter | Durum | Verdict |
|---|---|---|
| **Realized < -$1.50** | **-$1.5628** | **❌ HALT** |
| 5+ ardışık SL | zincir kırıldı (3 win) | ✓ |
| WinRate < %20 | %27 | ✓ (eşik üstü) |
| Open pos 0 + Realized<-$1.20 | EVET | ❌ HALT |

**HALT KESİN:** Realized eşiği geçti.

## Strateji Yorumu
- **Frekans hedefi başarılı:** 19 sinyal/60dk = 19/saat (binance-expert beklenti aralığı 20-30/h)
- **WR kötü:** %27.27, BE WR %34.8 — pozitif olmak için %35+ lazım
- **Net trade:** ortalama +$0.075 winning vs -$0.223 losing → asimetri kötü
- **TimeStop dominant:** 10/11 TimeStop, sadece 1 SL — yani fiyat 8dk'da TP'ye yetişemiyor, mark genelde flat veya hafif down

**Root cause:** TP %0.30-0.80 mesafesi 1m bar 8dk için ulaşılması zor. Mark genelde küçük fluctuation, fee baskın oluyor.

## Loop 47 Pivot — Filtre Güçlendirme (kalite önceliği)

Yeni parametreler:
- `RsiLowerBand`: 40 → **45** (oversold reddi)
- `RsiUpperBand`: 65 → **60** (overbought reddi sıkı)
- `VolumeMultiplier`: 0.8 → **1.2** (gerçek momentum hacim teyidi)
- `MinAtrPct`: 0.0003 → **0.0005** (sessiz coin reddi)
- `MaxHoldMinutes`: 8 → **12** (TP'ye ulaşma şansı artsın)
- `TpAtrMultiplier`: 1.5 → **1.2** (TP daha yakın, ulaşılabilir)
- Diğer parametreler aynı

Beklenen etki: frekans 19/h → 8-12/h (yarıya iner ama kalite artar). WR %27 → %35-45 hedef.

## Sıradaki: Loop 47 Boot
1. appsettings.json patch (12 EmaScalper1m strategy)
2. dotnet kill + DB reset + reseed
3. API restart
4. Loop 47 boot rapor
5. ScheduleWakeup 1800s → t30

— PM 2026-04-28 Loop 46 halt @ t=60
