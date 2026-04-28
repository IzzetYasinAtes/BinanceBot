# Loop 49 — Check t=420dk (2026-04-28 20:11 TR) — TOPARLAMA BAŞARILI ✓

## 3 Yeni TimeStop: 2 WIN + 1 BE/SL → WR %42.86

| Metrik | t360 | t420 | Δ |
|---|---|---|---|
| Cash | $204.86 | $505.40 | +$300.54 (3 pos kapandı) |
| OpenPositionsValue | $300.80 | $0 | -$300.80 |
| Equity | $505.66 | **$505.40** | -$0.26 |
| Realized | -$1.044 | **-$0.576** | **+$0.468** ✓ |
| Unrealized | +$0.954 | $0 | -$0.954 |
| Net | -$0.091 | **-$0.576** | (mark unrealized realize'a dönüştü) |
| Komisyon (toplam) | $0.825 | $1.050 | +$0.225 (3 exit) |
| Open Pos | 3 | 0 | -3 |
| Closed Pos | 4 | **7** | +3 |
| Signals | 7 | 7 | 0 yeni |
| **WinRate** | %25 (1/4) | **%42.86 (3/7)** | **+%17.86** ✓ |

## 3 Yeni TimeStop Detay

### 🟢 XRPUSDT (TimeStop, WIN +$0.281)
- Entry $1.3718 @ 15:01 UTC | Exit $1.3777 @ 17:01 UTC (120dk MaxHold)
- Mark up +%0.43 (TP %0.47'a %91 mesafe)
- Komisyon: $0.0749 + $0.0752 = $0.1501
- **Realized: +$0.281** (mark profit -$0.150 fee = +$0.281)

### 🟡 BTCUSDT (TimeStop, NEAR-BE -$0.003)
- Entry $75,838 @ 14:30 UTC | Exit $75,950 @ 16:30 UTC (120dk MaxHold)
- Mark up +%0.15
- Komisyon: $0.0751 + $0.0752 = $0.1503
- **Realized: -$0.003** (mark profit $0.147 ≈ fee → tam BE)

### 🟢 SOLUSDT (TimeStop, WIN +$0.190)
- Entry $83.23 @ 14:30 UTC | Exit $83.51 @ 16:30 UTC (120dk MaxHold)
- Mark up +%0.34 (TP %0.85'a %40 mesafe)
- Komisyon: $0.0749 + $0.0752 = $0.1501
- **Realized: +$0.190**

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | -$0.576 | ✓ buffer **$0.92** (geri açıldı!) |
| 5+ ardışık SL | 4WIN/3WIN içerir, zincir kırık | ✓ |
| WR < %20 (8+ trade) | %42.86 (7 trade) | ✓ |
| Zombie | 0 açık | ✓ |
| WS / CB | 51 stabil | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK + STRATEJİ HEALTH: GOOD.**

## Loop 49 Trade Tablosu (7 trade)

| # | Coin | Hold | Realized | Tip |
|---|---|---|---|---|
| 1 | BTC | 100dk | -$0.489 | SL |
| 2 | XRP | 70dk | -$0.546 | SL |
| 3 | **ETH** | **31dk** | **+$0.488** ✓ | **TP yakını** |
| 4 | XRP | 9dk | -$0.497 | SL hızlı |
| 5 | **XRP** | **120dk** | **+$0.281** ✓ | **TimeStop WIN** |
| 6 | BTC | 120dk | -$0.003 | TimeStop BE |
| 7 | **SOL** | **120dk** | **+$0.190** ✓ | **TimeStop WIN** |

**3 WIN + 1 BE + 3 SL = %50 effective WR.**

## Loop 41-49 Aggregate
| Loop | Trade | Realized | WR |
|---|---|---|---|
| 41-43 | 11 | -$2.97 | %0 |
| 44-45 | 2 | +$0.011 | %50 |
| 46-48 | 13 | -$1.69 | %23 |
| **49 (t420)** | **7** | **-$0.576** | **%42.86** ✓ |
| **Total** | **33** | **-$5.23** | %18 |

## Yorum
binance-expert orta senaryo (%45 WR, $0.15/gün) tam uyumlu — şu an Loop 49 ~%43 WR. -$0.576 gerçek 24h sonu için projeksiyon: ~ -$0.40 ile +$0.20 arası.

**Pivot başarısı:** EmaScalper1m %23 WR → BB MeanRev 15m gevşek %43 WR. binance-expert kararı (Seçenek D) doğruydu.

## Karar
**Loop 49 DEVAM** — sağlıklı operasyon, kar trend yakın.

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=480dk (21:11 TR)**

— PM 2026-04-28 Loop 49 t=420
