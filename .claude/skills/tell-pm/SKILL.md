---
name: tell-pm
description: Kullanıcının /tell-pm <mesaj> ile mid-task not bırakması. Mesajı MCP agent-bus.append_user_note ile kuyruğa yazar. PM bir sonraki checkpoint'te pm-read-user-notes skill'i ile okur.
---

# tell-pm

**Bu skill global — kullanıcı her session'da çağırabilir.** Mid-task interrupt'ın pragmatik uyarlaması.

## Kullanım

Kullanıcı yazar:
```
/tell-pm ETH tarafını şimdilik erteleyelim, BTC'ye odaklan
```

Bu skill:
1. Mesajı string olarak alır (tüm args concat).
2. MCP `agent-bus.append_user_note(message=<mesaj>)` çağırır.
3. Kullanıcıya şunu döner:
   ```
   ✅ Not kuyruğa düştü (seq=<N>). PM bir sonraki checkpoint'te okuyacak.
   ```

## Kural

- Mesaj boşsa hata ver: "Mesaj boş olamaz. Örnek: /tell-pm <not metni>".
- MCP çağrısı başarısızsa (agent-bus down) lokal fallback: `.ai-trace/user-notes.jsonl`'a doğrudan append (hook-style). Kullanıcıya "agent-bus erişilemedi ama lokal dosyaya yazıldı" de.
- PM'i doğrudan tetikleme — sadece kuyruğa yaz. PM ne zaman checkpoint yaparsa o zaman görür.

## Tasarım Notu

Bu skill tek **global (user-invocable)** skill'dir. Diğer 29 skill agent'lara bağlı; bu genel çünkü kullanıcı her yerden "PM'e bir şey söylemek" isteyebilir.
