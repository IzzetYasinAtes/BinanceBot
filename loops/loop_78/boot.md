# Loop 78 Boot — BBW Hard-Gate Deploy (2026-05-01 19:30 TR)

## Pivot Sebebi
Loop 77 t120 catastrophic: **4 ardışık big SL** (BTC/SOL/XRP/ADA aynı 5dk barda, trend reversal), Realized -$2.25 (Loop 77), CB tripped 3.cü. **BBW=0 emit'leri sürekli loss veriyor** (zayıf trend signal). BBW skor sistemine eklemek YETMEZ — hard-gate olmalı.

## backend-dev Implementation (Quick Fix 30dk)

**KmsMomentumEvaluator.cs:**
- `Parameters.BbwHardGate bool` (default false geriye uyumluluk)
- EMA200 gate sonrası: `if (BbwHardGate && BBW < BbwThreshold) skip` (yeni hard-gate)
- Log: `"KMS skip bbw_hard_gate symbol={...} bbw={...} threshold={...}"`
- ContextJson `bbwHardGate` audit alanı

**appsettings.json:** 5 KMS seed `BbwHardGate=true` deploy.

**Tests:** 2 yeni (Test 17-18 BBW hard-gate skip + bypass).

**Build/Test:** 267/267 PASS ✓

## DB UPDATE (PM)
`UPDATE Strategies SET ParametersJson = REPLACE(..., '"BbwScorePoints":1', '"BbwHardGate":true,"BbwScorePoints":1')` — 5 KMS row.

## Boot State
| Metrik | Değer |
|---|---|
| Bot PID | 14684 |
| Port | 5000 |
| WS | Streaming ✓ |
| Warmup | 5/5 ✓ |
| **BBW Hard-Gate** | **Enabled (BBW < 0.008 → skip)** ✓ |
| EMA200 Gate | Enabled (close > ema200) |
| BE move | Enabled (Trigger 0.0010 / Offset 0.0002) |
| Trailing stop | Enabled (TrailPct 0.0015) |
| KMS params | MinScore 4, RsiCeil 70, TpMul 1.5, MaxHold 35 |

## Tam Stack (Loop 71→78)
| Loop | Feature | Etki |
|---|---|---|
| L71 | KMS skor sistemi | Base |
| L75 | BE move | Pozitif yön kar koruma |
| L76 | Trailing stop | TP momentum yakalama |
| L77 | EMA200 hard-gate | Trend yukarı zorunlu |
| L77 | BBW score | Trend strength bilgi (nice-to-have) |
| **L78** | **BBW hard-gate** | **Zayıf trend emit susturma** |

## Beklenti
- BBW < 0.008 emit'leri (Loop 77'deki 4 ardışık SL pattern'i) susturulacak
- Frekans azalır (BBW filter zayıf trend bar'ları susturuyor)
- Entry kalitesi yüksek (trending market emit'leri TP'ye ulaşır)
- Realized iyileşme: -$2.25 → ~-$1.50 (yeni TP win + büyük loss önlenir)

## Halt Eşikleri
- Realized < -$0.50 (Loop 78 specific) → param fine
- 0 emit (60dk) → BBW threshold 0.008→0.006 (daha gevşek)
- Circuit breaker → API reset (counter persistent bug devam)

## Cumulative L71-L77 Update
- L71+L72+L73+L74+L75+L76+L77 = -$4.61
- $500 başlangıç → $495.39 (-%0.92)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (20:00 TR)**

— PM 2026-05-01 Loop 78 boot (BBW hard-gate deploy)
