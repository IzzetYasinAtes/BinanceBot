# Loop 81 — Synthesized Spec (architect + binance-expert + Explore)

PM sentezi — backend-dev için tek kaynak doğruluk.

## 1. Karar Özeti

| Karar | Kaynak | Loop 81 |
|---|---|---|
| Mimari pivot: KMS+BBR sil → Pattern detector | architect ADR-0024 | ✓ |
| 10 detector (skor) + 2 hard-gate + 1 soft-filter | architect + binance-expert merge | ✓ |
| Tek `StrategyType=PatternComposite=3` | architect | ✓ |
| 5 coin × 1 strateji = 5 seed | architect | ✓ |
| Yeni `Indicators.Macd(bars, 12, 26)` | binance-expert P7 | ✓ |
| R:R **1:2** (ADR-0023'ün 1:2.5 ile binance-expert'in 1:1.5 ortası) | PM compromise | ✓ |
| MaxOpenPositions 5→**3**, MaxConsecutiveLosses 5→**4**, CooldownBars 3→**2**, MaxSLPct 0.008→**0.006** | binance-expert | ✓ |
| DB tam reset (Positions+Orders+Events purge) | backend-dev fix + boot | ✓ |
| Frekans hedef: 8-12 emit/h, hedef WR %45+ | binance-expert | ✓ |
| KPI halt: Realized < -$2.00 4h | binance-expert | ✓ |

## 2. Final Pattern Set (sentez)

