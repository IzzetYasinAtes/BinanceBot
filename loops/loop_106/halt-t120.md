# Loop 106 Halt — t120 R:R 1:1 Simetri Yetmedi

Tarih: 2026-05-05 10:02 UTC | Boot: 07:38 UTC | Süre: 2h24m

## Halt: realizedPnl -$0.92 + UPnL -$0.72 = netPnl -$1.87 (eşik aşılma yakın)

### Closed (2, 0W/2L)
| # | Symbol | Direction | RPnL |
|---|---|---|---|
| 1 | ADAUSDT | Long | -$0.316 |
| 2 | SOLUSDT | Long | -$0.601 SL hit |

### Open (3, hepsi peak entry yakını)
| Symbol | Hold | UPnL | Peak/Entry-1 |
|---|---|---|---|
| XRPUSDT | 122min | -$0.35 | +0.071% |
| ETHUSDT | 32min | -$0.21 | +0.035% |
| SOLUSDT | 32min | -$0.16 | +0.021% |

3 pos toplam UPnL -$0.72.

## R:R 1:1 Simetri Sonuç

Loop 105+106 R:R 1:1 simetri test:
- TP %0.40 hit edilmedi (peak max +0.07%)
- BE-stop bazı durumlarda küçük loss (-$0.04-$0.05) çalıştı
- AMA SL hit -$0.6 patternini ÇÖZMEDİ
- Pos açıldıktan sonra mark düşüş aynı problem

## 26 Loop Cumulative

26 loop -$26.5+, 0 pozitif loop. Parametrik tune (RS, MTF, BE, RPT, R:R) tüketildi.

**ADR-0026 §A Pullback Limit Order zorunlu** — bar close anında market emit yerine limit order @ bar_close × 0.999 (-%0.10 offset). Pos açılışı tepe yerine geri çekilme noktasında.

## Loop 107 Spec

ADR-0026 §A:
1. Domain: PendingOrder aggregate veya Order.IsLimit + LimitPrice + ExpiresAt
2. Application: PlaceOrderCommand OrderType.Limit + LimitPrice + Timeout
3. Infrastructure: BinanceFuturesClient.PlaceLimitOrder + PendingLimitTimeoutWorker (5dk cancel)
4. Test: integration limit order fill + timeout cancel
5. Migration: Order.LimitPrice + ExpiresAt nullable column

backend-dev 4 commit, ~2-3 saat.

## Sonraki

backend-dev delegasyon başlatılacak.
