---
name: binance-rate-limit-analysis
description: Binance REST API weight tabanlı rate limit analizi. 1m/1d pencerelerde weight hesabı, IP-level vs UID-level ban, X-MBX-USED-WEIGHT header izleme, backoff stratejisi. binance-expert agent'ının rate-limit planlaması için.
---

# binance-rate-limit-analysis

Binance, endpoint başına **weight** atar ve weight'leri pencerelerde toplar. Aşım → HTTP 429 → sonra 418 (IP ban).

## Limit Tipleri

| Pencere | Limit | Ne |
|---|---|---|
| `REQUEST_WEIGHT` 1m | 6000 | Weight toplamı (endpoint başına farklı) |
| `ORDERS` 10s | 100 | Order endpoint sayısı |
| `ORDERS` 1d | 200,000 | Günlük order sayısı |
| `RAW_REQUESTS` 5m | 61,000 | Fiziksel istek sayısı |

Bu değerler **değişebilir** — `GET /api/v3/exchangeInfo` → `rateLimits[]` ile canlı doğrula.

## Önemli Endpoint Weight'leri (tipik)

| Endpoint | Weight |
|---|---|
| `GET /api/v3/time` | 1 |
| `GET /api/v3/exchangeInfo` | 10 |
| `GET /api/v3/depth` (limit=5000) | 50 |
| `GET /api/v3/klines` | 1 |
| `GET /api/v3/ticker/24hr` (all) | 40 |
| `POST /api/v3/order` | 1 (ama ORDERS counter +1) |
| `GET /api/v3/account` | 10 |

## Header İzleme

Her REST cevabında:
- `X-MBX-USED-WEIGHT-1M` — mevcut pencerede kullanılan weight
- `X-MBX-ORDER-COUNT-10S`, `X-MBX-ORDER-COUNT-1D`

**Kural:** backend HTTP response interceptor'ı bu header'ları `ILogger`'a yazsın + %80 threshold'unda uyarı versin.

## Ban Mantığı

- HTTP **429** → geçici hız aşımı; `Retry-After` header kadar bekle.
- HTTP **418** → IP ban (2m → 3d arası). `Retry-After` zorunlu.
- Ban süresince **aynı IP'den hiçbir endpoint** çağrılmamalı.

## Backoff Stratejisi

```
1. 429 → Retry-After değerini oku, bekle (min 1s, max 60s).
2. Tekrar 429 → exponential backoff × jitter (base 2s, max 60s).
3. 418 → durma; admin alarmı tetikle.
4. WS varsa WS'den mümkün olanı al (kline/trade/depth hepsi WS'te); REST sadece kritik.
```

## Planlama Kuralı

- Scheduled job'lar 1m pencereye yayılsın (burst atmasın).
- Aynı endpoint'e parallel request minimum tut — hashset'le track et.
- Order flood korumasında uygulama seviyesinde **token bucket** — Binance'i itmeden önce reddet.

## Backend-dev İçin

- `Polly` ile `WaitAndRetryPolicy(429, 418)` — `Retry-After` parse.
- `IHttpClientFactory` named client + `DelegatingHandler` ile weight header'ları log.
- `BackgroundService` scheduler: her job kendi weight bütçesini bilmeli.

## Kaynak

- https://binance-docs.github.io/apidocs/spot/en/#limits
- https://binance-docs.github.io/apidocs/spot/en/#filters (exchangeInfo live limits)
