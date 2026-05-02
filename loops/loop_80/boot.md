# Loop 80 Boot — Counter Fix + BBR Volume Surge + ADX Indicator (2026-05-02 05:05 TR)

## Pivot Sebebi
Loop 71-79 cumulative -$7.74 ($500'den -%1.55). binance-expert spec deploy: 3 odak alan implement.

## backend-dev Implementation (296/296 test pass)

### Priority 1: Counter Bug Fix
- `RiskProfile.ResetConsecutiveLossCounter(reason, now)` domain method (idempotent, CB status korur)
- `RiskProfileSeeder` startup hook: counter > 0 ise auto-reset
- 4 yeni Domain test

### Priority 2: BBR Volume Surge Gate
- `BbReversalSnapshot` 3 yeni alan: `AvgTradeCount20`, `CurrentTradeCount`, `Adx14`
- `IMarketIndicatorService.TryGetBbReversalSnapshot` signature `tradeCountWindow` parametresi
- `BbReversalEvaluator.Parameters` + 2 alan: `TradeCountWindow=20`, `TradeCountSurgeMultiplier=1.5`
- Volume surge gate: `currentTradeCount > avgTradeCount20 × 1.5` (warmup bypass)
- 6 yeni BBR test

### Priority 3: ADX Indicator + Regime Gates
- `Indicators.Adx(bars, period)` Wilder smoothing (min 28 bar)
- `KmsMomentumSnapshot.Adx14` field
- `BbReversalSnapshot.Adx14` field (üstteki ile)
- `KmsMomentumEvaluator`: AdxGateEnabled=true, AdxTrendingThreshold=20 (gevşek)
  - Hard-gate: `if Adx14 > 0 && Adx14 < 20 → skip`
- `BbReversalEvaluator`: AdxGateEnabled=true, AdxRangeMax=25
  - Hard-gate: `if Adx14 > 0 && Adx14 >= 25 → skip` (BBR sadece zayıf trend)
- 3 yeni Indicators.Adx test + 3 KMS ADX test

## Build/Test
- 0 hata / 0 uyarı (Domain/Application/Infrastructure)
- **296/296 PASS** (önceki 278 + 18 yeni)

## DB UPDATE
- 5 KMS seed: `AdxGateEnabled:true, AdxTrendingThreshold:20`
- 5 BBR seed: `TradeCountWindow:20, TradeCountSurgeMultiplier:1.5, AdxGateEnabled:true, AdxRangeMax:25`
- StrategySeeder otomatik upsert yaptı

## Boot State
| Metrik | Değer |
|---|---|
| Bot PID | 16752 |
| Port | 5000 |
| WS | Streaming ✓ |
| Warmup | 5/5 ✓ |
| Counter Auto-Reset | ✓ (idempotent, zaten 0) |
| BBR Volume Gate | Enabled (1.5x) ✓ |
| ADX Indicator | Active (Wilder 14) ✓ |
| KMS ADX Gate | Enabled (>20) ✓ |
| BBR ADX Gate | Enabled (<25) ✓ |
| 10 Strateji Active | 5 KMS + 5 BBR |

## Tam Stack (Loop 71-80)
| Loop | Feature |
|---|---|
| L71 | KMS skor sistemi |
| L75 | BE move |
| L76 | Trailing stop |
| L77 | EMA200 hard-gate + BBW score |
| L78 | BBW hard-gate |
| L79 | BB Reversal multi-regime |
| **L80** | **BBR Volume Surge + ADX + Counter Fix** |

## Beklenti
- BBR false breakdown önlendi (volume confirmation)
- KMS ADX < 20'de skip (trend yoksa emit yok)
- BBR ADX ≥ 25'te skip (trending'de KMS bölgesi)
- Counter persistent bug çözüldü
- Realized iyileşme: -$7.74 → carry over, yeni iş başlangıcı

## Halt Eşikleri
- Realized < -$1.00 (Loop 80 specific) → Loop 81 backlog (XRP/ADA coin-specific)
- 5+ ardışık SL → CB reset
- 0 emit (60dk) → ADX gate çok katı, threshold düzelt

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (05:35 TR)**

— PM 2026-05-02 Loop 80 boot (BBR volume + ADX + counter fix)
