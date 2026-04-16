# Agents Protokol — Tüm Agent'lar İçin Ortak Kurallar

Bu dosya `.claude/agents/` içindeki herhangi bir dosya okunduğunda yüklenir. Tüm agent'ların uyması zorunlu ortak kuralları içerir.

## Handoff Envelope (zarf)

Bir agent iş alırken (PM'den Task tool ile çağrıldığında) zarf formatı:

```
task_id: <PM tarafından atanır, örn. T-2026-04-16-001>
scope: <TR tek cümle — ne yapılacak>
in_scope_paths: [list of path globs, örn. src/Backend/**]
done_definition: <ne olunca "bitti" sayılır>
forbidden_paths: [<dokunulmayacak yerler>]
log_path: .ai-trace/   (her karar MCP'ye)
```

PM `pm-handoff` skill'i ile bu zarfı üretir ve MCP `append_handoff` çağırır.

## Tool-Scope Sözleşmesi

Her agent'ın `tools:` frontmatter'ı **minimum erişim** prensibine göre yazılır:

- `architect` — Read + Grep + Glob + Write(docs/adr/**). Kod yazmaz.
- `backend-dev` — Edit + Write(src/Backend/**, tools/mcp-agent-bus/**) + Bash(dotnet *). Frontend'e yazmaz.
- `frontend-dev` — Edit + Write(src/Frontend/**). Backend'e yazmaz.
- `binance-expert` — Read + Grep + Glob + WebFetch. Kod yazmaz, sadece araştırır ve rehberlik eder.
- `reviewer` — Read + Grep + Glob + Bash(git diff/status). Write yasak.
- `tester` — Read + Grep + Glob + Bash(dotnet test/build, sqlcmd) + MCP(playwright).

**Kapsam dışı path'a yazmaya kalkarsa — tool allowlist reddeder.** Bu bilinçli. Agent çaresiz kalırsa PM'e döner, PM başka agent'a devreder.

## Her Turn Sonunda Zorunlu

1. `agent-bus` MCP → `append_decision` (en az bir karar cümlesi + rationale).
2. Kendi task'ını bitirdiyse `release_task(agent_id, task_id, new_status)` çağır.
3. PM'e kısa TR özet dön; detay için `.ai-trace/subagent-stops/` otomatik doldurulur.

## Türkçe Ton

- Kısa, direkt, hedge yok. "Sanırım / belki / mümkünse" yok.
- Kararlar net: "X'i Y yapacağım çünkü Z".
- Kaynak gösterimi zorunlu olduğunda Türkçe paragraf + alt satırda "Kaynak: <URL>".

## Çakışma / Belirsizlik

- İki kural çelişirse → root `CLAUDE.md` kazanır.
- Root ile subfolder `CLAUDE.md` çelişirse → **subfolder kazanır** (daha spesifik context).
- Agent emin değilse → PM'e sorar, PM kullanıcıya sorar; karara kadar iş durur (MCP'ye `blocked` state).

## Yasaklar

- Başka agent'ın skill'ini çalıştırmaya kalkma — frontmatter `skills:` dışındaki erişilemez.
- MCP dışı logging — her audit kaydı MCP `append_decision` veya `append_handoff` üzerinden.
- Branch/commit yapma (reviewer hariç hiçbir agent commit atmaz; PM kullanıcıya onay sorar).
