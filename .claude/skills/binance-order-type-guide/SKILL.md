---
name: binance-order-type-guide
description: Binance Spot order türleri ve timeInForce rehberi. MARKET, LIMIT, STOP_LOSS, STOP_LOSS_LIMIT, TAKE_PROFIT, TAKE_PROFIT_LIMIT, LIMIT_MAKER, OCO semantiği. GTC/IOC/FOK kullanımı. Test order endpoint. binance-expert agent'ın order konularında kullandığı skill.
---

# binance-order-type-guide

Spot order semantiği — hangi type ne zaman? Yanlış type seçimi fee/slipaj/fill hatası yaratır.

## Order Types

| Type | Açıklama | Parametreler | Ne zaman |
|---|---|---|---|
| `MARKET` | Anlık piyasa emri; orderbook'u yer | `quantity` **veya** `quoteOrderQty` | Hızlı fill şart, fiyat önemsiz |
| `LIMIT` | Belirli fiyat; maker veya taker | `quantity`, `price`, `timeInForce` | Fiyat kontrolü istiyor |
| `STOP_LOSS` | Fiyat X'e ulaşınca MARKET order | `quantity`, `stopPrice` | Zarar durdurma (kaba) |
| `STOP_LOSS_LIMIT` | Fiyat X'e ulaşınca LIMIT emri yerleştir | `quantity`, `price`, `stopPrice`, `timeInForce` | Zarar durdur + slipaj kontrolü |
| `TAKE_PROFIT` | Fiyat X'e ulaşınca MARKET | `quantity`, `stopPrice` | Kar alma (kaba) |
| `TAKE_PROFIT_LIMIT` | Fiyat X'e ulaşınca LIMIT | `quantity`, `price`, `stopPrice`, `timeInForce` | Kar + slipaj kontrolü |
| `LIMIT_MAKER` | Sadece maker olarak; taker olursa reddedilir | `quantity`, `price` | Maker fee avantajı; taker'a dönerse cancel |

### OCO (One Cancels Other)
Ayrı endpoint: `POST /api/v3/order/oco` — LIMIT + STOP-LOSS-LIMIT birlikte; biri fill olursa diğeri iptal. Take-profit + stop-loss kombinasyonu için ideal.

## timeInForce

| Değer | Anlamı | Kullanım |
|---|---|---|
| `GTC` | Good-Till-Canceled | Varsayılan; LIMIT için |
| `IOC` | Immediate-Or-Cancel | Anında fill edebildiğin kadarı; kalanı iptal |
| `FOK` | Fill-Or-Kill | Tamamı fill olmazsa emir iptal |

## Önemli Filter'lar (LOT_SIZE, PRICE_FILTER, MIN_NOTIONAL)

`exchangeInfo` endpoint'inden canlı al:
- `LOT_SIZE.stepSize` — quantity bu adımın katı olmalı
- `LOT_SIZE.minQty`, `maxQty` — quantity aralığı
- `PRICE_FILTER.tickSize` — price bu adımın katı olmalı
- `MIN_NOTIONAL.minNotional` — price × quantity minimum değer

**Kural:** order göndermeden önce uygulamada filter'ları uygula; yoksa API `-1013 FILTER_FAILURE` döner.

```csharp
// Kaba örnek
var stepSize = 0.00001m;
quantity = Math.Floor(quantity / stepSize) * stepSize;
```

## Test Order

`POST /api/v3/order/test` — gerçek order atmadan validasyon yapar. Body aynı, response boş. Her yeni order kod path'ini test order'la doğrula.

## Signature & recvWindow

- Tüm SIGNED endpoint'ler HMAC-SHA256 signature ister — body params + secret.
- `recvWindow` (ms) — timestamp'in server time'ından kaç ms sonraya kadar kabul edilir; varsayılan 5000, max 60000.
- Clock drift > recvWindow → `-1021 INVALID_TIMESTAMP` hatası.

## Kural

- Slipaj hassas stratejide MARKET order asla kullanma — LIMIT_MAKER veya LIMIT + IOC tercih et.
- Stop + Take-Profit birlikte koyacaksan OCO kullan, iki ayrı order yerine.
- Test order'dan geçmeyen kod prod'a gitmez.
- Clock sync sorunu olursa NTP doğrula; `GET /api/v3/time` ile server time farkı ölç.

## Kaynak

- https://binance-docs.github.io/apidocs/spot/en/#new-order-trade
- https://binance-docs.github.io/apidocs/spot/en/#new-oco-trade
- https://binance-docs.github.io/apidocs/spot/en/#filters
