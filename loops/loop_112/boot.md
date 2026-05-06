# Loop 112 Boot — Strateji Pivot Aile A Swing Trading 4h MTF

Tarih: 2026-05-06 08:13 UTC | Bot port 5188

## Pivot Karar Özet

43 loop başarısız scalping (-$0.05 cumulative). Kullanıcı kararı: B - Strateji elden geçir.

**Sentez** (`loops/loop_strategy_pivot/spec-synthesized.md`):
- binance-expert tavsiye: Swing Trading 4h MTF
- architect karar: Senaryo C plug-in IStrategyEvaluator
- ADR-0027 yazıldı, ADR-0024 paused (silinmez, re-aktivasyon hazır)

## Backend-dev 13 Commit (bfa139b'ye kadar)

Çekirdek (4) + Aile A (9) commit. PatternComposite **paused** (Status=2), SwingTrade **active** (Status=3).

## Boot State

DB Strategies (10 strateji):
| Id | Name | Type | Status |
|---|---|---|---|
| 901 | BTC-Pattern | 3 (PatternComposite) | 2 (Paused) |
| 902 | ETH-Pattern | 3 | 2 |
| 903 | XRP-Pattern | 3 | 2 |
| 904 | SOL-Pattern | 3 | 2 |
| 905 | ADA-Pattern | 3 | 2 |
| **906** | **BTC-Swing** | **4 (SwingTrade)** | **3 (Active)** ✓ |
| 907 | ETH-Swing | 4 | 3 ✓ |
| 908 | XRP-Swing | 4 | 3 ✓ |
| 909 | SOL-Swing | 4 | 3 ✓ |
| 910 | ADA-Swing | 4 | 3 ✓ |

RiskProfile:
- RiskPerTradePct: **0.015** (%1.5/trade — binance-expert spec)
- MaxOpenPositions: 3
- CB: Healthy

appsettings (Loop 112):
- KlineIntervals: ["5m", "15m", **"4h"**] — 4h kline stream
- BackfillIntervals: ["5m", "4h"]
- BreakEven.TriggerPct: **0.0100** (%1 — Swing için)
- TrailingStop.TrailPct: **0.0050** (%0.5)
- HardMaxHoldMinutes: **720** (12 saat — Swing 4h × 2 bar)

SwingTrade ParametersJson (5 strateji):
- EmaShortPeriod: 20
- EmaLongPeriod: 50
- VolumeSurgeMultiplier: 1.5
- RsiPeriod: 14, Long [40, 65], Short [35, 60]
- AtrPeriod: 14, SlAtrMultiplier: 1.5, TpAtrMultiplier: 3.0
- MaxHoldHours: 8
- BeMoveTriggerPct: 0.01 (%1+ kar → BE)
- TimeExitMinProfitPct: 0.005 (%0.5+ kar + 8h → exit)

## Hipotez

Aile A Swing Trading 4h:
- Win rate %45-55 (binance-expert backtest tahmini)
- Avg win +%2 (gross) - %0.10 fee = +%1.9 net
- Avg loss -%1.5 (gross) - %0.10 fee = -%1.6 net
- Expectancy: 0.5 × +%1.9 + 0.5 × -%1.6 = **+%0.15/trade** (kaba)
- Haftalık 5-10 trade × +%0.15 × $500 = **+$3.75-7.50/hafta**
- Aylık beklenti: **+$15-30** (kar yörüngesi)

## Önemli

- Bot 4h bar close anında SwingTradeEvaluator çağrılır (ilk fırsat: bir sonraki 4h bar close — UTC 08:00, 12:00, 16:00, 20:00, 00:00, 04:00)
- 4h bar warmup: 50 bar gerek = ~8 gün geçmiş kline. DB'de 1700+ bar var (yeterli).
- PatternComposite yine resolve'da çağrılır AMA Status=Paused → query 0 sonuç → composer çalışmaz (Loop 100 enum bilgisi)

## Cumulative

44 loop -$0.05 cumulative (Loop 110 ve sonrası reset). Loop 112 = ilk gerçek strateji pivot test.

## Sonraki

ScheduleWakeup t60 (4h bar yakın) — pos açılım + SwingTrade davranış izleme. İlk 4h bar emit fırsatı yakın.
