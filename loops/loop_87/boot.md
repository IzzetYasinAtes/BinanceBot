# Loop 87 Boot — MTF 15m EMA Slope + RSI Cap 75 (2026-05-03 04:14 TR)

## Pivot Sebebi
Loop 85+86 = 6/6 yeni param emit kötü başlangıç (peak=0, BE armed olmadan SL). Hard-gate ON+OFF ikisi de işe yaramadı. binance-expert tanı: **5dk sinyal büyük TF yönüne aykırı entry = sahte breakout**.

## Loop 87 Çözüm — Senaryo A + D
**A. MTF 15m EMA21 slope onayı**: 5dk pattern emit etmeden önce 15m EMA21 yukarı eğimde olmalı (büyük TF doğrulama).

**D. RSI cap**: RSI14 > 75 → emit yok (aşırı alım sahte breakout filtresi).

## backend-dev Implementasyon (327/327 PASS)

### Değişiklikler
| Dosya | Değişiklik |
|-------|-----------|
| `BarSnapshot.cs` | +`Ema21_15m`, `Ema21Prev5_15m` alanlar |
| `PatternComposerOptions.cs` | +`RsiMaxEmit = 75m` |
| `MarketIndicatorService.cs` | 15m buffer (cap=200) + WarmupAsync 15m REST + RunAsync 15m WS |
| `PatternCompositeEvaluator.cs` | 2 yeni gate: MTF slope, RSI cap |
| `appsettings.json` | KlineIntervals `["5m","15m"]` + `RsiMaxEmit:75` (5 strateji) |

### Yeni Test (6 senaryo)
- Mtf15mSlopeDown_ReturnsNullSkip
- Mtf15mWarmupNotReady_ReturnsNullSkip
- Mtf15mSlopePositive_StillEmits
- Rsi14AboveCap_ReturnsNullSkip
- Rsi14AtCap_StillEmits (strict `>` semantic)
- RsiCapOverridenLow_SkipsEvenWithinDefault

## Boot State
| Metrik | Değer |
|---|---|
| Bot PID | 16280 |
| Port | 5188 |
| Build | 0/0 ✓ |
| Tests | 327/327 PASS |
| 5 Pattern Strateji | Active (RsiMaxEmit=75) |
| KlineIntervals | 5m + 15m ✓ |
| MTF Gate | Active (15m EMA21 slope > 0) |
| RSI Cap | 75 |
| Hard-gate | Active (volume_surge + spread_guard) |
| RequiredScore | 5 (appsettings) — DB'de hâlâ 3 (Loop 86 manuel) |
| BE.OffsetPct | 0.0020 (Loop 83) |
| Trail.TrailPct | 0.0050 (Loop 83) |
| MaxHold | 0 (yok) |
| Tick | 5s (Loop 85) |
| Slippage | 5bp (Loop 85) |
| BNB indirimi | off |
| **15m WS Warmup** | **~50h tarihsel veri** (REST backfill) |

## Loop 86 Carryover
- 3 açık (ADA/SOL/BTC) yeni param ile entry alındı, mevcut SL/TP/Trailing/BE-stop ile kapanır
- Yeni emit'ler L87 paramı (MTF + RSI cap) ile

## Beklenen Etki
- Sahte breakout %50+ azalır (büyük TF aleyhine emit eler)
- Frekans 4-6 emit/h (Loop 86 RequiredScore 3 → daha çok skor + MTF onay)
- Kalite: peak +%0.30+ trade'ler artar (gerçek momentum)

## L80→L87 Stack
| Loop | Ana Değişiklik | Net Realized |
|------|----------------|---------------|
| L80 | ADX gate + BBR vol + counter | -$0.52 |
| L81 | Pattern-based scalping | -$0.38 |
| L82 | Trailing 0.0025, BE 0.0020 | -$0.22 |
| L83 | BE Offset 0.002, Trail 0.0050 | $0 |
| L84 | Hard-gate skip kaldırıldı | -$0.004 |
| L85 | UI cash fix + tick 5s + paper realism + MaxHold 0 | -$0.168 |
| L86 | Hard-gate geri + RequiredScore 4→3 | -$0.171 (carryover) |
| **L87** | **MTF 15m EMA slope + RSI cap 75** | **HEDEF +$0.30+** |

## Cumulative L1-L86: -$15.55 worst case (3 açık SL hit varsayım)
## Loop 87 Hedef: İlk pozitif loop (Realized > $0)

## Halt Eşikleri
- Realized < -$1.50 → halt + Loop 88
- 4+ ardışık SL → MTF gate çalışmıyor
- 0 emit 1h → MTF çok katı

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (04:44 TR)** — MTF + RSI cap sonrası ilk emit kalitesi

— PM 2026-05-03 Loop 87 boot (MTF 15m EMA slope + RSI cap 75, sahte breakout fix, kullanıcı tatil otonom)
