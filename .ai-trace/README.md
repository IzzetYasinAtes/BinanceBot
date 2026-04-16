# .ai-trace/ — BinanceBot AI Log Şeması

Bu dizin, AI workspace'inin **audit trail**'idir. MCP `mcp-agent-bus` server'ı ve `.claude/hooks/trace.sh` buraya yazar. Uygulama kodu değil — sadece agent/session telemetrisi.

## Dosyalar

| Dosya | Commit | Format | Kim yazar | Ne içerir |
|---|---|---|---|---|
| `decisions.jsonl` | ✅ | JSONL | MCP `append_decision` | Her agent'ın karar satırı |
| `handoffs.jsonl` | ✅ | JSONL | MCP `append_handoff` + hook `subagent-stop` | PM↔subagent devirleri |
| `user-notes.jsonl` | ✅ | JSONL | MCP `append_user_note` (via `/tell-pm`) | Kullanıcının mid-task notları |
| `task-state.json` | ✅ | JSON | MCP `claim_task`/`release_task` | Task durum + geçmiş |
| `subagent-stops/*.md` | ✅ | Markdown | Hook `subagent-stop` | Her subagent completion özeti |
| `sessions/<date>.jsonl` | ❌ (ignored) | JSONL | Hook `user-prompt` + `session-end` | Kullanıcı prompt'ları + session marker |
| `tool-calls/<date>.jsonl` | ❌ (ignored) | JSONL | Hook `tool-use` | Her PostToolUse (debug için) |
| `bus-health.jsonl` | ❌ (ignored) | JSONL | MCP self-health | Server içi hata/uyarı |

## Kayıt şeması (JSONL)

Her satır bağımsız bir JSON objesi. Ortak alanlar:
- `ts` — ISO 8601 UTC timestamp
- `seq` — monoton artan sayı (MCP writer tarafından atanır, restart'ta kaldığı yerden devam)
- `kind` — kayıt tipi: `decision`, `handoff`, `user_note`, `user_prompt`, `tool_use`, `subagent_stop`, `session_end`

## Audit sorguları

```bash
# Son 20 kararı gör
tail -20 .ai-trace/decisions.jsonl | cat

# Hangi agent en çok ne zaman karar verdi
cat .ai-trace/decisions.jsonl | grep '"agent":"backend-dev"' | wc -l

# Git history'de karar değişim zinciri
git log -p -- .ai-trace/decisions.jsonl

# Belirli bir session'ın subagent completion'larını listele
ls -la .ai-trace/subagent-stops/
```

## Retention

Dosya tabanlı rotation yok. `git log` ile tarihsel takip yeterli. `decisions.jsonl` > 10 MB olursa aylık partition'a geçilir (plan bölüm 13/8).

## Gitignore notu

`sessions/` ve `tool-calls/` ham payload içerir (potansiyel gizli veri) ve `.gitignore`'da; kalıcı audit sadece `decisions`, `handoffs`, `user-notes`, `task-state`, `subagent-stops` üzerinden.
