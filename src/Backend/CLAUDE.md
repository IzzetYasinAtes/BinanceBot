# src/Backend/ — CLAUDE.md

Bu dosya `src/Backend/**` altındaki herhangi bir dosya okunduğunda otomatik yüklenir. Backend-dev agent'ı buraya tabidir.

## Solution Layout

```
src/Backend/
├── BinanceBot.sln
├── Domain/                       # entity, VO, domain event, aggregate root
│   └── <Aggregate>/
├── Application/                  # use case handler'ları
│   └── <Feature>/
│       ├── Commands/
│       └── Queries/
├── Infrastructure/               # DbContext, Binance client, Polly, Migrations
│   ├── Persistence/
│   │   ├── ApplicationDbContext.cs
│   │   ├── Configurations/       # IEntityTypeConfiguration<T>
│   │   └── Migrations/           # EF CLI buraya üretir
│   └── MarketData/               # Binance REST/WS client + supervisor
├── Api/                          # ASP.NET Core host
│   ├── Program.cs
│   └── Endpoints/
└── Tests/
    ├── Domain.Tests/
    ├── Application.Tests/
    └── Api.IntegrationTests/
```

## Dependency Rule (Clean Architecture)

```
Domain ←── Application ←── Infrastructure
               ↑                  ↑
               └─── Api (composition root) ───┘
```

- **Domain** hiçbir şey import etmez. Saf C# + BCL.
- **Application** sadece Domain'i referans verir. Interface'ler `Application` altında (örn. `IBinanceClient`), implementation Infrastructure'da.
- **Infrastructure** Application + Domain'i referans verir, dış dünyayı gerçekler.
- **Api** hepsini wire'lar — composition root.

**Yön ihlali ⇒ reject.**

## Zorunlu NuGet Paketleri

- `MediatR` — CQRS request/handler
- `FluentValidation.AspNetCore` veya `FluentValidation` + `FluentValidation.DependencyInjectionExtensions`
- `Ardalis.Result` — Result<T>
- `Microsoft.EntityFrameworkCore.SqlServer` + `...Tools`
- `Microsoft.Extensions.Http.Resilience` (Polly v8 entegre)

## Kurallar

1. **CQRS her zaman** — Command ve Query ayrı handler.
2. **Result<T> dön** — exception-for-flow yasak.
3. **EF lazy loading yasak** — `UseLazyLoadingProxies()` eklenmeyecek.
4. **AsNoTracking()** read path'te zorunlu.
5. **Repository-per-aggregate-root** — repository-per-entity yasak.
6. **Anemic model yasak** — iş kuralları Domain'de.
7. **IHttpClientFactory + Polly** — `new HttpClient()` yasak.
8. **ILogger<T> structured logging** — string concat yasak (`LogInformation("...{X}...", x)` kullan).
9. **async/await + CancellationToken** — `.Result` / `.Wait()` / `async void` yasak.
10. **Secret appsettings'te değil** — User Secrets (dev) + env var (prod).

## Binance-Spesifik

- Single `HttpClientFactory` named client: `"binance-rest"`.
- WebSocket supervisor: `BackgroundService` + `Channel<T>`.
- Reconnect: exponential backoff + jitter, max 30s cap.
- Subscribe replay zorunlu (reviewer-ws-resiliency skill ile denetlenir).
- Rate limit header'ları (`X-MBX-USED-WEIGHT-1M`) DelegatingHandler'da log.

## Entity Framework

- Code First — **Database First YASAK.**
- Migration dosyaları `Infrastructure/Persistence/Migrations/` içinde.
- Yeni migration: `dotnet ef migrations add <Name> --project Infrastructure --startup-project Api`.
- Table isimlendirme pluralized English (`Klines`, `Orders`).
- Column isimlendirme PascalCase — SQL server convention.
- Soft delete pattern **yasak** (ihtiyaç varsa ADR yazılıp explicit).

## Test

- Domain tests = pure, no mock, sadece domain logic.
- Application tests = MediatR handler + Moq/FakeItEasy (interface'leri mock).
- Api integration tests = `WebApplicationFactory<T>` + InMemory DB veya Testcontainers MSSQL.
- Binance WS tests = `testnet.binance.vision` üzerinden smoke, veya mock WS server.

## Proje Oluşturma (referans)

```bash
dotnet new sln -n BinanceBot
dotnet new classlib -n BinanceBot.Domain -f net10.0 -o Domain
dotnet new classlib -n BinanceBot.Application -f net10.0 -o Application
dotnet new classlib -n BinanceBot.Infrastructure -f net10.0 -o Infrastructure
dotnet new webapi -n BinanceBot.Api -f net10.0 -o Api --use-minimal-apis
# sln'e ekle ve project reference'ları ayarla
```

## Yasaklar

- `new HttpClient()` / `HttpClient` static
- Database-First (scaffold-dbcontext)
- Repository-per-entity (`IGenericRepository<T>`)
- Anemic domain (`public set;` ile dışa açık entity)
- `Task.Result` / `.Wait()` / `async void`
- Magic string/number
- `throw new Exception(...)` kontrol akışı için
