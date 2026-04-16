---
name: pm-decompose
description: Kullanıcıdan gelen yeni görevi ≤5 adımlık chunk'lara böler. Her chunk bir subagent çağrısı veya tek bir iş paketidir. PM her user turn'de otomatik çalıştırır.
---

# pm-decompose

Büyük görevleri chunk'lara bölmek, hem token maliyetini hem de checkpoint disiplinini sağlar.

## Kural

- Bir chunk = en fazla 5 eylem (tool call / subagent invocation / dosya okuma-yazma).
- Her chunk bağımsız çalıştırılabilir olmalı — chunk N başarısızsa chunk N+1 beklemeli, tüm plan patlamasın.
- Chunk sonunda kullanıcı onay verebilmeli, yön değiştirebilmeli.

## Örnek Dekompozisyon

Kullanıcı: "Binance'ten BTC/USDT kline stream'ini alıp DB'ye yazan backend feature ekle."

```
Chunk 1: binance-expert → kline stream spec, rate limit, reconnect stratejisi (araştırma)
Chunk 2: architect → aggregate tasarımı, ADR (Kline aggregate, KlineIngested domain event)
Chunk 3: backend-dev → EF migration + CQRS command (IngestKlineCommand + Handler)
Chunk 4: backend-dev → BackgroundService + Binance WS client + Channel<Kline>
Chunk 5: tester → Playwright ile admin UI doğrulaması + DB sanity + tester-error-scan
Chunk 6: reviewer → SOLID/DRY + reviewer-ws-resiliency checklist
```

## Çıktı Formatı

PM kullanıcıya göstermek için:

```
📦 Plan: "<özet>"
  1. [<agent>] <chunk 1 özet>
  2. [<agent>] <chunk 2 özet>
  ...
  
Onay verirsen 1. chunk'tan başlıyorum. /tell-pm ile not bırakabilirsin.
```

## Yasaklar

- Tek chunk'ta 5'ten fazla adım yazma (kullanıcı açıkça "hepsini arka arkaya yap, checkpoint istemiyorum" demediyse).
- Bağımlı chunk'ları paralele koşma — sırayla.
- Kripto konulu chunk'lar için `binance-expert` chunk'ını atlama — her kripto zincirinde **ilk chunk** odur.
