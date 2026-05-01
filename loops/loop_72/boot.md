# Loop 72 Boot — Param Tune + Carry-Over (2026-05-01 07:07 TR)

## Pivot Sebebi
Loop 71 t120: 4 zombi pozisyon Status=2 (Closing) bug — Hold süreleri 49-89dk MaxHold geçti, kapanmadı. PaperFill exit emir state machine bug (Loop 62 ile aynı pattern). Bot restart + state temizleme + frekans için param tune.

## Loop 71 Final Realized: **+$0.85** ✓

| Pozisyon | Side | Hold | PnL | Tip |
|---|---|---|---|---|
| ADAUSDT (10480) | Long | ~30min | -$0.089 | TimeStop SL |
| XRPUSDT (10479) | Long | ~45min | -$0.068 | TimeStop SL |
| **ETHUSDT (10481)** | Long | ~5min | **+$0.557** ✓ | **TP** |
| **BTCUSDT (10482)** | Long | ~10min | **+$0.450** ✓ | **TP** |

**Total: +$0.850** (WR %50, ama ETH/BTC TP win'leri loss'ları katladı)

## Loop 72 Param Değişikliği (sadece DB UPDATE, kod değişikliği yok)

| Parametre | L71 | **L72** |
|---|---|---|
| `RsiNeutralCeiling` | 52 | **60** (1 puan zone genişler) |
| `MinScoreThreshold` | 4 | **3** (frekans için permisif) |
| `MaxTpPct` | 0.018 | **0.025** (TP fırsatlarında daha çok kar) |

Diğer param sabit. Coin başına CoinClass aynı (BTC/ETH=large, SOL=mid, XRP/ADA=alt).

## Boot State
| Metrik | Değer |
|---|---|
| Cash / Equity | **$500.85** / $500.85 (Loop 71 carry-over) ✓ |
| StartingBalance | $500 |
| Net PnL | +$0.85 |
| Active | 5 KMS (param tune sonrası) |
| Bot PID | 17624 |
| WS State | Streaming ✓ |
| DB reset | Positions/Orders/SystemEvents/BookTickers (8 row, 8 row, 173 row, 5 row) |

## Beklenti
- Frekans: 4-8 emit/h (MinScore 3 ile permisif)
- Hedef: en az 2 emit / 30dk
- Realized hedef: +$1.50 cumulative (carry-over $0.85 + yeni emit kar)

## Halt Eşikleri
- Realized < $0 (Loop 72 specific) → Loop 73 binance-expert
- 5+ ardışık SL → otomatik halt
- 0 emit (60dk) → Loop 73 daha permisif (MinScore 3 → 2 — risk artışı)
- ZOMBI bug tekrar → Loop 73 backend-dev: PaperFill state machine fix

## Bilinen Sorun (Loop 73'te ele alınacak)
**Zombi Position bug**: PositionClosed event yayınlanıyor ama Position row Status=3'e geçmiyor. Manuel müdahale gerekiyor. backend-dev: PaperFillSimulator + PositionService state transition fix. PR: feature/zombi-position-fix.

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (07:37 TR)**

— PM 2026-05-01 Loop 72 boot
