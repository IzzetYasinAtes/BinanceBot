# Loop 68 — Check t=30dk (2026-05-01 01:01 TR)

## Boot State
- Bot PID: 20004 (BinanceBot.Api.exe + dotnet 20116) ✓
- API health: portfolio summary 200, Cash $500 / Equity $500 ✓
- 5 KMS strategies aktif (BTC/ETH/XRP/SOL/ADA, Status=3=Active)

## Sayım (Loop 68 boot 21:31 UTC sonrası, 30dk)
| Metrik | Değer |
|---|---|
| **SignalEmitted** | **0** |
| **SignalSkipped** | **30** (5 coin × 6 bar = beklenen) |
| OrderPlaced | 0 |
| OrderFilled | 0 |
| RiskAlert | 0 |
| Realized PnL | $0 |
| Closed Positions | 0 |
| Open Positions | 0 |

## Skip Detayı
Tüm SignalSkipped event'leri `reason=evaluator_skip` — KMS AND gate'lerinin biri/birkaçı false. Detail içinde hangi gate olduğu yok (evaluator `LogDebug` ile yazıyor, bot `Information` level — DB'ye düşmüyor).

## KMS AND Gate Yapısı (KmsMomentumEvaluator.cs:113-160)
1. **RSI Recovery cross**: `Rsi14 > 35 AND Rsi14Prev < 35` — *bar başına ÇOK nadir kesişme*
2. **EMA9 slope**: `Ema9Now > Ema9Prev`
3. **TradeCount surge**: `Cur > Avg20 × 0.8`
4. **Spread**: `(Ask−Bid)/Ask < 0.005`
5. **MinAtrPct**: `Atr/Close >= 0.0005`

→ Bottleneck büyük ihtimalle **RSI cross gate** (recovery cross sadece tam o barda RSI eşiği geçerken tetikleniyor).

## Karar (mantık matrix)
| Şart | Aksiyon |
|---|---|
| 0 emit / 30dk | **Loop 68 devam, t60 KESIN bekle** |
| RiskAlert = 0 | ✓ Sistem sağlıklı |
| Realized > -$1 | ✓ ($0) |

## t60 KESIN Plan (01:31 TR)
- 0 emit → **Loop 69 binance-expert pivot**: AND gate'leri OR/skor tabanına çevir, "RSI cross" yerine "RSI < threshold continuous" + score-based emit
- ≥1 emit → Loop 68 devam, ScheduleWakeup t90

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (01:31 TR) KESIN**

— PM 2026-05-01 Loop 68 check-t30
