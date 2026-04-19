# Loop 23 Reform Brief — 2026-04-19 ~20:00 UTC

Kullanıcı talimatı (birebir): "biz bu algoritmayı komple değiştirelim böyle olmayacak hiç işlem yok... dakikada 2-3 işlem açık olsun... saatle en az 180 işlem gibi bişeyler... 30sn 50sn 1dk gibi süreçlerde karlardan yararlanarak kartopu etkisi ile parayı büyütmeliyiz... 24. Loop ta saate en az 150 işlem yapılıyor ve kar ediyor olmamız gerek"

## HEDEFLER (Loop 24 için kesin)
- **Saatte ≥150 işlem** (dakikada 2.5-3)
- **Net kar** (fee drag'den sonra)
- **Sizing:** `max(equity × 0.01, 1.0) USD` (kullanıcı: "1 dolar alt limit fee ler falan için mantıklı değilse bunu değiştirebilirsin" — binance-expert araştıracak)
- **Timeframe:** 30sn-1dk, saniyelik mümkün
- **MaxOpenPositions:** 3-4 paralel (her symbol 1)
- **TP/SL:** Sıkı — fee'yi karşılayacak ama tetiklenebilir mesafe
- **UI:** Entry + Hedef + Stop Loss + canlı PnL net görünür

## SEMBOL LİSTESİ (KATİ)
BTC, ETH, BNB, XRP — başka yok. Spekülasyondan etkilenmeyen major coin'ler.

## FEE MATEMATİĞİ (KRİTİK)
- Binance spot: taker 0.1%, maker 0.0% (BNB discount %0.075)
- 150 trade/saat × taker 0.2% round-trip = **saatte %30 fee drag** (!!)
- Kar olması için: avg net per trade > 0 (fee düşüldükten sonra)
- %1 sizing × $1 notional × 0.002 = $0.002 fee/trade
- Günde 3600 trade × $0.002 = $7.2 fee ($100 bakiyeden)
- **Gerekli:** her trade **maker order** (fee $0) veya %0.3+ gross hareket

## AR-GE SORULARI (binance-expert)
1. Binance spot minNotional exact (BTC/ETH/BNB/XRP) — $1 işlem MÜMKÜN MÜ?
2. Minimum qty × price = notional — LOT_SIZE filter + NOTIONAL filter kombine
3. Rate limit: 1200 request/dk (IP), 180 order/10s (UID) — 150 trade/saat için yeterli
4. Maker vs taker — market order taker (%0.1), limit order maker (%0.0)
5. 30sn-1dk micro-scalping pattern'ler: order flow imbalance, bid-ask spread capture, VWAP micro-reclaim, tick-based RSI
6. Akademik: "high frequency scalping profit", "crypto spot market making $100 capital"
7. Paper mode gerçekçilik: taker/maker ayrımı simülasyonda nasıl

## YASAKLAR
- Kartopu etkisi **sadece kar ettikten sonra** büyüsün (pozitif PnL ile)
- Zarara girerse sizing yine $1 minimum (azaltma yok)
- 4 coin dışına çıkma yasak

## SIRA
1. **binance-expert** derin AR-GE (başladı)
2. **frontend-dev** paralel UI polish + trade count (başladı)
3. **architect** ADR-0018 (binance-expert sonrası)
4. **backend-dev** uygulama (architect sonrası)
5. **tester** Playwright + runtime gözlem
6. **reviewer** SOLID + fee matematiği denetimi
7. DB reset + API boot + Loop 24 cycle
