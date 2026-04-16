# mcp-agent-bus

BinanceBot workspace'inin **agent iletişim omurgası**. .NET 10 console app, stdio transport ile MCP server olarak çalışır. Claude Code session'ı başladığında `.claude/mcp.json` tarafından spawn edilir.

## Sağladığı Tool'lar

| Tool | Amaç |
|---|---|
| `ping` | Sağlık kontrolü |
| `append_decision` | Agent kararı → `.ai-trace/decisions.jsonl` |
| `append_handoff` | Agent devri → `.ai-trace/handoffs.jsonl` |
| `read_handoffs` | Son N handoff |
| `get_task_state` | Task durumu + geçmiş |
| `claim_task` | Task sahiplenme (conflict detection) |
| `release_task` | Task serbest bırakma + status |
| `append_user_note` | `/tell-pm` kullanıcı notu kuyruğa |
| `read_user_notes` | PM checkpoint'te kuyruğu okuma |

## Build & Run

```bash
cd tools/mcp-agent-bus
dotnet build -c Release
# Manuel test (stdio'da JSON-RPC bekler)
dotnet run -c Release
```

Claude Code otomatik olarak `.claude/mcp.json`'daki tanımla başlatır; manuel run'a gerek yok.

## Mimari

- `Program.cs` — host wiring + DI
- `Tools/AgentBusTools.cs` — 9 MCP tool, `[McpServerTool]` attribute'u ile işaretli
- `Storage/JsonlWriter.cs` — append-only JSONL, file lock + retry
- `Storage/TaskStateStore.cs` — task durumu `.ai-trace/task-state.json`, atomic rename ile güvenli yazım

## Tasarım Notları

- **Sequence numarası** JSONL dosyalarının max seq'inden başlatılır — restart'ta monoton artmaya devam eder.
- **File lock retry** 3x × 50ms backoff; Windows dosya paylaşım kilitleri için yeterli.
- **InvariantGlobalization: true** — datetime format uzlaşısız O-format (ISO 8601).
- **Logging** stderr'e gider (stdout JSON-RPC'ye ayrılmıştır — MCP protokol kuralı).
