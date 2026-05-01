# Loop 77 Boot — EMA200 Trend Gate + BBW Score (2026-05-01 17:25 TR)

## Pivot Sebebi
Loop 76 trailing stop deploy başarılı (TRAILING peak-up log ✓) AMA entry kalitesi problem (ADA -$0.61 büyük loss BE öncesi). binance-expert spec: **EMA200 trend gate** (long sadece trend yukarı) + **BBW score** (regime filter).

## binance-expert Spec (uygulandı)

**EMA200 Trend Gate (hard-gate):**
- `closePrice <= EMA200 → skip` (long sadece trend yukarı)
- Toggle: `Ema200GateEnabled` (default true, 0-emit sigortası için)
- Buffer 200 bar → REST warmup ile start'ta hazır

**BBW Score (skor formülüne dahil):**
- `BBW = (Upper - Lower) / Middle` (Bollinger 20 bar, stdDev 2.0)
- `BBW > 0.008 → +1 puan` (regime: trending, choppy değil)
- Hard-gate DEĞİL (0-emit risk)
- Max skor 6 → 7 (BBW ile +1)
- MinScore 4 sabit → frekans korunur

## backend-dev Implementation (6 dosya)
- `KmsMomentumSnapshot.cs` — Ema200 + BollingerBandWidth field'ları
- `MarketIndicatorService.cs` — Ema200Period=200 + BollingerPeriod=20 + warmup eşiği 30→200
- `KmsMomentumEvaluator.cs` — Parameters'a 4 yeni alan, EMA200 hard-gate, BBW skor toplama, ContextJson audit
- `appsettings.json` — 5 KMS seed yeni params
- `KmsMomentumEvaluatorTests.cs` — 5 yeni test (Test 12-16)

**Build/Test:** 265/265 PASS ✓ (5 yeni EMA200/BBW)

## DB UPDATE (PM)
5 KMS strategy ParametersJson UPDATE: Ema200GateEnabled=true, BbwScoreEnabled=true, BbwThreshold=0.008, BbwScorePoints=1.

## Boot State
| Metrik | Değer |
|---|---|
| Bot PID | 1868 |
| Port | 5000 |
| WS | Streaming ✓ |
| Warmup | 5/5 ✓ |
| EMA200 module | Enabled (hard-gate) ✓ |
| BBW module | Enabled (skor +1) ✓ |
| Trailing module | Enabled (TrailPct 0.0015) ✓ (Loop 76) |
| BE module | Enabled (Trigger 0.0010, Offset 0.0002) ✓ (Loop 75) |
| KMS params | MinScore 4, RsiCeil 70, TpMul 1.5, MaxHold 35 + Ema200/BBW |
| Migration | Loop76 (önceki) — Loop 77 migration GEREK YOK |

## Beklenti
- **EMA200 gate**: Downtrend coin'lerde emit susturulur → ADA -$0.61 gibi BE öncesi big loss önlenir
- **BBW score**: Trending market'te +1 puan → daha güçlü emit
- Frekans hafif düşebilir (downtrend filter), ama entry kalitesi yüksek
- Loop 75 BE move + Loop 76 trailing + Loop 77 EMA200/BBW = full feature stack

## Halt Eşikleri
- Realized < -$0.50 (Loop 77) → param tune (RsiCeiling fine, BBW threshold fine)
- 0 emit (60dk) → Ema200GateEnabled=false toggle
- 5+ ardışık SL → CB reset

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (17:55 TR)**

— PM 2026-05-01 Loop 77 boot (full stack: BE + Trailing + EMA200 + BBW)
