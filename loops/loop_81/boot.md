# Loop 81 Boot — Pattern-Based Scalping Pivot (2026-05-02 11:01 TR)

## Pivot Sebebi
L1-L80 cumulative -$13.97 ($500'den -%2.8). 280+ trade, %25 WR (BE %30-40 gerekli). Mevcut 3 strateji (KMS+BBR+Donchian) negative expectancy doğrulandı. Tam pivot.

**4 paralel agent araştırma** (PM autonomous):
- Explore: L1-L80 post-mortem
- architect: ADR-0024 + 8-commit plan
- binance-expert: 7 pattern + MACD spec
- backend-dev: VirtualBalance reset cascade fix (322/322 PASS)

## backend-dev Implementation (322/322 PASS, warn-free)

### 7 Commit Deploy
| # | SHA | İçerik |
|---|-----|--------|
| 1 | 16b869d | Application port'ları (BarSnapshot, IPatternDetector, IPatternRegistry, IPatternSignalComposer, CompositeSignalDecision, PatternComposerOptions) |
| 2 | 2854343 | MarketIndicatorService.TryGetBarSnapshot + Indicators.Macd |
| 3 | 5432412 | 13 detector (10 score + 2 hard-gate + 1 soft-filter) + 30 unit test |
| 4 | 1bc32da | PatternRegistry + WeightedScorePatternComposer + 8 unit test |
| 5 | 30789eb | PatternCompositeEvaluator + DI swap + KMS/BBR delete |
| 6 | f55dd98 | StrategyType enum temizlik (PatternComposite=3 only) |
| 7 | 227b893 | appsettings 5 PatternComposite seed + Migration Loop81PatternPivot |

### Dosya Hareket
- **Create**: 22 (10 detector + 2 hard-gate + 1 soft-filter + 7 ports + composer + registry + evaluator + tests)
- **Delete**: 6 (KmsMomentumEvaluator, BbReversalEvaluator, snapshots, KMS/BBR tests)
- **Modify**: 6 (StrategyEnums, IMarketIndicatorService, MarketIndicatorService, Indicators, DependencyInjection, appsettings)

## 13 Pattern Detector

### Skor (10)
EmaSqueezeBreak (3), VwapBounce (2), InsideBarBreakout (3), RsiOversoldRecovery (2), VolumeSpikeDonchian (4), HigherLowEmaTouch (2), MacdZeroCross (2), BullishEngulfing (2), HammerReversal (2), BollingerLowerReversal (2)

**Skor tavanı: 24, threshold: 5**

### Hard-Gate (2)
VolumeSurgeGate (vol>avg20×1.0, warmup<20 bypass), SpreadGuardGate (spread/mid<0.001)

### Soft Filter (1)
AdxRegimeFilter (ADX 15-35 dışı → skor × 0.7)

## Geometri
```
SL: max(ATR14 × 1.2, 0.6%)
TP: SL × 2.0  (R:R 1:2)
Trailing: peak × (1 - 0.0015), aktif: UPnL > +0.10%
BE: UPnL > +0.10% → SL = entry
MaxHold: 60dk
```

## Risk Profili
MaxOpenPositions=3, MaxConsecutiveLosses=4, CooldownBars=2, MaxSLPct=0.006

## Boot State
| Metrik | Değer |
|---|---|
| Bot PID | 1868 |
| Port | 5188 |
| Migration | Loop81PatternPivot ✓ uygulandı |
| Strategies | 5 PatternComposite Active (BTC/ETH/XRP/SOL/ADA-Pattern) |
| WS | Streaming ✓ |
| **DB Tam Reset** | ✓ Positions=0, Orders=0, Events=0 |
| **VirtualBalance** | $500 / Reset count=4 |
| **RiskProfile** | Counter 0/4, MaxOpen 3, CB Healthy |
| **VirtualBalanceConsistencyChecker** | ✓ IHostedService aktif |

## L1-L80 Toplam
- 280+ trade, %25 WR, -$13.97
- En iyi: L71 +$0.85 (KMS skor)
- En kötü: L77 -$2.25 (5 coin synchronize reversal)

## L81 KPI
| Metrik | Hedef | Halt |
|--------|-------|------|
| Emit/h | ≥8 (gerçekçi 8-12) | <2 4h → loosen threshold |
| WR | ≥45% | <30% 4h → param spiral red |
| Realized 4h | ≥-$0.50 | < -$2.00 → halt |
| Consec SL | ≤4 | 5+ → CB tripped (auto) |

## İlk Gözlem (boot+5dk)
PatternComposite skip log: tüm coin'ler `hard_gate:volume_surge_gate` çoğunlukla. Beklenen — warmup<20 bar. 5m × 20 = 100dk warmup tam dolduğunda ilk emit'ler bekleniyor.

## Halt Eşikleri
- Realized < -$2.00 → halt + Loop 82 fine-tune
- 0 emit 4h+ → composer threshold 5→4'e düşür
- 5+ ardışık SL → CB tripped (auto, 30dk cooldown)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (11:31 TR)**

İlk emit beklentisi t≥t30 (warmup tamamlanması).

— PM 2026-05-02 Loop 81 boot (pattern-based scalping pivot, 13 detector + composer + DB reset)