### Skor Detector'ları (10 adet)
| # | Pattern | Skor | Trigger Özeti |
|---|---------|------|---------------|
| 1 | **EmaSqueezeBreak** | 3 | BBW<0.0025 + EMA9/21 cross + volume 1.3x (architect BollingerSqueezeBreakout + binance P1 merge) |
| 2 | **VwapBounce** | 2 | VWAP geçişi + RSI 40-65 + EMA21 yukarı eğim (binance P2; L29'da +%50 WR kanıt) |
| 3 | **InsideBarBreakout** | 3 | Inside bar + sonraki bar high break + volume 1.5x (binance P3) |
| 4 | **RsiOversoldRecovery** | 2 | RSI<35 + 2 ardışık yükseliş (architect + binance P4) |
| 5 | **VolumeSpikeDonchian** | 4 | volume>2.5x + Donchian-20 üst kırılım — **en güçlü** (binance P5 + architect merge) |
| 6 | **HigherLowEmaTouch** | 2 | Uptrend + EMA21 touch + bounce (binance P6 + architect Ema9Slope merge) |
| 7 | **MacdZeroCross** | 2 | MACD(12,26) zero-line aşağıdan yukarı kesim (binance P7) |
| 8 | **BullishEngulfing** | 2 | 2-bar engulfing reversal (architect) |
| 9 | **HammerReversal** | 2 | Lower wick > body × 2 + close > open (architect) |
| 10 | **BollingerLowerReversal** | 2 | Close BBLower'a değdi + sonraki bar yukarı (architect) |

**Skor tavanı**: 24. Default emit threshold: **5** (~%21, agresif emit, frekans odaklı).

### Hard-Gate'ler (2 adet — fail = skip)
| # | Gate | Trigger |
|---|------|---------|
| H1 | **VolumeSurgeGate** | currentVolume > avg20 × 1.0 (warmup<20 bypass) |
| H2 | **SpreadGuardGate** | bidAskSpread/mid < 0.001 (XRP/ADA korunur) |

### Soft Filter (1 adet — skor düşür)
| # | Filter | Etki |
|---|--------|------|
| S1 | **AdxRegimeFilter** | ADX 15-35 dışı ise skor × 0.7 (no skip — L80 no-man's-land sorunu engellendi) |

## 3. Geometri

```
Entry: bar close (5m)
SL: max(ATR14 × 1.2, 0.6%)  // MaxSLPct 0.006
TP: SL × 2.0  // R:R 1:2
Trailing: peak × (1 - 0.0015), aktif: UPnL > +0.10%
BE: UPnL > +0.10% → SL = entry
MaxHold: 60dk (12 bar @ 5m)
```

## 4. Risk Profili

```json
{
  "MaxOpenPositions": 3,
  "MaxConsecutiveLosses": 4,
  "CooldownBars": 2,
  "MaxSLPct": 0.006,
  "MaxDrawdown24h": 0.20,
  "MinFreeMarginPct": 0.30
}
```

## 5. Implementation — 8 Commit Plan (architect)

| Commit | İçerik |
|--------|--------|
| 1 | Application port'ları (BarSnapshot, IPatternDetector, IPatternRegistry, IPatternSignalComposer, CompositeSignalDecision, PatternComposerOptions) |
| 2 | MarketIndicatorService.TryGetBarSnapshot impl (eski 2 method backward compat geçici) |
| 3 | 10 detector + 2 hard-gate + 1 soft-filter (13 sınıf) + unit testler |
| 4 | PatternRegistry + WeightedScorePatternComposer + 8 unit test |
| 5 | PatternCompositeEvaluator + DI swap (KMS+BBR sil, PatternComposite ekle) |
| 6 | StrategyEnums temizlik + KMS/BBR/Donchian dosyalarını SİL + IMarketIndicatorService temizle |
| 7 | appsettings.json 5 PatternComposite seed + Migration `Loop81PatternPivot` (Strategies/Signals/Positions/Orders/OrderFills DELETE) |
| 8 | Indicators.Macd ekle (binance-expert AKSIYON-1) |

## 6. Yeni Indicator (Indicators.cs)

```csharp
// Macd line: EMA(close, fast) - EMA(close, slow)
// Signal line: EMA(macdLine, 9) — Loop 81'de SADECE zero-cross kullanıyor, signal opsiyonel
public static double Macd(IReadOnlyList<Kline> bars, int fast = 12, int slow = 26)
{
    if (bars.Count < slow + 1) return 0;
    var emaFast = Ema(bars.Select(b => b.Close).ToList(), fast);
    var emaSlow = Ema(bars.Select(b => b.Close).ToList(), slow);
    return emaFast - emaSlow;
}
```

## 7. Loop 81 Boot Sırası

1. Backend-dev 8-commit deploy (architect plan + binance-expert spec sentezi)
2. Bot kill (PID 16752)
3. `dotnet build` warn-free
4. `dotnet test` tüm geç (mevcut 307 + yeni ~40 = ~350)
5. Bot restart
6. **DB tam reset**: `POST /api/papertrade/reset` (backend-dev'in yeni endpoint'i — Positions+Orders+Events purge cascade)
7. UI smoke: dashboard $500 başlangıç, 0 işlem
8. boot.md yaz, commit, push
9. ScheduleWakeup t30 (30dk içinde ≥3 emit ≥1 fill bekle)

## 8. KPI ve Halt Eşikleri

| Metrik | Hedef | Halt |
|--------|-------|------|
| Emit/h | ≥8 | <2 4h → loosen pattern threshold |
| WR | ≥45% | <30% 4h → param spiral red flag |
| Realized 4h | ≥-$0.50 | < -$2.00 → halt + Loop 82 |
| Consec SL | ≤4 | 5+ → CB tripped (auto) |

## 9. Frekans Beklenti (binance-expert)

5 coin × 12 bar/h × 10 pattern = teorik 600 evaluation/h. Gerçekçi:
- Hard-gate fail %50 → 300
- Threshold ≥5 fail %95 → 15 emit/h
- Cooldown 2 bar (10dk) → 8-12 emit/h efektif
- Hedef: **8-12 emit/h** (mevcut 1.5 emit/h'ten 5-8x iyileşme)

— PM 2026-05-02 Loop 81 spec sentezi (architect + binance-expert + Explore + backend-dev fix)
