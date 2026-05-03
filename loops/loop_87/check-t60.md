# Loop 87 — Check t=60dk (2026-05-03 05:14 TR) — 1h 0 Emit, RsiMaxEmit 75→85 (Memory #12)

## Sonuç: 1h 0 Yeni Emit → ANINDA RSI Cap Gevşetme

t30→t60: 0 yeni emit (toplam 0/60dk), 0 yeni close. Memory Golden Rule #12 ihlal — pivot.

## Sayım (60dk)
| Metrik | Değer |
|--------|-------|
| SignalEmitted | **0** (1h boyunca!) |
| SignalSkipped | 60 (+25 son 30dk) |
| OrderFilled | 1 (SOL L86 carryover) |
| PositionClosed | 1 |
| Realized | -$0.7024 sabit |
| Open | 0 |
| Counter | 1/4 |
| CB | Healthy |

## Aksiyon: RsiMaxEmit 75 → 85
**Hipotez**: Pazar momentum dönüşünden sonra RSI 75-85 bölgesi yaygın. RSI 75 cap çok katı, gerçek breakout'ları da eliyor.

**DB UPDATE**: 5 strateji ParametersJson `RsiMaxEmit`: 75 → **85**.

**Bot restart**: PID 16280 → kill → restart **PID 17564**.

### Diğer Paramlar Sabit
- MTF gate aktif (15m EMA21 slope > 0)
- Hard-gate aktif (volume_surge + spread_guard)
- RequiredScore 3 (Loop 86)
- BE.OffsetPct 0.0020, Trail.TrailPct 0.0050

## Karar
| Şart | Aksiyon |
|---|---|
| 1h 0 emit | **RsiMaxEmit 75→85** ✓ |
| Realized -$0.7024 (>-$1.50) | Devam |
| Bot restart | PID 17564 ✓ |
| Memory #12 | Uygulandı |

## L83/L86/L87 RsiMaxEmit Karşılaştırma
- L83: RSI cap yok → 0 emit (hard-gate sebep)
- L86: RSI cap yok + RequiredScore 3 → 2 emit (hard-gate aktif yine)
- **L87: RSI cap 75 + MTF + RequiredScore 3 → 0 emit** (3 filter çakışması)
- **L87 (yeni)**: RSI cap **85** + MTF + RequiredScore 3 → ?

Eğer hâlâ 0 emit → MTF ana sorun (15m EMA21 slope), o zaman MTF gevşet (≤ 0 → < -0.001 threshold).

## t90 Beklenti (05:38 TR)
- 1+ yeni emit RsiMaxEmit 85 ile
- Eğer hâlâ 0 → MTF gate gevşet veya kapat
- Realized: sabit (yeni emit yok ise)

## Halt Eşikleri
- Realized < -$1.50 → halt + Loop 88
- 0 emit 90dk yine → MTF kapat
- Sahte breakout pattern devam → spec yanlış

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=90dk (05:38 TR)** — RsiMaxEmit 85 ile emit testi

— PM 2026-05-03 Loop 87 check-t60 (1h 0 emit Golden #12, RsiMaxEmit 75→85, bot restart PID 17564)
