# Loop 41 — Check t=90dk (2026-04-24 12:00 TR)

## 🎯 İLK SİNYAL GELDİ

**BNBUSDT LONG @ 08:30 UTC (11:30 TR), açıldı 32dk önce**

| Metrik | Değer |
|---|---|
| Symbol | BNBUSDT |
| Side | LONG |
| Quantity | 0.1570 BNB |
| Entry Price | $637.6338 |
| Notional | $100.1085 |
| Mark Price (now) | $636.9050 |
| Unrealized PnL | -$0.0846 |
| Komisyon ödenen | $0.0751 |
| Net etki | -$0.1597 |
| Süre | 32dk 2sn / MaxHold 90dk |
| Kalan MaxHold | ~58dk |
| Status | OPEN / AKTIF |

## DB Sayım
| Metrik | t90 | Δ vs t60 |
|---|---|---|
| Cash | $399.8164 | -$100.18 (BNB pos için kilit) |
| Equity | $499.7822 | -$0.22 (komisyon + unrealized) |
| netPnl | -$0.2178 | -$0.22 |
| Pos Open | 1 | +1 ✓ |
| Pos Closed | 0 | 0 |
| Order Total | 1 | +1 |
| Signals | 17 | +17 |
| Fills | 1 | +1 |
| EvtErr (35dk) | 0 | 0 |
| EvtSkip (35dk) | 329 | +36 |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | 0 (henüz close yok) | ✓ |
| 5+ ardışık SL | 0 | ✓ |
| Zombie >270dk | 32dk | ✓ |
| WS disconnect | Streaming, drift -412ms | ✓ |
| CB Tripped | HEALTHY | ✓ |
| Console error UI | 0/2 sayfa | ✓ |

**HALT YOK — loop devam.**

## Playwright Smoke (2 sayfa, 1920×1080)
- ui-t90-01-positions.png — BNBUSDT LONG satırı tam görünür: 0.1570 / 637.6338 / $100.1085 / -$0.1144 / 32dk 2sn / AKTIF
- ui-t90-02-dashboard.png — Hero: Kapalı $0 / Açık -$0.1427 / Toplam -$0.2178. Cash $399.82 / Equity $499.78. Komisyon $0.0751.

**UI gözlem (backlog notu):**
- Pozisyonlar tablosunda **TP ve SL kolonları "—" gösteriyor** — backend monitor TP/SL takip ediyor olmalı (Loop 38'de doğrulanmış) ama UI bu değerleri Position entity'den okuyamıyor (büyük olasılıkla Order ContextJson'da metadata olarak duruyor, Position'a serialize edilmiyor). **Loop 42 backlog:** Position entity'ye TP/SL alan ekle veya UI Position.OrderId ile join çekip ContextJson'dan parse etsin.

## Sinyal vs Fill Oranı
- 17 StrategySignal kaydı, 1 Fill = ~%6 fill rate
- Olası neden: sinyaller geliyor ama RiskGuard / max position / cooldown / dispatcher veto'larıyla eleniyor. Detaylı analiz için Signal.Status alanı incelenmeli (Loop 41 sonu raporda).
- Bu **iyi haber**: Donchian filtresi sıkı ama sinyal üretiyor. Sadece RiskGuard tarafında bir şey kısıtlıyor (büyük ihtimalle 1 açık pozisyon → diğer sembollerde sinyal gelse bile blok).

## Sıradaki Wakeup
**ScheduleWakeup 9000 → t=240dk (4h, 14:30 TR)**

Beklentiler:
- BNBUSDT pozisyonu 12:30-13:00 TR civarında ya TP / SL / TimeStop ile kapanır
- t240'a kadar (Avrupa pik 12-15 TR) muhtemelen 3-6 ek trade
- Realized PnL ilk değerini alacak

— PM 2026-04-24 t=90
