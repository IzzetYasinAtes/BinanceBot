# Loop 74 Boot — binance-expert Quick-Win Param Tune (2026-05-01 11:30 TR)

## Pivot Sebebi
Loop 73 halt: 5 trade hepsi `order_timestop` (Loop 72 patterni tekrar), CB tripped. t90'da 4/4 açık pozitif UPnl idi, t120'de fiyat geri döndü, TP %0.3 hit edemeden MaxHold 30dk timestop loss.

binance-expert teşhisi: **"Slow-bleed timestop"** — pozisyon entry'den +%0.10 hareket ediyor, TP %0.3'e ulaşamıyor, geri çekiliyor, SL %0.2'e değmiyor, MaxHold loss.

## binance-expert Quick-Win Spec

| Parametre | L73 | **L74** | Mantık |
|---|---|---|---|
| `MinScoreThreshold` | 4 | **5** | Sadece güçlü entry (5/6 skor) |
| `RsiNeutralCeiling` | 60 | **50** | Sıkı RSI (oversold momentum) |
| `TpAtrMultiplier` | 1.2 | **1.5** | TP biraz geniş |
| `TpAtrMultiplierLow` | 1.0 | **1.3** | |
| `TpAtrMultiplierHigh` | 1.5 | **1.8** | |
| `SlAtrMultiplier` | 0.55 | **0.60** | SL biraz gevşek |
| `MinTpPct` | 0.003 | **0.002** | Min TP daha küçük |
| `MaxTpPct` | 0.015 | **0.012** | Max TP %1.2 |
| `MaxHoldMinutes` | 30 | **35** | MaxHold biraz uzun |

**Önerilmedi (Loop 75'e ertelendi):**
- Break-even SL move (kod değişikliği — backend-dev iş)
- Trailing stop
- BBW regime filter (choppy/trending)
- EMA200 trend gate

## Boot State
| Metrik | Değer |
|---|---|
| Cash / Equity | $500.31 (carry-over) — Realized -$0.40 hesaba katılmadı (DB UPDATE değil) |
| GERÇEK Realized: | -$0.086 (L71+L72+L73 cumulative) |
| Active | 5 KMS (Status=3 reactivated) ✓ |
| Bot PID | 7548 (RESTART YOK — DB fresh okuma) |
| **CB API Reset** | **200 OK** ✓ |
| Strategy params | DB UPDATE 5 row (yeni param) |

## ÖĞRENILEN: API Payload PascalCase
`{"AdminNote":"..."}` ✓ (`{"adminNote":"..."}` 400 verir). ASP.NET Core minimal API JSON binding case-sensitive. Memory güncellendi.

## Beklenti
- L73 timestop pattern kırılsın (TP %0.5-1.2, MaxHold 35dk)
- MinScore 5 ile emit frekansı düşer ama kalite artar (5-10/h)
- Realized > $0 hedef, TP hit oranı kritik metrik

## Halt Eşikleri
- Realized < -$0.30 (Loop 74) → Loop 75 break-even SL implement (backend-dev)
- Circuit breaker tekrar tripped → API reset + Loop 75 algoritma overhaul
- 0 emit (60dk) → MinScore 5→4 geri al

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (12:00 TR)**

— PM 2026-05-01 Loop 74 boot
