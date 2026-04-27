# Loop 43 — Stale Close (2026-04-27 oturumu)

## Durum
- **Bot offline 2 gün** (24.04 → 27.04). DOGEUSDT pozisyonu DB'de Status=1 (Open) olarak kalmış, ama mark price stale.
- Son SystemEvent: 24.04.2026 21:25 UTC (DOGE sinyal anı yakını).
- API HTTP 000 (kapalı), dotnet.exe idle (73KB).

## Loop 43 Final Toplam (DB sayım)

| Metrik | Değer |
|---|---|
| Açılan Pozisyon | 2 (ADA SL kapalı, DOGE açık-stale) |
| Realized PnL | -$0.4473 (ADA SL) |
| DOGE unrealized (stale) | +$0.046 (mark eskimiş, gerçek değer bilinmez) |
| Toplam Sinyal | 2 (ADA + DOGE) |
| SignalSkipped | 3440 (evaluator_skip generic) |
| Komisyon | $0.225 |
| Net | -$0.3958 |

## Root Cause — Bot Offline
- Loop 43 ScheduleWakeup zinciri 25.04 01:18 TR (t450) sonrası kesildi (ya kullanıcı escape, ya wakeup hatası).
- API restart edilmedi 2 gün boyunca → DOGE position TP/SL/TimeStop tetiklenemedi, gerçek sonucu bilmiyoruz.
- **Bu bir strateji halt'ı değil — pasif zombie state.**

## Loop 41-42-43 Aggregate (3 loop birleşik)
| Loop | Trade | TP | SL | Realized | Sebep |
|---|---|---|---|---|---|
| 41 | 8 | 0 | 8 | -$1.7985 | LTC whipsaw, cooldown yok |
| 42 | 2 | 0 | 2 | -$0.7262 | XRP+SOL eş-SL, stagnation |
| 43 | 1 (kapalı) | 0 | 1 | -$0.4473 | ADA SL, DOGE stale |
| **Total** | **11** | **0** | **11** | **-$2.97** | **%0 WR** |

DOGE bilinmediği için en iyi/kötü senaryo:
- Best (TP hit): -$2.97 + $1.05 = **-$1.92 net Loop 41-43**
- Worst (SL hit): -$2.97 - $0.50 = **-$3.47 net**

## Karar — Loop 43 Stale-Close + Loop 44 Pivot
1. DOGE pozisyonunu DB'de manuel kapatmıyorum (Loop 44 boot DB reset edecek).
2. Loop 43 raporları (boot, t30, t60, t90, t150, t210, t270, t330, t390, halt-final-stale) loops/loop_43/ altında.
3. binance-expert'e root cause + Loop 44 strateji önerisi delege edildi (paralel).
4. Loop 44 binance-expert raporunu alır almaz boot.

— PM 2026-04-27 (oturum yeniden başlatma)
