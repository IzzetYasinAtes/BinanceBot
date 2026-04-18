# Loop 1 — Research: REST Kline Backfill

**Tarih:** 2026-04-17
**Agent:** binance-expert
**Konu:** Boot'ta REST backfill ile evaluator warmup'ını ortadan kaldır

## Sorun
KlineIngestionWorker yalnızca WebSocket persist ediyor. Her DB drop sonrası strateji evaluator'lar (TrendFollowing SlowEma=20, ATR=14; MeanReversion BB=20, RSI=14) ~20-30 dk warmup yaşıyor. 4h loop'larda kayıp süre ölçümü bozuyor.

## Karar Bulguları

### 1. Endpoint
- `GET /api/v3/klines` — testnet.binance.vision'da çalışıyor (200 OK, canlı doğrulandı).
- **Limit max = 1000** (default 500). 1m × 1000 = **16h 40dk geçmiş** tek istekte alınır.

### 2. Rate Limit
- Sabit weight = **2 / istek** (limit parametresi weight'i değiştirmez).
- 3 sembol × 1 istek = **6 weight** toplam.
- Mainnet limit 1200/dk → %0.5 kullanım. Çakışma riski sıfır.
- Testnet limit 6000/dk (test rahat).

### 3. Pattern
- **Sıralı çek** (paralel kazancı yok, retry izolasyonu daha kolay).
- Polly: 429 → `Retry-After` saniye bekle; 418 → min 2dk; 5xx → exponential 1s/2s/4s, max 3 deneme.
- Mevcut `BinanceMarketDataClient.GetKlinesAsync` imzası hazır (startTime/endTime/limit).

### 4. Kapsam
- **Sadece 1m yeterli** (mevcut stratejiler için). 1000 bar = 16h derinlik.
- 5m/15m/1h ileride yeni strateji eklenirse düşünülebilir.

### 5. Çakışma / Idempotency
- REST'ten gelen son bar `IsClosed=false` olabilir (henüz kapanmamış).
- `KlineIngestionWorker.PersistAsync` zaten upsert (FirstOrDefaultAsync → insert/update). PK = (Symbol, Interval, OpenTime).
- WS aynı barı sonradan kapanmış halde yazsa sorun yok.

### 6. Sıra
- **Doğru sıra: WS subscribe ÖNCE → REST backfill SONRA.**
- Backfill ayrı bir `IHostedService.ExecuteAsync` başında bir kez koşar, WS supervisor bağımsız.
- 700ms backfill penceresinde gerçekleşen barlar WS'den de capture edilir → gap yok.

## Mevcut Kod Hazırlığı
- `BinanceMarketDataClient.GetKlinesAsync` (src/Infrastructure/Binance/BinanceMarketDataClient.cs:104-152) hazır.
- `BinanceOptions.RestBaseUrl = "https://testnet.binance.vision"` doğru.
- Yeni `KlineBackfillWorker : IHostedService` eklenmeli.

## Kaynaklar
- https://developers.binance.com/docs/binance-spot-api-docs/rest-api/market-data-endpoints#klinecandlestick-data
- Canlı doğrulama: `curl https://testnet.binance.vision/api/v3/klines?symbol=BTCUSDT&interval=1m&limit=1000` → 200, `x-mbx-used-weight-1m: 2`
- Rate limit: `curl https://testnet.binance.vision/api/v3/exchangeInfo` → REQUEST_WEIGHT/1m = 6000 (testnet) / 1200 (mainnet)
