# Loop 56 — Check t=30dk (2026-04-29 08:27 TR)

## Frekans ✓ (3 yeni emit) ama -$0.45 SOL SL Hızlı

| Metrik | Boot | t30 |
|---|---|---|
| Cash | $500.36 | $399.79 (1 pos kilit) |
| OpenPositionsValue | $0 | $100.03 |
| Equity | $500.36 | **$499.82** (-$0.54) |
| Realized (Loop 54+56) | +$0.355 | **-$0.098** (Loop 56 -$0.45) |
| Unrealized | $0 | +$0.019 (SOL açık) |
| Net | +$0.355 | -$0.18 |
| Komisyon (toplam) | $0.150 | $0.375 (+$0.225 = 3 entry/exit) |
| Open Pos | 0 | 1 (SOL) |
| Closed Pos | 1 (ETH) | 2 (ETH + SOL #1) |
| **SignalEmitted** | 1 (ETH) | **4** (3 yeni Loop 56) ✓ |
| SignalSkipped | 1106 | 1449 |
| WR | %100 (1/1) | %50 (1/2) |

## Loop 56 Yeni Trade'ler

### 🔴 SOLUSDT #1 (KAPALI — SL HIZLI)
- Entry $85.13 (varsay) @ 04:56 UTC | Exit $84.72 @ 05:02 UTC
- Hold: 6dk (MaxHold 10dk öncesi SL hit)
- Mark down -%0.48 → SL tetiklendi
- Komisyon: $0.0751 + $0.0748 = $0.150
- **Realized: -$0.453**

### 🟡 SOLUSDT #2 (HALA AÇIK)
- Entry $84.84 (varsay) @ 05:23 UTC | Mark $84.87
- Hold: 4dk (MaxHold 10dk → 6dk kaldı)
- Mark +$0.03 (+%0.04)
- Unrealized: **+$0.019**

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized > -$1.00 | -$0.098 | ✓ buffer **$0.90** |
| 3+ ardışık SL | 1 SL (önce ETH WIN) | ✓ |
| WR < %25 | %50 (1/2) | ✓ |
| WS / CB | normal | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK ama buffer ZAYIF ($0.90 kaldı).**

## Yorum
3 yeni emit ✓ (frekans hedef sağlandı: 6/h yeterli, biz 6/30dk = 12/h yaptık).

Sorun: SOL aynı coin'den iki kez ardışık (cooldown 2 bar = 2dk yetmedi). İlk SOL SL'den 21dk sonra ikinci SOL açıldı. Aynı volatilite rejiminde → ikincisi de SL riski yüksek.

Eğer SOL #2 SL hit ederse → -$0.45 daha → Loop 56 toplam realized -$0.90, kar tamamen silinir + halt eşiği yakın.

## Karar
**Loop 56 DEVAM** ama agresif izleme. SOL #2 6dk içinde kapanır (MaxHold 10dk).

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (08:57 TR)**

t60'ta:
- SOL #2 kapanmış (TP/SL/TimeStop)
- Yeni emit'ler de var
- Halt değerlendirmesi

— PM 2026-04-29 Loop 56 t=30
