# Loop 41 — Check t=150dk (2026-04-24 13:06 TR)

> Not: Orijinal plan t240 idi. ScheduleWakeup runtime [60,3600] aralığına clamp ediyor (max 1h). t90 → t150 → t210 → t240 → ... zinciri ile devam.

## 🎯 İlk Trade Sonucu — SL HIT

| Metrik | Değer |
|---|---|
| Symbol | BNBUSDT LONG |
| Entry @ 08:30 UTC | $637.6338 (qty 0.157, $100.1085) |
| StopPrice (SL) | $636.2949 (-%0.21 entry'den) |
| TakeProfit | $640.7579 (+%0.49 entry'den) |
| R:R tasarımı | ~2.34:1 (AR-GE 2.67:1 hedefe yakın) |
| Exit @ 09:04 UTC | $636.0564 (SL'den $0.24 aşağı slippage) |
| Hold süresi | 34dk 36sn / MaxHold 90dk (erken kapanış SL ile) |
| Mark loss | $0.2476 |
| Komisyon (entry+exit) | $0.0751 + $0.0749 = $0.1500 |
| **Realized PnL** | **-$0.3976 (-%0.40)** |
| ClosedReason | SL HIT (Exit < StopPrice doğrulandı) |

## DB Sayım
| Metrik | t150 | Δ vs t90 |
|---|---|---|
| Cash | $499.6024 | +$99.79 (BNB pos kapandı, $100 - loss döndü) |
| Equity | $499.6024 | -$0.18 (toplam) |
| netPnl | -$0.3976 | -$0.18 |
| Pos Open | 0 | -1 ✓ kapandı |
| Pos Closed | 1 | +1 |
| Order Total | 2 | +1 (kapanış order) |
| Signals | 17 | 0 (yeni signal yok 60dk) |
| Fills | 2 | +1 |
| EvtErr (90dk) | 0 | 0 |
| EvtSkip (60dk) | 524 | +195 |
| Komisyon toplamı | $0.1500 | +$0.075 |

## Per-Coin Trade Kırılımı
| Coin | Trade | TP | SL | TimeStop | Realized | Avg Hold |
|---|---|---|---|---|---|---|
| BNBUSDT | 1 | 0 | 1 | 0 | -$0.3976 | 34m36s |

**Win Rate: 0% (1L)** — istatistiksel anlamı yok (1 trade), AR-GE %35-45 WR beklentisi 50+ trade'de ortaya çıkar.

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | -$0.40 | ✓ (1.10 buffer var) |
| 5+ ardışık SL | 1/5 | ✓ |
| Zombie >270dk | 0 açık | ✓ |
| WS disconnect | Streaming, drift -423ms | ✓ |
| CB Tripped | HEALTHY | ✓ |
| Console error UI | 0/3 sayfa | ✓ |

**HALT YOK — loop devam.**

## Playwright Smoke (3 sayfa, 1920×1080)
- ui-t150-01-positions-open.png — Açık 0, Kapalı (1) tab var
- ui-t150-02-positions-closed.png — BNB satırı tam: 637.6338→636.0564, -$0.3976, 34dk 36sn, 24 Nis 12:04 TR
- ui-t150-03-dashboard.png — Hero -$0.3976, Komisyon $0.1500, Kartopu -%0.08, Canlı İşlem Akışı'nda BNB satırı

**UI Backlog (Loop 42 candidate):**
- Positions Kapalı tablosunda **KOMİSYON kolonu $0.0000** gösteriyor (DB Position.EntryCommission+ExitCommission = $0.150 var, DTO eksik). Dashboard hero'sunda doğru hesaplanıyor.
- Positions Açık tablosunda **TP/SL kolonu "—"** (Position.StopPrice/TakeProfit alanları DB'de var, DTO/UI okumuyor)

## Gözlem
- **Piyasa rejim değişimi:** t60'ta hero pozitif (+%0.03..+%0.38), t150'de hepsi negatif (-%0.14..-%0.32). Bu doğrultu değişimi tam BNB pozisyonu açıkken oldu → **klasik false breakout**. Donchian üst kırılım sinyali geldi, ama momentum sürdürülmedi.
- 60dk yeni sinyal yok (BNB kapandıktan sonra). Avrupa pik dilimi içindeyiz, normal beklentide saatte 4-7 sinyal vardı. Hala olası — pencere geniş.
- Skip event devam ediyor (524 son 60dk = ~9/dk = doğru ritim 12 coin × 1 değerlendirme/m).

## Sıradaki Wakeup
**ScheduleWakeup 3600 → t=210dk (14:06 TR)**

Beklentiler:
- Yeni Donchian breakout sinyalleri Avrupa pik dilimi devam ediyor
- t210'a kadar 1-3 yeni trade beklenir
- Realized hala -$0.40 civarında olmalı (yeni SL hit olursa -$0.80'e iner — buffer hala var $0.70)

— PM 2026-04-24 t=150
