---
name: pm-handoff
description: PM subagent'ı Task tool ile çağırmadan önce standart handoff zarfı üretir (task_id, scope, done_definition, forbidden_paths, in_scope_paths) ve MCP agent-bus.append_handoff ile log'a yazar. Zarf formatı trace parseability garanti eder.
---

# pm-handoff

PM her Task(...) çağrısından **önce** bu skill'i çalıştırır. Zarf, subagent'a verilecek prompt'un ilk bölümüne aynen kopyalanır.

## Zarf Şablonu

```
# Handoff Envelope
task_id: T-<YYYY-MM-DD>-<NNN>
scope: <tek cümle TR — ne yapılacak>
in_scope_paths:
  - <glob1>
  - <glob2>
done_definition: <ne olursa "bitti" sayılır>
forbidden_paths:
  - <dokunulmayacak yerler>
log_contract: her kararında agent-bus.append_decision çağır
return_format: <kısa TR özet + önemli dosya listeleri>
```

## Sıra

1. Task id'si oluştur: `T-<UTC-date>-<sıralı>` (aynı günde artan sayı).
2. Scope'u tek cümle Türkçe yaz. Muğlak olmasın — "X özelliğini şuraya ekle" gibi.
3. `in_scope_paths` sadece agent'ın kendi tool-scope'una uyanlar (örn. backend-dev için `src/**`).
4. `done_definition` ölçülebilir olsun ("X endpoint çalışıyor ve Z test yeşil").
5. `forbidden_paths`'i **açıkça** yaz — agent oraya yazmaya kalkmasın.
6. MCP `agent-bus.append_handoff(from_agent="pm", to_agent=<name>, task_id=..., scope=..., done_definition=..., forbidden_paths=...)` çağır.
7. Zarfı subagent prompt'unun ilk 15 satırına yapıştır.

## Kural

- Zarf yoksa Task çağırma. Zarf = audit trail ön-koşulu.
- Subagent zarfı okuyup uygulamalı; okumadıysa reviewer fark eder.
- Zarf Türkçe, path'ler/kod İngilizce.
