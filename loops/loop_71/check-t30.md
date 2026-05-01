# Loop 71 — Check t=30dk (2026-05-01 05:28 TR)

## Sonuç: 0 emit / 30dk — Skor sistemi de tetiklenmedi (KESIN PROBLEM)

binance-expert spec'in 4/6 min skor + RSI Zone + Momentum + CoinClass uygulaması ilk 30dk'da **0 emit** üretti. Beklenti 4-7 emit/30dk idi (8-15/h tahmini).

## Sayım (~30dk)
| Metrik | Değer |
|---|---|
| **SignalEmitted** | **0** ⚠️ |
| **SignalSkipped** | **30** (5 coin × 6 bar) |
| OrderPlaced | 0 |
| OrderFilled | 0 |
| RiskAlert | **0** ✓ |
| PositionOpened | 0 |
| Realized | $0 |

## Loop Karşılaştırma
| Loop | Algoritma | t30 emit |
|---|---|---|
| L68 | AND, RSI 35, TC 0.8 | 0 |
| L70 | AND, RSI 38, TC 0.6 | 0 |
| **L71** | **Skor 4/6, Zone+Mom, CoinClass** | **0** ⚠️ |

→ Algoritma değişikliği bile ilk 30dk'da emit getirmedi. Yapısal teşhis lazım.

## Hipotezler (öncelik sırası)
1. **Spread hard-gate (0.005 = %0.5) sürekli 0 puan** — testnet liquidity düşük, spread genelde %0.5'in üstünde olabilir
2. **RSI Zone gate hep 0 puan** — Rsi14 hep 52 üstü (overbought rejimde) ya da Rsi14 < Rsi14Prev (düşüş trendinde)
3. **CoinClass MinAtr eşikleri çok yüksek** — testnet'te ATR/Close oranı küçük olabilir

## Teşhis Eksiği
SignalSkipped event'in `details` field'ında skor + gate bilgisi YOK (sadece `reason: "evaluator_skip"`). Evaluator skor hesaplıyor ama event payload'a yazmıyor — backend-dev refactor'unda diagnostic logging eksik.

## Karar
| Şart | Aksiyon |
|---|---|
| 0 emit / 30dk | **Loop 71 devam, t60 KESIN bekle** |
| t60 hala 0 emit | **Loop 72 KESIN diagnostic refactor**: SignalSkipped payload'a skor + gate detay ekle + Spread hard-gate gevşet (0.005 → 0.02) + RsiOversoldZone (40 → 50) |
| RiskAlert = 0 | ✓ |

## t60 KESIN (05:56 TR) Plan
- ≥1 emit → Loop 71 devam, t90
- 0 emit → **Loop 72**: backend-dev quick fix (skip event'e skor + gate JSON) + Spread/RSI eşik gevşet + reset + restart

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (05:58 TR)**

— PM 2026-05-01 Loop 71 check-t30
