# Loop 43 — Check t=90dk (2026-04-24 19:03 TR)

## 🎯 İLK SİNYAL — ADAUSDT LONG

| Metrik | Değer |
|---|---|
| Symbol | ADAUSDT LONG |
| Entry @ 16:00 UTC (19:00 TR) | $0.2522252 (qty 396.5, $100.0073) |
| StopPrice (SL) | $0.2515686 (-%0.26 entry'den) |
| TakeProfit | $0.2541429 (+%0.76 entry'den) |
| R:R tasarımı | **~2.92** (AR-GE 2.67 hedefe yakın) |
| Mark @ t90 | $0.2518500 (henüz SL/TP'den uzak) |
| Hold | 4dk 12sn / MaxHold 90dk |
| Unrealized | -$0.1488 |
| Komisyon | $0.0750 (entry) |
| Net etki | -$0.2238 |
| Status | OPEN / AKTIF |

## DB Sayım
| Metrik | t30 | t90 | Δ |
|---|---|---|---|
| Cash | $500.0000 | $399.9177 | -$100.08 (ADA pos kilit) |
| Equity | $500.0000 | $499.7762 | -$0.22 |
| netPnl | $0.0000 | -$0.2238 | -$0.22 |
| Pos Open | 0 | 1 | +1 ✓ |
| Order Total | 0 | 1 | +1 |
| Signals | 0 | 1 | +1 ✓ |
| Fills | 0 | 1 | +1 |
| EvtSkip (60dk) | 250 | 486 | normal |
| EvtErr | 0 | 0 | 0 |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | 0 | ✓ buffer dolu |
| 5+ ardışık SL | 0 | ✓ |
| Zombie | 4dk açık | ✓ |
| WS / CB | Streaming, drift -518ms, HEALTHY | ✓ |

**HALT YOK.**

## Filtre Gevşetme Sonucu — DOĞRULANDI
Loop 42 (MinAtrPct 0.0010) → Loop 43 (MinAtrPct 0.0007):
- Loop 42 t30-t150 (2 saat 11dk): **0 yeni trade** post ilk 2 SL
- Loop 43 t30-t90 (60dk): **1 yeni trade** (ADA)

Filtre gevşetmesi sinyal frekansını yükseltti. ADA matematiksel R:R 2.92 (AR-GE hedefine en yakın oranımız bugüne kadar — Loop 41 BNB 2.34, Loop 42 SOL 2.0).

## Piyasa Rejim Gözlem
- Önceki check (t30): tüm hero kart kırmızı (-%0.01..-%0.57)
- Şimdi (t90): mixed (BTC -%0.25, ETH -%0.92, ama ADA +%0.84 son 1h üst kırılım yaptı)
- ADA dağılan piyasada üst kırılım yakaladı = **strateji doğru çalışıyor** (selectivity)

## Playwright Smoke (1 sayfa — pozisyon detay)
- ui-t90-01-positions-open.png — ADAUSDT LONG 396.5, $0.2522 → mark $0.2520, -$0.1091 (-%0.11), 4dk 12sn AKTIF
- Console error 0
- TP/SL kolonu hala "—" (UI backlog Loop 42'den hatırlatma)

## Sıradaki Wakeup
**ScheduleWakeup 3600 → t=150dk (20:03 TR)**

Beklenti:
- ADA pozisyon: 60dk içinde TP / SL / TimeStop ile kapanacak
- Yeni sinyaller gelmeye başlayabilir (filtre gevşetildi)
- t150'de ilk realized PnL alınmış olur

— PM 2026-04-24 Loop 43 t=90
