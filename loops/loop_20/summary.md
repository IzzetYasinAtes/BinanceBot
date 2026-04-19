# Loop 20 Özet — HALT t150 (Piyasa-Hedef Matematiksel Uyumsuzluk)

## Süre: 150 dk (4h tam cycle'a ulaşmadan halt)

## Performans
- Equity: $100.00 → $100.00 (değişmez)
- 0 sinyal, 0 order, 0 trade
- WS Streaming 150 dk stabil, 0 exception

## Teknik durum (HEALTHY)
- 125 KlineClosed işlendi
- 125 VwapEma evaluator çağrısı (100% coverage)
- Warmup tamam: 1440×1m + 60×1h per symbol
- WS disconnect 0, error flood 0
- Clock drift ~700ms (testnet normal)

## Halt gerekçesi — piyasa gerçekliği
Kullanıcı t150'de "hiç işlem yok" gözlemi → rakamsal analiz yapıldı (`loops/loop_20/diagnosis-no-trade.md`, `reality-check.md`).

**Son 180dk 1m bar hareketi:**
| Sembol | Ortalama/1dk | Max/1dk |
|---|---|---|
| BTC | %0.032 | %0.25 |
| BNB | %0.011 | %0.14 |
| XRP | %0.029 | %0.48 |

**15 dk pencere kümülatif hareket:**
| Sembol | Ortalama | Max |
|---|---|---|
| BTC | %0.21 | %0.90 |
| BNB | %0.14 | %0.42 |
| XRP | %0.30 | **%1.14** |

**Kullanıcı hedefi:** net %1-2 kar/trade → gross %1.2-2.2 (fee %0.2)
**Gerçek:** 15dk ortalama %0.14-0.30 → hedef ortalamanın 5-10 katı

→ Matematiksel olarak 4-AND koşul sağlanmasa bile hedef ulaşılamaz.
→ Hatta stratejinin filtresi kaldırılsa bile WR × avgWin fee + hedef için yetmez.

## Kullanıcı kararı (t150+)
**B seçildi:** Hedef gerçekleştir → **%0.3-0.5 net/trade**, sık işlem + sıkı SL + TP

"böyle sık işlem yapıp stoploss koy kardan faydalan"

## Loop 21 reform planı
1. binance-expert AR-GE — B yaklaşımı için akademik + Binance volatilite matematiği
2. architect ADR-0016 — VwapEmaHybridV2 (gevşek 3-of-4 weighted, TP %0.5-0.8, SL %0.3-0.5)
3. backend-dev — evaluator tune + **Sistem Olayları sayfası fix** (SystemEvents tablosu boş)
4. frontend-dev — **index hero küçült** (40px→28px) + afilli component'ler (counter anim, neon glow, ticker marquee, ring progress)
5. tester + reviewer
6. DB reset + API restart + Loop 21 cycle

## Önemli notlar — Loop 21 boot için

**Eski loglarda gözlemlenen ek bulgu:** XRP'de 11 kez `directionGate=true + vwapContext=true + volume=3x` ama `reclaim=false` — strateji sinyal eşiğine çok yakın geldi. Demek ki EMA21(1h) döndüğünde VWAP reclaim koşulu aşırı sıkı. ADR-0016'da bu bilgi kullanılmalı.

**Sistem Olayları boş:** logs.html sayfası `/api/logs/tail` endpoint'ini çağırıyor, 200 dönüyor ama data yok. SystemEvents tablosuna hiç insert yapılmamış. Event publishing servisi eksik/broken — Loop 21'de fix.
