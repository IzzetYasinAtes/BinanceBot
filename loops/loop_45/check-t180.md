# Loop 45 — Check t=180dk (2026-04-28 06:56 TR) — SİNYAL GELDİ ✓

## Durum: Filtre Gevşetmesi İşe Yaradı — 2 LONG Açık

| Metrik | t60 | t120 | t180 | Δ (t120→t180) |
|---|---|---|---|---|
| Cash | $500 | $500 | $299.19 | -$200.81 (2 pos kilit) |
| OpenPositionsValue | $0 | $0 | $200.91 | +$200.91 |
| Equity | $500 | $500 | **$500.09** | **+$0.09** ← İLK POZİTİF |
| Realized | $0 | $0 | $0 | 0 |
| Unrealized | $0 | $0 | **+$0.244** | **+$0.244** |
| Komisyon | $0 | $0 | $0.150 | -$0.150 (2 entry) |
| Net | $0 | $0 | +$0.094 | +$0.094 |
| Open Pos | 0 | 0 | **2** | **+2** ✓ |
| Closed Pos | 0 | 0 | 0 | 0 |
| Orders | 0 | 0 | 2 | +2 |
| Signals | 0 | 0 | **2** | **+2** ✓ |
| Fills | 0 | 0 | 2 | +2 |
| SignalSkipped (toplam) | 310 | 615 | 925 | +310 |

## Açık Pozisyonlar

| Coin | Side | Entry | Mark | SL | TP | Qty | Unrealized | Komisyon | Hold |
|---|---|---|---|---|---|---|---|---|---|
| BTCUSDT | LONG | $76,773.17 | $76,833.51 | $76,472.48 (-%0.39) | $77,204.98 (+%0.56) | 0.00131 | **+$0.079** | $0.0754 | 41dk |
| XRPUSDT | LONG | $1.3901 | $1.3926 | $1.3856 (-%0.32) | $1.3954 (+%0.39) | 72.0 | **+$0.181** | $0.0751 | 26dk |

R:R BTC ≈ 1.43:1, XRP ≈ 1.21:1 (R:R düştü çünkü ATR×1.5 TP ile %0.39 cap'e takıldı).

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | $0 (closed yok) | ✓ buffer $1.50 |
| 5+ ardışık SL | 0 | ✓ |
| Zombie | 41dk + 26dk açık (MaxHold 90dk) | ✓ |
| Signal akmıyor | 2 sinyal geldi | ✓ |
| WS / CB | 4 state change normal | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK + İLK POZİTİF EQUITY (+$0.09).**

## Loop 45 Karar — DEVAM
- Karar mantığı: t180'de Signals=2>0 → Loop 45 devam, Loop 46 boot **iptal**
- Filtre gevşetme stratejisi (BBstd 1.8, RSI 35, volZ 0.8) işe yaradı — gece dilimine 1h kala 2 sinyal
- Beklenti: 2 pozisyon t270-t300 arası kapanacak (TP/SL/TimeStop)

## Senaryo Hesabı (2 pozisyon)
**Best (her ikisi TP):** +$0.56 + $0.39 ≈ +$0.95 net (komisyon dahil **+$0.65 cumulative**)
**Worst (her ikisi SL):** -$0.39 - $0.32 ≈ -$0.71 net + komisyon = **-$1.00 cumulative**
**Mixed (1 TP, 1 SL):** ~ -$0.05 ile +$0.10 arası

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=240dk (07:56 TR)**

t240'da:
- BTC ve XRP muhtemelen kapanmış olacak (66dk + 86dk hold)
- Yeni sinyal geldi mi kontrol
- Halt: realized<-$1.50 olursa Loop 46 (DİKKAT: 5 SL etkisi yapmadan -$1.50 olamaz, çünkü 2 SL ~-$0.71 + komisyon)

— PM 2026-04-28 Loop 45 t=180
