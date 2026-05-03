# Loop 91 — Check t=60dk (2026-05-03 09:48 TR) — +5 Yeni Emit (Pazar Dönüşü), ADA Recovery

## Sonuç: MTF Gate Pazar Dönüşünü Algıladı, +5 Yeni Emit, ADA Recovery +$0.08

t30→t60 (30dk): **+5 yeni emit** (MTF gate slope dönüşmüş, downtrend kırılıyor). MaxOpen=3 dolu → fill yok. ADA dramatik recovery (-$0.150 → -$0.070).

## Sayım (60dk)
| Metrik | Değer |
|--------|-------|
| **SignalEmitted** | **5** ✓ (pazar dönüşü) |
| SignalSkipped | 54 |
| OrderFilled | 0 (MaxOpen dolu) |
| PositionClosed | 0 |
| Realized | $0 |
| Open | 3 (carryover) |
| Counter | 0/4 |

## Carryover Hareketi
| Symbol | Hold | UPnl t30 | UPnl t60 | Δ |
|--------|------|----------|----------|---|
| SOL | 97min | -$0.222 | -$0.198 | +$0.024 |
| BTC | 97min | -$0.175 | -$0.176 | sabit |
| **ADA** | 58min | -$0.150 | **-$0.070** | **+$0.080** ✓ |

UPnL toplam: -$0.547 → **-$0.444** (+$0.103 iyileşme).

## Pazar Dönüşü İşareti
- 5 yeni emit MTF gate geçtiyse → 15m EMA21 slope artık negatif değil (≥ -%0.1)
- Pazar yön değişiyor olabilir (downtrend kırılıyor)
- Carryover'lar da iyileşmeye başladı (özellikle ADA)

## Karar
| Şart | Aksiyon |
|---|---|
| Realized $0 (>-$2.00) | **Loop 91 devam, t90** |
| 5 yeni emit | MTF doğru çalışıyor (downtrend kırılınca emit verdi) |
| ADA recovery | İyi sinyal, BE eşiğine yaklaşırsa pozitif close |
| MaxOpen dolu | Yeni emit fill için close gerek |

## t90 Beklenti (10:13 TR)
- Carryover'lar BE-stop pozitif veya SL hit
- ADA BE eşiğe yaklaşırsa +$0.10+ pozitif close
- Yeni emit fill (eğer 1 close olursa)

## Halt Eşikleri
- Realized < -$2.00 → halt + Loop 92
- 4+ ardışık SL → spec yanlış

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=90dk (10:13 TR)** — kısa kontrol

— PM 2026-05-03 Loop 91 check-t60 (+5 emit pazar dönüşü, ADA recovery, BE-stop fırsat)
