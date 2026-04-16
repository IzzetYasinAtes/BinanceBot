---
name: binance-ws-stream-design
description: Binance WebSocket stream subscription tasarım rehberi. Kline/trade/depth/bookTicker/userData stream tipleri, single vs combined stream, ping/pong heartbeat, 24h connection limit, reconnect+replay pattern. binance-expert agent'ının WS konularında kullandığı skill.
---

# binance-ws-stream-design

Binance WS ile gerçek zamanlı veri tüketiminin tasarım referansı. Reconnect/resiliency kritik.

## Stream Endpoint'leri

- **Tek stream:** `wss://stream.binance.com:9443/ws/<streamName>`
- **Combined stream:** `wss://stream.binance.com:9443/stream?streams=<s1>/<s2>/...` — payload `{ "stream": "...", "data": {...} }`

### Stream Adları

- `<symbol>@kline_<interval>` — 1m/3m/5m/15m/30m/1h/4h/1d/1w/1M — örn. `btcusdt@kline_1m`
- `<symbol>@trade` — tek trade
- `<symbol>@aggTrade` — aggregate trade
- `<symbol>@depth` / `<symbol>@depth@100ms` — orderbook diff
- `<symbol>@bookTicker` — best bid/ask
- User Data Stream — `listenKey` ile; `/api/v3/userDataStream` POST ile key al

## Lifecycle Kuralları (ÖZEL DİKKAT)

1. **Ping/Pong:** Server her **3 dakikada** ping gönderir; client **10 dk içinde** pong dönmezse disconnect. Kütüphanedeki otomatik pong'u doğrula.
2. **Max connection:** Tek connection **24 saat** sonra server tarafından kapatılır — uygulamada beklenen davranış.
3. **Listen key:** User data stream için 60 dk'da bir `PUT /api/v3/userDataStream` ile refresh zorunlu.
4. **Stream limit:** Tek connection'da max **1024 stream**.
5. **Rate limit:** 5 mesaj/saniye (subscribe/unsubscribe için).

## Reconnect + Replay Pattern

```
1. Disconnect tespit et (socket close, pong timeout, explicit error)
2. Exponential backoff + jitter: 1s → 2s → 4s → 8s → 16s (max 30s cap)
3. Yeni connection'ı aç
4. Subscribe replay — mevcut stream listesini aynı sırayla yeniden subscribe et
5. User data için yeni listen key al (eski geçersiz olabilir)
6. Missed event tespit: her stream'in son event time'ını tut; reconnect sonrası REST ile gap doldur (kline için /api/v3/klines)
7. Handler idempotent olmalı — aynı event tekrar gelirse DB'de duplicate kontrol (unique constraint: symbol + openTime)
```

## Tek vs Combined Stream Kararı

- <10 stream → combined kullan (tek connection, paylaşılan ping/pong).
- 10-1024 stream → combined + yönet.
- >1024 stream → birden çok connection'a böl.

## Backend-dev İçin Öneriler

- `System.Net.WebSockets.ClientWebSocket` + `Channel<T>` producer/consumer.
- `BackgroundService` ile supervisor — restart policy: always-on.
- `HostedService` ctor'da `CancellationToken` dinle, graceful shutdown.
- Handler'da DB insert + domain event publish ayır — trickle failure'lar DB'yi durdurmasın.

## Kural

- Tek WS connection'a 1024'ten fazla stream koyma.
- Subscribe replay olmadan reconnect yasak — sadece soket açıp eski stream'leri unutmak veri kaybı demek.
- Handler'da exception yakalama — tek mesaj bozulursa tüm stream durmasın.
- Heartbeat pong'u framework'e güven, ama timeout ekleyerek doğrula.

## Kaynak

- https://binance-docs.github.io/apidocs/spot/en/#websocket-market-streams
- https://binance-docs.github.io/apidocs/spot/en/#user-data-streams
