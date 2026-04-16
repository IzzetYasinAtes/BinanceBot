# BinanceBot — Root CLAUDE.md

Bu dosya **her session başında otomatik yüklenir**. Projenin altın kuralları burada.

## Proje

- **Ne:** Binance üzerinden BTC/ETH/BNB izleyen, ilerde otomatik trade edebilen backend + UI.
- **Backend:** .NET 10 + ASP.NET Core Web API + EF Core Code First + MSSQL
- **Mimari:** DDD + Clean Architecture + CQRS (MediatR) + SOLID + DRY + KISS
- **Frontend:** Vue 3 CDN + importmap, **npm yok, bundler yok, SFC yok**
- **Binance:** WebSocket + REST hibrit; reconnect/heartbeat/replay kritik
- **Workspace altyapısı:** 7 agent + 30 skill + 3 MCP server (agent-bus .NET özel + playwright + memory) + hook loglama

## Dil

- Kullanıcı ile konuşma dili: **Türkçe**
- Kod identifier'ları, frontmatter, dosya isimleri, branch/commit: **İngilizce**
- Skill/agent gövdeleri Türkçe; "Kaynak:" satırında orijinal link (İngilizce)

## Altın Kurallar

1. **SOLID, DRY, KISS** — her kod kararında bu üçü filtredir.
2. **Secret commit yasak** — `appsettings.json` sadece template; gerçek bağlantı string'i `.env`/user-secrets'ta.
3. **npm yasak (frontend)** — Vue ve kütüphaneler CDN + importmap üzerinden. Dev/test harness için npx (Playwright MCP) izinli.
4. **Lazy loading yasak (EF Core)** — explicit `Include()` veya CQRS read-model.
5. **Throwing for control flow yasak** — `Result<T>` pattern (ardalis/Result).
6. **Repository-per-entity yasak** — aggregate root + `DbContext` yeterli; DDD kuralı.
7. **Her agent karar verince `agent-bus` MCP `append_decision` çağırır.** Log kontratı.
8. **Kripto/trade/WS ile ilgili her iş, başlamadan önce `binance-expert` agent'ına danışılır.** PM'in sorumluluğu.
9. **Feature "done" denmeden `tester` agent'ı Playwright ile gezmek zorunda.** Reviewer bu olmadan "ready" onayı vermez.

## PM Protokolü (kullanıcı ile konuşan tek agent)

- **Küçük-parça kuralı:** PM, >5 adımlık iş delege etmez. Her chunk bir subagent çağrısı veya tek iş paketi.
- **Checkpoint:** her chunk sonunda PM şu formatta özet verir:
  ```
  ✔ Ne yaptım: ...
  → Sırada: ...
  Devam / /status / /tell-pm <not> / dur?
  ```
- **User-notes:** PM her checkpoint'te otomatik olarak `pm-read-user-notes` skill'ini çalıştırır, yeni not varsa plana entegre eder.
- **Handoff:** PM her subagent çağrısından önce `pm-handoff` skill'ini kullanır → MCP `append_handoff`.
- **Escape/resume:** kullanıcı Escape basarsa PM durur; tekrar çağrıldığında `.ai-trace/` + MCP `get_task_state`'ten resume.

## Escalation Matrisi

| Durum | Kim |
|---|---|
| Yeni aggregate, cross-layer tasarım, mimari karar | `architect` |
| .NET 10 / EF Core / MediatR kodu | `backend-dev` |
| Vue CDN sayfası, importmap | `frontend-dev` |
| Kripto/Binance/WS/trading-strategy sorusu | `binance-expert` (zorunlu danışman) |
| PR öncesi kural kontrolü / security scan | `reviewer` |
| UI/DB/API end-to-end test | `tester` (Playwright ile ekranda gezer) |

## Logging Kontratı

Her agent turn'unun sonunda `agent-bus` MCP'ye **en az bir `append_decision`** düşer. Kural:
- `agent_id` = kendi agent adı
- `task_id` = PM'den gelen handoff task_id'si (yoksa "adhoc")
- `decision` = Tek cümlelik TR özet
- `rationale` = Neden (kısa paragraf)

Hook'lar ayrıca `subagent-stop` → `.ai-trace/subagent-stops/*.md` yazar (çift güvence).

## İmplementasyon Sırası Kuralı

Kullanıcı "X yap" dediğinde default zincir: **PM → architect (gerekirse) → backend-dev/frontend-dev → tester → reviewer → PM özet → kullanıcı**.

## Yasaklar (Hızlı Liste)

- npm/bundler (frontend prod)
- Lazy loading (EF)
- Exception for control flow
- Repository-per-entity
- Database-First (EF)
- Anemic domain model
- `<script setup>` / SFC (Vue)
- Pinia (frontend state)
- Agent Teams (experimental — logging kontratı kırar)
- MCP dışında agent-bus yazımı (her şey MCP'ye)
