# Loop 49 — Check t=120dk (2026-04-28 15:01 TR)

## Durum: 2 LONG Açıldı (BTC + XRP), Hafif Unrealized Loss

| Metrik | t60 | t120 | Δ |
|---|---|---|---|
| Cash | $500 | $299.79 | -$200.21 (2 pos kilit) |
| OpenPositionsValue | $0 | $199.86 | +$199.86 |
| Equity | $500 | $499.66 | -$0.34 |
| Realized | $0 | $0 | 0 |
| Unrealized | $0 | -$0.194 | -$0.194 |
| Net | $0 | -$0.344 | -$0.344 (komisyon dahil) |
| Komisyon | $0 | $0.150 | +$0.150 (2 entry) |
| Open Pos | 0 | **2** | **+2** ✓ |
| Signals | 0 | **2** | +2 ✓ |
| SignalSkipped | 265 | 565 | +300 |
| WsStateChanged | 46 | 51 | +5 (normal, ilk endişe yanlıştı) |

## Açık Pozisyonlar

| Coin | Side | Entry | Mark | SL | TP | Unrealized | Hold |
|---|---|---|---|---|---|---|---|
| BTC | LONG | $76,323 (varsay) | $76,223 | $76,114 (-%0.14) | $76,732 (+%0.67) | **-$0.166** | 31dk |
| XRP | LONG | $1.3826 (varsay) | $1.3813 | $1.3768 (-%0.33) | $1.3878 (+%0.47) | **-$0.028** | 1dk |

R:R BTC ≈ 4.78:1 (TP %0.67, SL %0.14 — çok iyi geometri!), XRP ≈ 1.42:1.

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | $0 | ✓ buffer $1.50 |
| 5+ ardışık SL | 0 | ✓ |
| Zombie | 31dk + 1dk (MaxHold 120dk) | ✓ |
| Signal akmıyor | 2 sinyal var | ✓ |
| WS / CB | 51 state change (normal) | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK.**

## Yorum
BB MeanRev 15m gevşetilmiş **çalışıyor** — 2h'da 2 sinyal (binance-expert beklentisi 4-5/gün ile uyumlu). Filtre dengesi iyi.

BTC R:R 4.78:1 dikkat çekici (SL çok yakın çünkü ATR küçük olmuş olmalı). TP ulaşılırsa +$0.67 net (>$0.50 fee düşüldükten sonra +$0.52). SL hit olursa -$0.14 (çok küçük zarar).

WsStateChanged 46→51 = sadece +5 (1h içinde) — başlangıç pattern'i geçti, normal seviyeye indi. Endişe yanlıştı.

## Senaryo (2 pozisyon)
- **Best (ikisi TP):** +$0.67 + $0.47 = +$1.14 - $0.30 komisyon = **+$0.84 net**
- **Worst (ikisi SL):** -$0.14 - $0.33 = -$0.47 - $0.30 komisyon = **-$0.77 net**
- **Mixed:** ~$0 ile +$0.30 arası

Her durumda halt eşiği aşılmaz.

## Karar
**Loop 49 DEVAM.** ScheduleWakeup t180 → BTC ve XRP kapanış sonucunu yakalayacak.

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=180dk (16:00 TR)**

— PM 2026-04-28 Loop 49 t=120
