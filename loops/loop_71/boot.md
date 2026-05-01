# Loop 71 Boot — KMS Skor-Tabanlı Evaluator (2026-05-01 04:56 TR)

## Pivot Sebebi
Loop 70 t60 KESIN halt: 0 emit/65dk. Daha gevşek param (RSI 38, TC 0.6) bile yetmedi. KMS AND gate yapısı + RSI cross gate inherently nadir tetikleniyor (0.10 × 0.55 × 0.45 × 0.90 × 0.85 ≈ %1.9, 53 bardan 1).

## binance-expert Refactor Spec (uygulandı)

**Skor Formülü (6 puan, min 4/6):**
| Gate | Puan | Tip |
|---|---|---|
| RSI Zone (oversold + momentum) | 0-2 | must-have (≥1 zorunlu) |
| EMA9 pozitif slope | 0-1 | nice-to-have |
| TradeCount surge | 0-1 | nice-to-have |
| Spread filter | 0-1 | hard-gate (0 = skip) |
| MinAtrPct | 0-1 | hard-gate (0 = skip) |

**RSI Zone + Momentum (cross DEĞİL):**
- `Rsi14 < 40 AND Rsi14 > Rsi14Prev` → 2 puan
- `Rsi14 < 52 AND Rsi14 > Rsi14Prev` → 1 puan
- diğer → 0

**Skor bazlı dinamik TP/SL:**
- 4/6 → TpMul 1.5, SlMul 0.85, MaxHold 30
- 5/6 → TpMul 1.8, SlMul 0.75, MaxHold 45
- 6/6 → TpMul 2.2, SlMul 0.65, MaxHold 60

**CoinClass (asimetri çözümü):**
- BTC, ETH → large (MinAtrPct 0.0002)
- SOL → mid (MinAtrPct 0.0003)
- XRP, ADA → alt (MinAtrPct 0.0004)

**StreakGuard skeleton (Loop 72):**
ICooldownService.GetCurrentScoreThreshold() — şimdilik sabit 4.

## Implementation Özeti (backend-dev)
| Dosya | Değişiklik |
|---|---|
| `KmsMomentumEvaluator.cs` | Komple yeniden yaz: skor mantığı + RSI Zone + CoinClass switch + dinamik TP/SL |
| `ICooldownService.cs` | `GetCurrentScoreThreshold(strategyId, symbol)` metodu |
| `CooldownService.cs` | Stub: return 4 (Loop 72 streak guard placeholder) |
| `appsettings.json` | 5 KMS seed yeni param JSON şeması (CoinClass per coin) |
| `KmsMomentumEvaluatorTests.cs` | 5 test → 12 test (Score 4/5/6 emit, Skor 3 skip, Spread/MinAtr/RsiZone hard-gate, CoinClass large/alt) |

**Build & Test:**
- 0 uyarı / 0 hata (Infrastructure + Tests)
- 233/233 test geçti
- Eski `RsiRecoveryThreshold` / `TradeCountMultiplier` / `MinAtrPct` SİLİNDİ (Golden Rule #13)

## Boot State
| Metrik | Değer |
|---|---|
| Cash / Equity | $500 / $500 (DB reset) |
| Active | 5 KMS (BTC=large, ETH=large, SOL=mid, XRP=alt, ADA=alt) |
| Param güncellendi | DB UPDATE Strategies WHERE Name LIKE '%-KMS' (5 row, per coin CoinClass) |
| Bot PID | 18100 (BinanceBot.Api.exe) |
| WS State | Streaming ✓ |

## Beklenti (binance-expert frekans tahmini)
- 5m × 5 coin × cooldown 3 bar → max 20/h üst sınır
- Gerçekçi: **8-15 emit/h** (skor tabanlı + CoinClass)
- Hedef: en az 4 emit / 30dk

## Halt Eşikleri
- Realized < -$1.50 → Loop 72 binance-expert (StreakGuard implement)
- 5+ ardışık SL → otomatik halt
- 0 emit (60dk) → algoritma fundamental sorun (binance-expert tekrar)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (05:26 TR)**

— PM 2026-05-01 Loop 71 boot
