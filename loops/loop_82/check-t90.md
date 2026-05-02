# Loop 82 — Check t=90dk (2026-05-02 16:35 TR) — Yeni Param Hâlâ Küçük Loss (Peak %0.23-0.25 < %0.27 Breakeven)

## Sonuç: Trailing Buffer 0.0025 Hâlâ Yetmedi — Peak'ler %0.27 Eşiği Aşamıyor

t60→t90 (30dk): **+2 close (ETH+BTC carryover)**, Realized **$0 → -$0.13**. +1 yeni emit (ADA, hâlâ açık -$0.07). Counter=2/4.

## Sayım (90dk)
| Metrik | t60 | **t90** | Δ |
|--------|-----|---------|---|
| SignalEmitted | 1 | **2** | +1 (ADA) |
| OrderFilled | 0 | 3 | +3 (2 exit + 1 new) |
| **PositionClosed** | 0 | **2** | **+2 (ETH+BTC)** |
| **Realized PnL** | $0 | **-$0.1298** | **-$0.13** |
| Open | 2 | 1 (ADA) | -1 |
| Counter | 0 | **2** | +2 |

## Yeni Param Test Sonuçları (2 Close)
| Symbol | Hold | Peak | Exit Tipi | PnL |
|--------|------|------|-----------|-----|
| ETH | 195min | **+%0.25** | trailing-exit | **-$0.069** |
| BTC | 118min | **+%0.23** | trailing-exit | **-$0.060** |

**Ortalama**: -$0.064/trade, peak ortalama +%0.24.

### Kritik Sayı: Trailing Breakeven (Yeni Param)
- TrailPct=0.0025 + slippage 0.0002 = **%0.27 minimum peak gerek**
- ETH peak %0.25 → eşiğin **0.02 altında** → küçük loss
- BTC peak %0.23 → eşiğin **0.04 altında** → küçük loss
- Loop 81 SOL %0.33 (eşik üstü), L82'de hiçbiri %0.27 aşamadı

→ Bu loop'ta peak'ler küçük (volatilite az). Trailing buffer hâlâ dar.

## Açık ADA
| Hold | UPnl | %UPnl | Risk |
|------|------|-------|------|
| 21min | -$0.070 | -%0.07 | SL'e mesafe %0.33 (MaxSLPct 0.4%) |

ADA SL hit olursa: Realized -$0.13 → -$0.45 + Counter=3.

## Karar
| Şart | Aksiyon |
|---|---|
| Realized -$0.13 (>-$1.50) | **Loop 82 devam, t120** |
| 2 ardışık küçük loss | İzleme (4'te CB tripped) |
| Peak %0.23-0.25 < %0.27 eşik | Loop 83 backlog: trailing buffer 0.0025→0.0040 veya R:R 1:2→1:1.5 |
| ADA -$0.07 erken | İzle |
| Counter=2 | 2 daha SL = halt |

## L80/L81/L82 Karşılaştırma (90dk)
| Metrik | L80 t90 | L81 t90 | **L82 t90** |
|--------|---------|---------|-------------|
| Emit | 7 | 3 | 2 |
| Closed | 3 | 0 | 2 |
| Realized | -$0.45 | $0 | **-$0.13** |
| Avg/Trade | -$0.15 | n/a | **-$0.065** ✓ (L80'den 2x iyi) |

L82 yeni param **avg loss küçülttü** (-$0.10 → -$0.065) ama hâlâ pozitif değil.

## t120 Beklenti (17:05 TR)
- ADA outcome (SL hit veya recovery)
- Yeni emit (1 slot boş, 1 dolu)
- Realized: -$0.13 sabit veya -$0.45 (ADA SL)
- Counter: 2 → 3 muhtemel

## Loop 83 Spec Pre-Trigger
Eğer t120-t150'de pattern **devam** (peak %0.20-0.25 + trailing-exit küçük loss):
- Trailing buffer 0.0025 → **0.0040** veya
- R:R 1:2 → **1:1.5** (TP daha erken vurulur, peak gerekmez) veya
- TP otomatik move (trailing yerine fixed TP %0.30)

## Halt Eşikleri
- Realized < -$1.50 → halt + Loop 83
- Counter ≥ 4 → CB tripped (auto, halt zorunlu)
- ADA SL hit + Counter=3 → halt değil ama yakın

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=120dk (17:00 TR)**

— PM 2026-05-02 Loop 82 check-t90 (yeni param trailing buffer hâlâ dar, peak %0.24 ort < %0.27 eşik, Loop 83 yakın)
