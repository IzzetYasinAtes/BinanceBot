# Loop 43 — Check t=150dk (2026-04-24 20:07 TR)

## ADAUSDT Sonucu — SL HIT (16dk 8sn hold)

| Metrik | Değer |
|---|---|
| Symbol | ADAUSDT LONG |
| Entry @ 16:00 UTC (19:00 TR) | $0.252225 |
| StopPrice (SL) | $0.251569 (-%0.26) |
| TakeProfit | $0.254143 (+%0.76) |
| R:R tasarımı | 2.92 |
| Exit @ 16:16 UTC | $0.251475 (SL'den $0.0001 aşağı = SL hit + slippage) |
| Hold | 16dk 8sn / MaxHold 90dk |
| Mark loss | $0.297 |
| Komisyon (entry+exit) | $0.0750 + $0.0748 = $0.1498 |
| **Realized PnL** | **-$0.4473** |

## DB Sayım
| Metrik | t90 | t150 | Δ |
|---|---|---|---|
| Cash | $399.9177 | $499.5527 | +$99.64 (ADA pos kapandı, $100 - loss döndü) |
| Equity | $499.7762 | $499.5527 | -$0.22 (mark loss + komisyon) |
| netPnl | -$0.2238 | -$0.4473 | -$0.22 |
| Pos Open | 1 | 0 | -1 ✓ |
| Pos Closed | 0 | 1 | +1 |
| Order Total | 1 | 2 | +1 |
| Signals | 1 | 1 | 0 (yeni signal yok) |
| Fills | 1 | 2 | +1 |
| EvtSkip (60dk) | 486 | 464 | normal |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | -$0.4473 | ✓ buffer **$1.05** |
| 5+ ardışık SL | 1 | ✓ |
| Zombie | 0 açık | ✓ |
| WS / CB | Streaming, drift -544ms, HEALTHY | ✓ |

**HALT YOK.**

## Loop 41/42/43 Karşılaştırma (gerçek trade verileri)
| Loop | Trade | TP | SL | Realized | Sebep |
|---|---|---|---|---|---|
| 41 (halt t210) | 8 | 0 | 8 | -$1.7985 | LTC whipsaw (cooldown yok) |
| 42 (stagnation) | 2 | 0 | 2 | -$0.7262 | XRP+SOL eşzamanlı SL |
| 43 (devam) | 1 | 0 | 1 | -$0.4473 | ADA SL (R:R 2.92, beklenti dahilinde) |

**Toplam 11 trade, 0 TP, 11 SL, %0 WR.** AR-GE %35-45 WR beklentisi sağlanmadı. 

Bu çok önemli bir gözlem — ya:
1. Strateji matematiği gerçek piyasada geçerli değil (false breakout oranı yüksek)
2. Test örneklemi çok küçük (11 trade istatistiksel anlam taşımaz, AR-GE 50+ trade'de WR ortaya çıkar)
3. Piyasa rejimi şu an Donchian breakout için kötü (downward + low vol Asya/Avrupa geçiş)

**Pragmatik karar:** Loop 43 devam edip toplam 5+ trade'e ulaşılmadan strateji üzerinde nihai karar verme erken. ADA tek SL ile loop devam.

## Playwright Smoke (1 sayfa)
- ui-t150-01-dashboard.png — Hero -$0.4473/-%0.09, Saat-Başı İşlem 1/150, Canlı İşlem Akışı'nda ADA satırı, Piyasa hero karışık (BTC/ETH/BNB/XRP hepsi kırmızı)
- Console error 0

## Sıradaki Wakeup
**ScheduleWakeup 3600 → t=210dk (21:07 TR)**

Beklenti:
- ADA cooldown 90dk dolacak ~17:46 UTC (20:46 TR)
- 9 fresh coin (BTC, ETH, XRP, SOL, DOGE, LINK, DOT, AVAX, TRX) hala fresh
- Avrupa pik dilimi yaklaşıyor (15-19 UTC = 18-22 TR)

— PM 2026-04-24 Loop 43 t=150
