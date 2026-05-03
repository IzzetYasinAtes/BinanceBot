# Loop 86 — Check t=90dk (2026-05-03 03:23 TR) — RequiredScore 3 ile +2 Emit, AMA Kötü Başlangıç (L85 Pattern Tekrarı)

## Sonuç: Hard-Gate + RequiredScore 3 Emit Verdi, Ama Pozisyonlar Hemen Aleyhe

t60→t90 (30dk threshold düşürme sonrası): **+2 yeni emit ve fill** (ADA + SOL). UPnL toplam **-$0.114** (ikisi de erken kötü). Realized -$0.171 sabit.

## Sayım (90dk cumulative)
| Metrik | t60 | **t90** | Δ |
|--------|-----|---------|---|
| SignalEmitted | 0 | **2** | **+2** ✓ |
| SignalSkipped | 60 | 83 | +23 |
| OrderFilled | 1 | 3 | +2 |
| PositionOpened | 0 | 2 | +2 |
| PositionClosed | 1 | 1 | sabit (L85 carryover) |
| Realized | -$0.171 | -$0.171 | sabit |
| Open | 0 | 2 | +2 |
| Counter | 0/4 | 0/4 | sabit |

## Açık Pozisyon (Yeni Emit'ler)
| Symbol | Hold | UPnl | %UPnl | Durum |
|--------|------|------|-------|-------|
| ADA | 4min | -$0.070 | -%0.07 | Yeni, hemen aleyhe |
| SOL | 3min | -$0.044 | -%0.04 | Yeni, kötü başlangıç |

**UPnL Toplam: -$0.114**

## Loop 85 Sahte Breakout Pattern TEKRARI Riski
Loop 85'te: yeni emit → peak=0, BE armed olmadan SL = -$0.71 ortalama. Aynı pattern başlıyor:
- ADA & SOL ikisi de hemen aleyhe (peak henüz görmedik)
- Hard-gate aktif olmasına rağmen kalitesiz emit

**Olası sebep**: Hard-gate skor toplama'ya katkı vermiyor (DefaultWeight=0). Composer hard-gate fail = skip yapıyor (Loop 86 fix), ama hard-gate PASS = sadece "izin verir", kalite garantisi vermez. Pattern detector kalitesi düşük (XRP/ADA/SOL alt-coin volatil).

## Karar
| Şart | Aksiyon |
|---|---|
| Realized -$0.171 (>-$1.50) | **Loop 86 devam, t120** |
| 2 emit ✓ | RequiredScore 3 çalıştı (Memory #12 OK) |
| Pozisyonlar kötü başlangıç | İzle (eğer recovery yoksa Loop 87 spec) |
| Counter 0/4 | OK |

## Loop 87 Spec Pre-Trigger
Eğer ADA + SOL ikisi de SL hit olursa (Counter 2'ye çıkar) ve sonraki emit'ler de aynı pattern → **Loop 87 binance-expert çağır**:
- Pattern detector kalite spec (false positive azalt)
- Yön doğrulama (1-2 bar momentum onay)
- Per-coin enable/disable (alt-coin XRP/ADA/SOL'u kapat, BTC/ETH'a odaklan?)

## L80→L86 Karşılaştırma (90dk)
| Loop | Emit | Closed | Realized | Status |
|------|------|--------|----------|--------|
| L84 | 2 | 0 | $0 | açık negatif |
| L85 | 11 | 4 | +$0.715 | ETH/BTC TP! |
| L86 | 2 | 0 (carryover) | -$0.171 | yeni emit kötü |

L86 = L85'in tekrarı: emit gelir, kötü başlar. Asıl çözüm pattern kalite, gate değil.

## t120 Beklenti (03:48 TR)
- ADA SL hit (Counter +1) veya recovery
- SOL SL hit (Counter +1) veya recovery
- Yeni emit
- Realized: -$0.171 → -$0.40 muhtemel (eğer ikisi SL)

## Halt Eşikleri
- Realized < -$1.50 → halt + Loop 87
- Counter ≥ 4 → CB tripped (auto)
- 3+ ardışık SL → spec yanlış

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=120dk (03:48 TR)** — kısa kontrol

— PM 2026-05-03 Loop 86 check-t90 (RequiredScore 3 çalıştı +2 emit, AMA L85 sahte breakout pattern tekrarı, t120 kritik)
