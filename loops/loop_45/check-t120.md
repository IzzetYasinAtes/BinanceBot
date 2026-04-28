# Loop 45 — Check t=120dk (2026-04-28 05:54 TR)

## Durum: Gevşetilmiş Filtre 2h, Yine 0 Sinyal

| Metrik | Boot | t60 | t120 | Δ (t60→t120) |
|---|---|---|---|---|
| Cash / Equity | $500 | $500 | $500 | 0 |
| Realized | $0 | $0 | $0 | 0 |
| Open / Closed Pos | 0/0 | 0/0 | 0/0 | 0 |
| Orders | 0 | 0 | 0 | 0 |
| Signals | 0 | 0 | 0 | 0 |
| Fills | 0 | 0 | 0 | 0 |
| SignalSkipped (toplam) | 0 | 310 | 615 | +305 |
| SignalSkipped (60dk son) | — | 300 | 300 | tutarlı eval |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | $0 | ✓ buffer $1.50 |
| 5+ ardışık SL | 0 | ✓ |
| Zombie | 0 açık | ✓ |
| Signal akmıyor (>4h) | 2h, henüz t240 değil | ⏳ |
| WS / CB | 4 state change normal | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK** ama loop atıl — gevşetilmiş filtre etkisi sıfır.

## Yorum
3 filtre birden gevşetildi (BBstd 2.0→1.8, RSI 30→35, volZ 1.0→0.8), 2 saat geçti, hala 0 sinyal. Bu güçlü bir sinyal:
- BTC/ETH/XRP/SOL/ADA blue-chip 5 coin kombinasyonu **bu rejimde mean reversion için dead zone**
- Asia-Pasifik gece UTC 00-04 = TR 03-07 boyunca 5 coin'in hiçbiri BB lower'a inmemiş + RSI<35 + volZ>0.8 koşulunu birlikte sağlamamış

**Loop 46 kesinleşti** (t240 = 07:51 TR'de tetiklenir).

## Loop 46 Plan — Coin Genişletme + Sıkı Filtre Geri (Hipotez 1)

binance-expert tercihi 1: 5 → 10 coin genişletme. Mid-cap'ler (DOGE, AVAX, LINK, DOT, TRX) blue-chip'lerden daha volatil → oversold koşul daha sık tetiklenir.

**Loop 46 değişiklikler:**
- Yeni 5 BB strategy ekle: DOGE, AVAX, LINK, DOT, TRX (Activate=true)
- Mevcut 5 BB strategy filtre **eski sıkı haline geri** (BBstd 2.0, RSI 30, volZ 1.0) — kaliteyi koru, sadece coin genişlet
- Toplam 10 BB Mean Rev15m strategy aktif

**Beklenti:** Coin sayısı 2x → sinyal sıklığı ~2x. 4h'da 1-2 sinyal hedef.

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=180dk (06:54 TR)**

t240'da kesin Loop 46 boot:
1. appsettings.json patch (5 yeni BB strategy + mevcut 5 sıkı filtre geri)
2. dotnet kill + DB reset + reseed
3. API restart
4. Loop 46 boot rapor
5. ScheduleWakeup t60

— PM 2026-04-28 Loop 45 t=120
