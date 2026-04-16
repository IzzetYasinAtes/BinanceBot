# Kaynak Referansları — BinanceBot AI Workspace

Her skill ve kural bu kaynaklardan süzülüp Türkçe'ye adapte edildi. Bir pattern değiştiyse önce buraya bak.

## Claude Code — Subagent / Skill / Rule Templates

- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk) — MCP C# SDK + samples. QuickstartWeatherServer minimal template'imiz.
- [wshobson/agents](https://github.com/wshobson/agents) — geniş subagent koleksiyonu (delegation pattern referansı).
- [VoltAgent/awesome-claude-code-subagents](https://github.com/VoltAgent/awesome-claude-code-subagents)
- [hesreallyhim/awesome-claude-code](https://github.com/hesreallyhim/awesome-claude-code)
- [davila7/claude-code-templates](https://github.com/davila7/claude-code-templates)
- [PatrickJS/awesome-cursorrules](https://github.com/PatrickJS/awesome-cursorrules) — rule pattern kaynağı (`.cursorrules` → `CLAUDE.md`'ye uyarlama).

## .NET / ASP.NET Core / Clean Architecture / DDD / CQRS

- [learn.microsoft.com/dotnet](https://learn.microsoft.com/en-us/dotnet/) — resmi .NET dokümantasyonu.
- [learn.microsoft.com/aspnet/core](https://learn.microsoft.com/en-us/aspnet/core/) — ASP.NET Core resmi.
- [learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/) — resmi DDD/CQRS .NET rehberi.
- [learn.microsoft.com/ef/core](https://learn.microsoft.com/en-us/ef/core/) — EF Core Code First + migration.
- [learn.microsoft.com/dotnet/core/resilience/http-resilience](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience) — Microsoft.Extensions.Http.Resilience (Polly v8).
- [learn.microsoft.com/aspnet/core/fundamentals/minimal-apis](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis) — Minimal API.
- [learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services) — BackgroundService.
- [learn.microsoft.com/dotnet/standard/threading/channels](https://learn.microsoft.com/en-us/dotnet/standard/threading/channels) — `Channel<T>`.
- [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture) — .NET Clean Arch referans.
- [ardalis/CleanArchitecture](https://github.com/ardalis/CleanArchitecture) — alternatif Clean Arch.
- [ardalis/Result](https://github.com/ardalis/Result) — Result<T> pattern.
- [App-vNext/Polly](https://github.com/App-vNext/Polly) — resilience.
- [jbogard/MediatR](https://github.com/jbogard/MediatR) — CQRS request/handler.
- [FluentValidation/FluentValidation](https://github.com/FluentValidation/FluentValidation) — validation pipeline.

## Vue.js CDN (no-build)

- [vuejs.org/guide/quick-start#using-vue-from-cdn](https://vuejs.org/guide/quick-start.html#using-vue-from-cdn) — resmi CDN kullanım.
- [vuejs.org/guide/scaling-up/state-management](https://vuejs.org/guide/scaling-up/state-management.html#simple-state-management-with-reactivity-api) — reactive() ile state.
- [vuejs.org/api/reactivity-core](https://vuejs.org/api/reactivity-core.html) — Reactivity API.
- [developer.mozilla.org/Web/HTML/Element/script/type/importmap](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/script/type/importmap) — MDN importmap.
- [developer.mozilla.org/Web/API/fetch](https://developer.mozilla.org/en-US/docs/Web/API/fetch) — MDN fetch.

## Binance

- [binance-docs.github.io/apidocs/spot/en/](https://binance-docs.github.io/apidocs/spot/en/) — resmi Spot REST + WS.
- [github.com/binance/binance-spot-api-docs](https://github.com/binance/binance-spot-api-docs) — doc source.
- [github.com/binance/binance-connector-net](https://github.com/binance/binance-connector-net) — resmi .NET client referansı.
- [github.com/ccxt/ccxt](https://github.com/ccxt/ccxt) — çok-borsa referans (pattern için).
- [testnet.binance.vision](https://testnet.binance.vision) — test net.
- [binance.com/en/fee/schedule](https://www.binance.com/en/fee/schedule) — fee tablosu.

## MCP (Model Context Protocol)

- [modelcontextprotocol.io](https://modelcontextprotocol.io/) — spec + quickstart.
- [github.com/modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk) — resmi C# SDK.
- [github.com/modelcontextprotocol/servers](https://github.com/modelcontextprotocol/servers) — referans server'lar (memory, filesystem, ...).
- [github.com/microsoft/playwright-mcp](https://github.com/microsoft/playwright-mcp) — Playwright MCP (tester için).

## Security & OWASP

- [owasp.org/www-project-cheat-sheets/cheatsheets/Dotnet_Security_Cheat_Sheet](https://owasp.org/www-project-cheat-sheets/cheatsheets/Dotnet_Security_Cheat_Sheet.html)
- [cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Scripting_Prevention_Cheat_Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Scripting_Prevention_Cheat_Sheet.html)
- [learn.microsoft.com/aspnet/core/security](https://learn.microsoft.com/en-us/aspnet/core/security/)

## ADR / Architecture

- [adr.github.io/madr](https://adr.github.io/madr/) — MADR resmi.
- [github.com/joelparkerhenderson/architecture-decision-record](https://github.com/joelparkerhenderson/architecture-decision-record) — ADR örnekleri.

## Playwright

- [playwright.dev/docs](https://playwright.dev/docs/) — resmi.
- [playwright.dev/docs/writing-tests](https://playwright.dev/docs/writing-tests).

## Test

- [learn.microsoft.com/dotnet/core/testing](https://learn.microsoft.com/en-us/dotnet/core/testing/)
- [github.com/coverlet-coverage/coverlet](https://github.com/coverlet-coverage/coverlet) — coverage.

## Nasıl Güncellenir

Yeni skill/kural eklenince veya revize edilince:
1. İlgili bölüme URL ekle / düzenle.
2. Skill dosyasının altındaki "Kaynak:" satırını güncelle.
3. MCP `agent-bus.append_decision` — "REFERENCES güncellendi: <değişim>".
