# Loop 41 Boot Raporu — 2026-04-24 t=0

## Strateji
**Donchian Channel Breakout + Volume Z-Score Filtresi (15m timeframe)**
Şampiyon AR-GE: `loops/loop_41/strategy-arge-v2.md`

Tasarım özet:
- 12 coin × DonchianBreakout15m evaluator
- TP = ATR(14) × 2.0, clip [0.005, 0.012]
- SL = ATR(14) × 0.65, clip [0.002, 0.005]
- MaxHold = 90dk, MaxOpenPositions = 5 (RiskProfile global)
- Sizing: $100/trade (RiskProfile %20 / $20.10 floor)
- Hedef R:R 2.67:1 ortalama, BE WR %36.5
- 24h beklenti: orta senaryo +$5.16/gün, kötü senaryo +$2.10/gün, halt -$1.50

## Backend impl (backend-dev özet)
- Yeni: `DonchianBreakoutEvaluator` + `DonchianBreakoutIndicatorSnapshot`
- Yeni: `Indicators.Donchian()`, `Indicators.VolumeStdev()`
- `MarketIndicatorService.SymbolState.FifteenMinute` buffer (cap=80)
- `StrategyType.DonchianBreakout15m = 4` enum
- DI: `IStrategyEvaluator` registry'ye eklendi
- WS: `KlineIntervals` `["1m","5m","15m"]` — supervisor generic enumerate (patch gereksizdi)
- appsettings: 12 Donchian Activate=true, 12 AtrSwing Activate=false
- Build: 0 warn 0 err / Test: 252/252 geçti
- Cooldown V1'de yok (SRP — RiskProfile.MaxOpenPositions=5 yaklaşık koruma)

## DB Reset (t=0)
PowerShell SqlClient ile tek transaction DELETE:
- OrderFills 16 → 0
- Orders 16 → 0
- StrategySignals 12 → 0
- Positions 8 → 0
- Strategies 12 → 0 (eski AtrSwing Active=Draft kalıntısı temizlik)
- SystemEvents 222 → 0
- RiskProfiles 3 → 0 (seeder yeniden create eder)
- BookTickers 12 → 0
- OrderBookSnapshots 78 → 0

VirtualBalance: API up sonrası `POST /api/papertrade/reset { startingBalance: 500 }` → IterationId fabd6d90-..., ResetCount=10.

## API Restart Doğrulama
- `curl /api/portfolio/summary` → cash $500.0000, equity $500.0000, netPnl 0
- `curl /api/strategies` → 12 DonchianBO15m **Active** + 12 AtrSwing **Draft**
- WS Streaming, drift -177ms ~ -373ms (sağlıklı, < ±1s)

## Playwright Smoke (1920×1080 viewport)
| # | Sayfa | Console Error | Screenshot | Notlar |
|---|---|---|---|---|
| 01 | dashboard | 0 | ui-t0-01-dashboard.png | Hero 3 kart $0/$0/$0, 4 küsurat ✓ |
| 02 | positions | 0 | ui-t0-02-positions.png | Açık 0 / Kapalı 0 ✓ |
| 03 | orders | 0 | ui-t0-03-orders.png | Tümü 0, filtreler ✓ |
| 04 | strategies | 0 | ui-t0-04-strategies.png | 12 Donchian AKTIF + 12 AtrSwing TASLAK ✓ |
| 05 | risk | 0 | ui-t0-05-risk.png | DD 0%, ÜstÜste 0/8, CB HEALTHY ✓ |
| 06 | klines | 0 | ui-t0-06-klines.png | BTC 1m grafik dolu, 1m/5m/**15m**/1s seçici ✓ |
| 07 | orderbook | 0 | ui-t0-07-orderbook.png | BTC bid/ask depth ✓ |
| 08 | logs | 0 | ui-t0-08-logs.png | Startup + Backfill + Activate + SignalSkipped (warmup) ✓ |

**Genel: 8/8 sayfa hatasız, console error 0.**

## Sonraki Adım
- ScheduleWakeup t=30dk (1800s)
- t=30: DB sayım (trade, signal, fill) + Playwright smoke + halt kriter
- 15m bar warmup için ilk gerçek kapanış ~10:30 UTC (sıradaki tam 15dk dilimi). 20-bar warmup ≈ 5 saat → ilk DonchianBO sinyali ~15:30 UTC civarı (hesaplı tahmin, daha erken tetik mümkün eğer backfill 20+ bar getirmişse).

## Halt Kriterleri Hatırlatma
- Realized < -$1.50 → halt
- 5+ ardışık kayıp → halt
- Zombie pozisyon (MaxHold × 3 >270dk açık) → halt
- API down / WS disconnect > 5dk → halt
- Console error veya UI patlama → halt + frontend-dev fix
- CB Tripped → halt

Kar tarafı:
- Realized > +$2 → loop devam, parametre dondur
- Realized > +$5 → 24h sonu kaydet, Loop 42 ince ayar değerlendir

— PM 2026-04-24 t=0
