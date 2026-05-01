# Loop 76 — Check t=30dk (2026-05-01 16:09 TR) — MinScore 5 Yine Katı, 4'e Geri

## Sonuç: 0 Emit (binance-expert MinScore 5 önerisi testnet'te işe yaramadı), Hızlı Geri

binance-expert "MinScore 4→5 entry kalitesi" önerisi Loop 76 t30: **0 emit / 30 skip** (Loop 74 patterni tekrar). Hızlı düzeltme: MinScore 5→4. Trailing stop deploy korunur.

## Sayım (~30dk)
| Metrik | Değer |
|---|---|
| **SignalEmitted** | **0** ⚠️ |
| SignalSkipped | 30 |
| OrderPlaced | 0 |
| RiskAlert | 0 |
| Realized | $0 |

## Hızlı Düzeltme (Loop 76.5)
| Parametre | L76 boot | **L76.5** |
|---|---|---|
| `MinScoreThreshold` | 5 | **4** (Loop 75'le aynı) |
| `RsiNeutralCeiling` | 60 | 60 (sabit) |
| `TpAtrMultiplier` | 1.5 | 1.5 |
| `SlAtrMultiplier` | 0.60 | 0.60 |
| `MaxHoldMinutes` | 35 | 35 |
| BE module | Enabled (Trigger 0.0010, Offset 0.0002) | KORUNDU |
| **Trailing module** | **Enabled (TrailPct 0.0015)** | **KORUNDU ✓** |

→ MinScore 4 ile Loop 75 emit-friendly seviye. Trailing stop yeni özellik (BE sonrası TP momentum koruyacak).

## binance-expert Spec Hatası
binance-expert "MinScore 4→5 = entry kalitesi artar" önerdi. Pratikte testnet'te 6 puanın 5'ini puanlamak çok katı (Loop 74'te de aynı sorun). **Loop 76'nın asıl değeri**: Trailing stop. MinScore 5 öneri testnet doğrulaması olmadan verilmiş.

## Karar
| Şart | Aksiyon |
|---|---|
| 0 emit + MinScore 5 katı | **MinScore 5→4 düzeltildi ✓** |
| Trailing module aktif | ✓ Loop 75 BE + Loop 76 trail combo |
| Bot devam | t60 wakeup |

## t60 Beklenti (16:39 TR)
- MinScore 4 ile yeni emit gelecek (Loop 75 = 30 emit/5h frekans)
- Pozisyonlar BE applied → trailing aktif → TP momentum'unu yakalar
- Trailing exit log "TRAILING-EXIT" görmeli

## Halt Eşikleri
- Realized < -$0.50 → Loop 77 EMA200+BBW (entry kalitesi)
- 5+ ardışık SL → CB reset
- t60 hala 0 emit → RsiCeiling 60→55 düzeltme

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (16:39 TR)**

— PM 2026-05-01 Loop 76 check-t30 (MinScore 4 geri)
