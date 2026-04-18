# Loop 4 Özeti (ERKEN KAPATILDI)

## Süre
- Başlangıç: 2026-04-18 06:03 UTC → Bitiş: ~06:49 UTC (~46 dk)
- **ERKEN KAPATIŞ** — kullanıcı talimatı: "az trade var, 24h %0.00 hatalı, uçtan uca audit gerek"

## Sonuç
- Paper $100 → **$95.30** (-%4.70 net)
- Toplam Order: 4 (hepsi 06:11 dolayında, 40+ dk hiç yeni order)
- Sinyal: 4 (eşit, fan-out 1:1 sized çalıştı)
- realizedPnl24h: -$0.035, consecutiveLosses: 3 (max=3 ama CB Healthy ❌ — yeni bug #12)

## Kritik Bulgular (Loop 5'e backlog)

### #12 — CB tetiklemiyor (consecutiveLosses=max ama Healthy)
RecordTradeOutcomeCommandHandler eşik kontrolü `>=` değil `>` olabilir, veya StrategyDeactivate akışı kopuk.

### #13 — Market Summary 24h %change = 0 (TÜM sembollerde)
`/api/market/summary` `change24hPct: 0` döner ama volume24hQuote dolu. BinanceMarketDataClient hesabı bozuk veya 24h ticker endpoint kullanmıyor.

### #14 — Trade frequency çok düşük (~4 order / 40dk)
- TrendFollowing EMA cross seyrek olay (5/20 cross dakikalıkta nadir)
- MeanReversion RSI <30 || >70 nadir
- Grid hiç sinyal üretmedi (henüz tanı yok)
Strateji parametreleri konservatif, sermaye kar etmiyor (-%4.7 net).

### #15 — Paper realism şüphesi
"Canlıda nasıl olacaksa öyle" gerek. Şu an: depth walk + 5bps slippage. Gerçek order book'ta partial fill, queue position, latency etkileri eksik.

## Karar: MUTATE — Loop 5 uçtan uca audit + reform

## Loop 5 Plan
1. binance-expert kapsamlı araştırma:
   - 24h değişim Binance API'da nasıl alınır (`/ticker/24hr`)
   - Trade frequency artırma stratejileri
   - Paper realism (canlı davranış simülasyonu)
2. architect: market summary fix + strategy frequency reform tasarım
3. backend-dev: uygulama
4. reviewer
5. DB drop, restart, Loop 5 4h cycle

## Commit
- Hash: c2f3642 (regression fix)
- Push: main ✅
