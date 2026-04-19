# Loop 22 Boot — 2026-04-19 17:22 UTC

## Başlangıç
- API: `http://localhost:5188` çalışıyor
- DB: Tam temiz reset, $100, 3 VwapEmaHybrid strateji
- ETHUSDT Binance.Symbols'ta (Loop 22 fix #4)
- SystemEventPublisher aktif

## Reform özeti (ADR-0017 + Loop 21 agresif parametreler)
| Parametre | Değer |
|---|---|
| SlopeTolerance | −0.003 |
| VwapTolerancePct | 0.005 |
| VolumeMultiplier | 0.5 |
| TpGrossPct | 0.005 (%0.5 gross / %0.3 net) |
| StopPct BTC/BNB | 0.003 |
| StopPct XRP | 0.004 |
| MaxHoldMinutes | 10 |

## Loop 22'nin getirdiği fixler (Loop 21 10 bug)
1. ✓ Sizing $20 (targetNotional = max(equity×0.20, 20))
2. ✓ DuplicateSignalProtection (StrategyId+Symbol+Mode açık pos check)
3. ✓ TimeStop maxHoldMinutes ContextJson key fix
4. ✓ ETHUSDT Binance.Symbols
5. ✓ Position card TP/SL kv + mesafe
6. ✓ Cash negatif UI
7. ✓ BTC/ETH/BNB/XRP gerçek SVG logoları
8. ✓ BinanceBot marka logosu + hover rotate
9. ✓ Logs filter chip reactive + count + empty
10. ✓ Hero 30 → 23px

## Loop 22 kalite kapısı
- build 0/0 ✓
- test 193/193 yeşil ✓
- reviewer READY 0 blocker ✓
- tester PARTIAL PASS 10/10 ✓

## Health check programı
- t30 (17:52 UTC), t90 (18:52), t150 (19:52), t210 (20:52), t240 final (21:22)

## Red flag
- Sizing hala $40 → backend fix bug'lı
- MaxHoldDurationSeconds NULL → TimeStop fix bug'lı
- 3 ardışık zarar → PAUSE
- WS disconnect 5+ dk → halt
