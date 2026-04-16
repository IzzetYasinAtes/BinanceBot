---
name: pm-read-user-notes
description: Her checkpoint'te PM user-notes kuyruğunu okur. Kullanıcının /tell-pm ile mid-task bıraktığı notları alır, aktif plana entegre eder veya "şu notu aldım" der. Mid-task interrupt'ın pragmatik uyarlaması.
---

# pm-read-user-notes

Claude Code native mid-task chat desteklemiyor. Bu skill ile PM her chunk sonunda kullanıcının bıraktığı notları okur, **consume eder** ve plana entegre eder.

## Sıra

1. MCP `agent-bus.read_user_notes(since_seq=<son_consume_seq>)` çağır.
2. Eğer boş → "yeni not yok" de ve devam et.
3. Eğer not varsa her biri için:
   - Not metnini kullanıcıya göster: `📥 <ts>: "<mesaj>"`
   - Notun mevcut plana etkisini değerlendir:
     - (a) plan değişmiyor → "not aldım, devam ediyorum"
     - (b) plan değişiyor → yeni chunk ekle / sıralama değişir → "notu aldım, planı şöyle güncelledim: ..."
     - (c) plan iptal → "bu notla eski planı iptal ediyorum, yeni plan: ..."
   - PM sessiona `last_consumed_seq` = max(seq) olarak işaretler (context'te tut).
4. MCP `append_decision` ile "user note processed: <seq>" logu yaz.

## Kural

- Her checkpoint'te çağır (PM protokolü).
- Consume edilen not'u bir daha "yeni not" olarak gösterme — `since_seq` takibi şart.
- Kullanıcıya notu **aldığını ve nasıl işlediğini** açıkça bildir. Sessizce geçme.
- Not içeriği Türkçe olmasa bile çeviri yapma — olduğu gibi göster, kararı sen Türkçe ver.

## Örnek

```
📥 2026-04-16T14:23:11Z: "ETH için ayrı stream istemiyorum, BTC'yle aynı connection'dan gelsin"

→ Notu aldım. Plan güncellendi:
  Chunk 3 (backend-dev) artık tek WS connection'da BTC+ETH multiplex subscription kuracak.
  Binance-expert ile doğruladım: `/ws/btcusdt@kline_1m/ethusdt@kline_1m` combined stream mümkün.
  Devam edebilir miyim?
```
