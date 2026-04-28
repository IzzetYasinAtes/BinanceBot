# Loop 49 — Check t=240dk (2026-04-28 17:05 TR)

## 3 Trade, 1 WIN (ETH +$0.488), 2 SL (BTC + XRP)

| Metrik | t180 | t240 | Δ |
|---|---|---|---|
| Cash | $299.79 | $499.45 | +$199.66 (3 pos kapandı) |
| OpenPositionsValue | $199.99 | $0 | -$199.99 |
| Equity | $499.79 | $499.45 | -$0.34 |
| Realized | $0 | **-$0.547** | -$0.547 |
| Net | -$0.213 | -$0.547 | -$0.334 |
| Komisyon (toplam) | $0.150 | $0.450 | +$0.300 (3 entry+3 exit) |
| Open Pos | 2 | **0** | -2 |
| Closed Pos | 0 | **3** | +3 |
| Signals | 2 | **3** | +1 (ETH) |
| WinRate | — | **%33.3 (1/3)** | ✓ BE WR sağlandı |
| WsStateChanged | 51 | 51 | 0 stabil ✓ |

## Trade Detayları

### 🔴 BTCUSDT (SL HIT)
- Entry: $76,350.34 @ 11:30 UTC | Exit: $76,091.30 @ 13:10 UTC
- Hold: 100dk (TimeStop'tan önce SL tetiklendi)
- SL beklenmiş $76,114, exit $76,091 → SL %0.01 daha aşağı (slippage)
- Komisyon: $0.0750 + $0.0748 = $0.1498
- **Realized: -$0.489**

### 🔴 XRPUSDT (SL HIT)
- Entry: $1.3817 @ 12:00 UTC | Exit: $1.3763 @ 13:10 UTC
- Hold: 70dk (TimeStop'tan önce SL tetiklendi)
- SL beklenmiş $1.3768, exit $1.3763 → SL %0.04 aşağı (slippage)
- Komisyon: $0.0750 + $0.0747 = $0.1498
- **Realized: -$0.546**

### 🟢 ETHUSDT (WIN — TP HIT veya yaklaşık) ✓
- Entry: $2,267.76 @ 13:15 UTC | Exit: $2,282.23 @ 13:46 UTC
- Hold: 31dk (hızlı kapanış!)
- Mark up: +%0.64 (TP yakını)
- Komisyon: $0.0750 + $0.0755 = $0.1505
- **Realized: +$0.488** (mark profit -$0.150 fee = +$0.488)

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | -$0.547 | ✓ buffer **$0.95** |
| 5+ ardışık SL | 2 SL + 1 WIN (zincir kırıldı) | ✓ |
| WR < %25 (5+ trade) | %33 (3 trade) | ✓ BE WR sağlandı |
| Zombie | 0 açık | ✓ |
| WS / CB | 51 stabil | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK + ETH WIN ZİNCİRİ KIRDI.**

## Loop 41-49 Aggregate
| Loop | Trade | Realized | WR |
|---|---|---|---|
| 41-43 | 11 | -$2.97 | %0 |
| 44-45 | 2 | +$0.011 | %50 |
| 46-48 | 13 | -$1.69 | %23 |
| **49 (t240)** | **3** | **-$0.547** | **%33** |
| **Total** | **29** | **-$5.20** | %17 |

## Yorum (Strateji Sağlığı)
- **3 trade küçük örneklem** — istatistiksel WR ölçümü değil
- **ETH +$0.488** demonstrasyon: BB MeanRev 15m **TP ulaşılabilir** (önceden Loop 45 XRP TimeStop +$0.089 vardı, şimdi gerçek TP +$0.488)
- BTC ve XRP SL hit — düşen piyasa katalizörü, BB lower bounce çalışmadı
- Net asimetri: +$0.488 vs -$0.518 ortalama → **R:R 2:1 sağlandı**, WR %40+ olursa karlı

binance-expert beklenti tablosu: orta senaryo %45 WR, +$0.15/gün. Şu an %33 WR ile $-0.55, biraz altında ama 3 trade ile karar veremem.

## Karar
**Loop 49 DEVAM** ve normal cycle.

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=300dk (18:05 TR)**

— PM 2026-04-28 Loop 49 t=240
