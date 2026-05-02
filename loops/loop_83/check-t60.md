# Loop 83 — Check t=60dk (2026-05-02 18:16 TR) — 0 Emit 1h → RequiredScore 5→4 (Memory Golden #12)

## Sonuç: Pattern Composer Threshold Düşürüldü, Bot Restart

t30→t60: 0 yeni emit (toplam 0/60dk), Realized $0 sabit. Memory Golden Rule #12: "0 emit > 1h → ANINDA filtre gevşet". RequiredScore **5→4** düşürüldü, bot restart.

## Sayım (60dk t30→t60 değişim)
| Metrik | t30 | **t60** | Δ |
|--------|-----|---------|---|
| SignalEmitted | 0 | **0** | sabit |
| SignalSkipped | 35 | 70 | +35 |
| OrderFilled | 0 | 0 | sabit |
| PositionClosed | 0 | 0 | sabit |
| Realized | $0 | $0 | sabit |
| Open | 0 | 0 | sabit |
| Counter | 0/4 | 0/4 | sabit |

## Aksiyon: Composer Threshold Gevşetme
**Sebep**: Golden Rule #12 — 0 emit 1h ihlali. 5 coin × pattern detector × 12 bar/h = ~60 evaluation, hiçbiri threshold ≥5 sağlamadı (skor tavanı 24, %20 gerek).

**Değişim**:
- ParametersJson: `RequiredScore` **5 → 4** (5 strateji DB UPDATE)
- Bot kill (PID 8628) + restart (PID **13664**)
- Diğer parametreler sabit (BE Offset 0.002, Trail 0.0050)

**Beklenti**: %20 → %16.7 threshold → daha fazla pattern stack threshold geçer.

## Karar
| Şart | Aksiyon |
|---|---|
| 0 emit 1h | **Filtre gevşetildi (5→4)** |
| Realized $0 | OK |
| Bot restart | PID 13664 ✓ |
| Memory Golden #12 | Uygulandı |

## L80/L81/L82/L83 Karşılaştırma (60dk)
| Metrik | L80 | L81 | L82 | **L83** |
|--------|-----|-----|-----|---------|
| Emit | 6 | 2 | 1 | **0** (gevşetildi 5→4) |
| Realized | -$0.45 | $0 | $0 | **$0** |

L83 sermaye stable + filtre düşürme = sürekli işlem prensibi tekrar.

## t90 Beklenti (18:43 TR)
- 1-3 yeni emit (yeni threshold 4 ile)
- BE-stop +$0.10 net pozitif test (Loop 83 spec asıl test)
- Realized: $0 sabit veya küçük kar

## Halt Eşikleri
- Realized < -$1.50 → halt + Loop 84
- 3 ardışık küçük loss yine → spec yanlış
- 0 emit 90dk daha → threshold 4→3 değil, pivot gerek

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=90dk (18:46 TR)**

— PM 2026-05-02 Loop 83 check-t60 (RequiredScore 5→4, Golden Rule #12, bot restart PID 13664)
