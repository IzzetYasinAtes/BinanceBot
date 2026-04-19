# Loop 20 Boot — 2026-04-19 10:29 UTC

## Başlangıç durumu (temiz reset)

- API: `http://localhost:5188` çalışıyor
- DB: Tüm tablolar sıfırlandı (Orders/Positions/StrategySignals/OrderFills/SystemEvents/OrderBookSnapshots/BookTickers DELETE)
- VirtualBalances: $100.00 başlangıç, $100.00 cash, ResetCount++
- Aktif stratejiler: 3 adet VwapEmaHybrid
  - BTC-VwapEma-Scalper (id=7)
  - BNB-VwapEma-Scalper (id=8)
  - XRP-VwapEma-Scalper (id=9)

## Reform özeti (Loop 19'dan aktarılan)

- Algoritma: VWAP-Bounce + EMA21(1h) trend filter hibrit (spot long-only)
- 4 koşul giriş: EMA21↑ ∧ prev<VWAP ∧ last>VWAP ∧ vol≥SMA20×1.2
- TP=swingHigh(20)×0.95, SL=entry×(1-%0.8), timeStop 15dk
- Sizing: `max(equity × 0.20, 20)` USD — kartopu kuralı
- WS stream: 1m + 1h (reviewer BLOCKER-1 fix)

## Health check programı

- t30 (11:00 UTC): WS state, warmup tamam mı, snapshot null mi, log hata
- t90 (12:00 UTC): Sinyal sayısı, Order sayısı, PnL trend, TP hit
- t150 (13:00 UTC): Orta iaşe — WR, fee drag, DD
- t210 (14:00 UTC): Son aşama değerlendirme
- t240 (14:30 UTC): Final özet + karar (HOLD/MUTATE/PAUSE)

## Red flag listesi (bozuksa loop halt)

- CB Tripped + tüm stratejiler Paused
- 30+ dk sinyal var, order 0
- Equity dramatic dump (%50+ peak'ten)
- Error log flood (>5 distinct error/30dk)
- WS Disconnected/Faulted 5+ dk
- 3 ardışık zarar → PAUSE (kullanıcıya sor, wakeup kurma)
