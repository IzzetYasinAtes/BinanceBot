# Loop 74 — Check t=30dk (2026-05-01 12:02 TR) — MinScore 5 Çok Katı, 4'e Geri

## Sonuç: 0 Emit (MinScore 5 = aşırı sıkı), Hızlı MinScore 4 Geri

binance-expert spec MinScore 5 ile **0 emit/30dk** verdi. Geri alındı (MinScore 4) — RsiCeiling 50 + TP/SL/MaxHold tune korunur.

## Sayım (~30dk)
| Metrik | Değer |
|---|---|
| **SignalEmitted** | **0** ⚠️ (MinScore 5 katı) |
| SignalSkipped | 30 |
| OrderPlaced | 0 |
| RiskAlert | 0 |
| Realized | $0 |

## Hızlı Düzeltme (Loop 74.5)

| Parametre | L74 boot | **L74.5** |
|---|---|---|
| `MinScoreThreshold` | 5 | **4** (Loop 71/73 başarılı seviye) |
| `RsiNeutralCeiling` | 50 | 50 (sıkı, korundu) |
| `TpAtrMultiplier` | 1.5 | 1.5 |
| `SlAtrMultiplier` | 0.60 | 0.60 |
| `MaxHoldMinutes` | 35 | 35 |

**Mantık**: MinScore 5 = 6/6 puanın 5'i = RSI Zone 1 + Slope + Surge + Spread + MinAtr hepsi true zorunlu. Çok nadir tetikleniyor. MinScore 4 daha pragmatik (Loop 71 ve 73 da bu seviyedeydi).

## Karar
- 0 emit → **MinScore 5→4 düzeltme yapıldı**
- Loop 74 devam, ScheduleWakeup t60 (12:32 TR)
- Sonraki bar yeni emit gelmeli

## t60 Beklenti (12:32 TR)
- 2-4 emit (MinScore 4 + RsiCeiling 50 + TP biraz geniş ile)
- TP hit oranı kritik (Loop 73'te %0 idi)
- Yine timestop pattern olursa Loop 75 backend-dev (break-even SL implement)

## Halt Eşikleri
- Realized < -$0.30 → Loop 75 backend-dev break-even SL
- CB tripped → API reset (PascalCase!) + Loop 75 algoritma
- t60 hala 0 emit → RsiCeiling 50→55

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (12:32 TR)**

— PM 2026-05-01 Loop 74 check-t30 (MinScore düzeltildi)
