# BinanceBot

Binance WebSocket + REST hibrit, .NET 10 + Vue 3 CDN, DDD + Clean Architecture + CQRS.

## Durum

🏗️ **Kurulum aşaması.** Şu an sadece **AI çalışma sistemi** kuruludur — uygulama kodu (backend, frontend, DB) henüz yazılmadı. AI workspace hazır olduğunda (bu noktada) aşama 2'ye (uygulama geliştirme) geçilir.

## Kurulum

### Gereksinimler

- **.NET 10 SDK** (10.0.103+)
- **Node.js 18+** (Playwright MCP + hook scriptleri için)
- **MSSQL** (SQL Express veya LocalDB — dev için)
- **Git Bash** veya WSL (Windows'ta hook scripti için)

### İlk Kurulum

```bash
# MCP agent-bus server build
dotnet build -c Release tools/mcp-agent-bus/mcp-agent-bus.csproj

# Playwright MCP browser binary'si (tek seferlik)
npx -y @playwright/mcp@latest --version

# Claude Code'u aç — otomatik session başlar
```

## Yapı

```
BinanceBot/
├── CLAUDE.md                  # root altın kurallar
├── .claude/                   # agents, skills, hooks, mcp config
│   ├── agents/                # 7 agent (pm, architect, backend-dev, frontend-dev, binance-expert, reviewer, tester)
│   ├── skills/                # 30 skill (agent başına dedike)
│   ├── hooks/trace.sh         # logging hook
│   ├── mcp.json               # 3 MCP server (agent-bus, playwright, memory)
│   └── settings.json          # hook + permission
├── tools/mcp-agent-bus/       # .NET 10 MCP server (agent iletişim omurgası)
├── docs/                      # ADR, features, glossary, workspace-guide, REFERENCES
├── src/
│   ├── Backend/               # .NET 10 app (HENÜZ BOŞ — AI ile yazılacak)
│   └── Frontend/              # Vue 3 CDN (HENÜZ BOŞ — AI ile yazılacak)
└── .ai-trace/                 # agent audit log (JSONL)
```

## Nasıl Çalışır?

1. Claude Code aç.
2. PM (Proje Yöneticisi) karşılar — Türkçe.
3. "X yapalım" de → PM görevi ≤5 chunk'a bölüp uzman agent'lara dağıtır.
4. Her chunk sonunda özet + onay.
5. Mid-task not: `/tell-pm <mesaj>`.
6. Durum: `/status`.

Detaylı kullanım: [`docs/workspace-guide.md`](./docs/workspace-guide.md).

## Agent Seti

| Agent | Rol |
|---|---|
| `pm` | Orchestrator, kullanıcı ile konuşan tek agent |
| `architect` | DDD/Clean/CQRS sınır + ADR |
| `backend-dev` | .NET 10 + EF Core + MediatR kodu |
| `frontend-dev` | Vue 3 CDN + importmap + SFC-less |
| `binance-expert` | Binance/kripto domain otoritesi (zorunlu danışman) |
| `reviewer` | Read-only diff review + security scan |
| `tester` | Playwright UI + MSSQL sanity + API contract |

## Logging / Audit

Her agent kararı, her handoff, her user note MCP `agent-bus` üzerinden `.ai-trace/` JSONL'lerine yazılır. Hook'lar paralel olarak session/tool-call ham payload'ını tutar (ignored). Detaylı şema: [`.ai-trace/README.md`](./.ai-trace/README.md).

## Kaynaklar

Tüm skill/kural kaynağı: [`docs/sources/REFERENCES.md`](./docs/sources/REFERENCES.md). Starred GitHub repoları + resmi dokümantasyon.

## Lisans

Bkz. `LICENSE`.
