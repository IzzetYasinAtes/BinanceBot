# tools/mcp-agent-bus/ — CLAUDE.md

BinanceBot'un agent iletişim MCP server'ı. .NET 10 console app. Bu dosya `tools/mcp-agent-bus/**` dokunulduğunda yüklenir.

## Amaç

- Agent'lar arası iletişim omurgası (handoff, decision, user-notes).
- `.ai-trace/` dizinine JSONL yazım.
- Task claim/release state yönetimi.

## Mimari

- `Program.cs` — `Host.CreateApplicationBuilder` + `AddMcpServer().WithStdioServerTransport().WithTools<T>()`.
- `Tools/AgentBusTools.cs` — tüm MCP tool'ları tek sınıfta (`[McpServerToolType]` + `[McpServerTool]`).
- `Storage/JsonlWriter.cs` — append-only, file lock retry.
- `Storage/TaskStateStore.cs` — JSON + atomic rename save.

## Yeni Tool Ekleme

1. `AgentBusTools.cs` içine metot ekle:
   ```csharp
   [McpServerTool(Name = "<snake_case>"), Description("TR açıklama.")]
   public static object MyTool(
       JsonlWriter writer,                     // DI resolved
       [Description("…")] string arg1,
       [Description("…")] int arg2 = 0)
   {
       // ...
       return new { ok = true };
   }
   ```
2. Metot **`Task<T>` veya `object`** dönmeli. JSON serializable olmalı.
3. DI servisleri (writer, store) parametre olarak inject edilir.
4. Build: `dotnet build -c Release`.
5. Claude Code session restart → yeni tool yüklenir.

## Kurallar

- **Stdout sadece JSON-RPC için**. Log stderr'e (`LogToStandardErrorThreshold = Trace`).
- **InvariantGlobalization: true** — datetime'lar "O" format.
- Her tool:
  - DI parametreleri başta (writer, store).
  - Kullanıcı parametreleri `[Description("...")]` ile.
  - Optional params sona, default değerli.
  - Return JSON serializable (anonymous object, record, dictionary).
- Exception'ı metot içinde tut — unhandled exception MCP'de error döner ama process hayatta kalır (Host generic exception handler).

## JSONL Yazım Disiplini

- Her kayıt: `{ "seq": <long>, "ts": "<ISO>", "kind": "<decision|handoff|user_note>", ... }`.
- Seq monoton — `JsonlWriter` Interlocked ile atomic.
- Dosya lock retry: 3x × 50ms backoff.
- Restart'ta disk'ten max seq okunur, devam eder.

## Task State Store

- Atomic rename pattern: `task-state.json.tmp` yaz → `Move` ile overwrite.
- Read: file-sharing ile okur (retry ile), broken JSON → boş dict döner.
- Write: lock alınır, dict update, disk'e yazılır.

## Yasaklar

- `Console.WriteLine` / `Console.Out` — stdout kontamine eder (MCP çöker).
- Global state (`static` mutable field) — storage sınıflarında da lock'lu.
- External HTTP çağrısı — MCP server yalnız local I/O.
- Secret / env var key log'a basılması.

## Build/Test

```bash
dotnet build -c Release /d/repos/BinanceBot/tools/mcp-agent-bus/mcp-agent-bus.csproj
# Elle test (stdio'da MCP initialize + tool call)
dotnet run --project /d/repos/BinanceBot/tools/mcp-agent-bus
```

Claude Code otomatik spawn eder; manuel run nadiren gerek.

## Kaynak

- https://github.com/modelcontextprotocol/csharp-sdk
- https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/QuickstartWeatherServer
- https://modelcontextprotocol.io/
