---
name: pm-status
description: /status komutuyla kullanıcıya anlık workspace durumu verir — TodoWrite task'ları, son 5 handoff (MCP read_handoffs), bekleyen user-notes, blocker'lar. Sadece PM agent'ı çağırır.
---

# pm-status

Kullanıcı `/status` yazdığında bu skill devreye girer. PM tüm sistemin anlık fotoğrafını Türkçe tek ekrana sığdırır.

## Çalışma

1. `TodoWrite` tool'undan mevcut task listesini al (status, sırada, blocker).
2. MCP `agent-bus.read_handoffs(last_n=5)` — son 5 agent devri.
3. MCP `agent-bus.read_user_notes(since_seq=0)` — tüm not kuyruğu; hangileri henüz consume edilmemiş.
4. `.ai-trace/subagent-stops/` dizini — son 3 completion'ı kısaca oku.

## Çıktı Formatı

```
📋 Durum — <ISO 8601 UTC>

Aktif görevler:
  ✔ tamam: <count>
  ⏳ yürüyen: <count> (<subjects>)
  🚫 blocked: <count> (<reasons>)

Son 5 handoff:
  <from> → <to> : <scope özet>  (<ts>)
  ...

Kullanıcı notları (consume edilmemiş):
  • <message>
  (yoksa: "yeni not yok")

Son subagent completion'ları:
  - <agent>: <one-line summary>
```

## Kural

- Bu skill **read-only** — hiçbir state değişikliği yapmaz.
- Kısa tut — kullanıcı scroll yapmasın.
- Henüz consume edilmemiş user-notes varsa kullanıcıya hatırlat ki PM bir sonraki chunk'ta bunları işleyecek.
