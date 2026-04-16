---
name: reviewer-ws-resiliency
description: WebSocket kod değişikliklerinde resiliency checklist. Exponential backoff + jitter, heartbeat (ping/pong), missed-pong detection, subscription replay, at-least-once handler idempotency, graceful shutdown, connection pool limits. reviewer agent Binance WS PR'larında uygular.
---

# reviewer-ws-resiliency

WebSocket kodu kırılgan — checklist olmadan review etme.

## Checklist

### Reconnect & Backoff
- [ ] Disconnect tespit ediliyor mu? (explicit close, pong timeout, exception)
- [ ] **Exponential backoff** var mı? (1s → 2s → 4s → 8s → ... max 30s)
- [ ] **Jitter** eklenmiş mi? `Random(0, 1000)ms` — thundering herd önler.
- [ ] Max retry cap var mı? (N sonra alarm tetikle)
- [ ] Başarılı connection sonrası attempt counter **reset** ediliyor mu?

### Heartbeat
- [ ] `ClientWebSocket.KeepAliveInterval` set edilmiş mi (20s önerilir)?
- [ ] Binance server'ın **3 dk ping**'ine pong dönüldüğü (framework otomatik mi, manuel mi)?
- [ ] Pong timeout explicit olarak handle ediliyor mu? (10 dk pong yoksa disconnect zorla)

### Subscription Replay
- [ ] Reconnect sonrası eski stream'ler **otomatik yeniden subscribe** ediliyor mu?
- [ ] Subscribe rate limit (5 msg/sn) aşılmıyor mu? (queue/batch)
- [ ] Listen key (user data stream) yenileniyor mu? (60 dk'da bir PUT)

### Missed Event / Gap
- [ ] Her stream'in son event time'ı tutuluyor mu?
- [ ] Reconnect sonra gap tespit ediliyor mu? (REST `/api/v3/klines` ile fill)
- [ ] Gap fill sırası: önce DB'de var mı kontrol (idempotent), sonra insert.

### Handler Idempotency
- [ ] Unique constraint var mı? (Symbol + OpenTime gibi)
- [ ] Duplicate event geldiğinde silent ignore mu, update mi?
- [ ] Exception handler içinde — tek mesaj crash tüm stream'i durdurmasın.

### Graceful Shutdown
- [ ] `BackgroundService.StopAsync` — in-flight message'ları bitir, sonra kapat?
- [ ] `CancellationToken` zincirleme edilmiş mi?
- [ ] `ws.CloseAsync(NormalClosure)` gönderiliyor mu?

### Connection Limits
- [ ] Tek connection'da max 1024 stream sınırına dikkat?
- [ ] 24h connection limit (Binance tarafı) — beklenen davranış, reconnect tetikleniyor mu?

### Resource Leak
- [ ] `using` / `await using` — WebSocket / HttpClient / Channel doğru dispose?
- [ ] `BackgroundService` stoptan sonra thread leak yok?

## Verdict

Aşağıdakilerden **en az biri** eksikse → 🚫 blocker:
- Exponential backoff + jitter yok
- Subscribe replay yok
- Handler idempotent değil (unique index yok)

Aşağıdaki eksikse → ⚠️ minor:
- Pong timeout explicit değil
- Jitter yok ama exponential var
- Gap fill yok (history reconstruction kaybı)

## Kaynak

- https://binance-docs.github.io/apidocs/spot/en/#websocket-market-streams
- https://binance-docs.github.io/apidocs/spot/en/#user-data-streams
- Plan skill `binance-ws-stream-design`
