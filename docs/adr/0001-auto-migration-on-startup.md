# 0001. Uygulama Acilisinda Otomatik EF Core Migration

Date: 2026-04-16
Status: Accepted

## Context

BinanceBot tek kisilik (solo-dev) bir projedir. Uretim ortami da developer in makinesi / kucuk VM olacak, ayri bir operasyon ekibi yok. EF Core Code First ile yonetilen MSSQL semasinin migration lari normalde `dotnet ef database update` komutuyla elle uygulanir. Bu akis:

- CI/CD yoksa ya da minimal ise "deploy ettim ama migration unuttum" vakasi uretir.
- `ASPNETCORE_ENVIRONMENT=Production` modda `dotnet-ef` CLI zaten kurulu degil; bir de dotnet-ef global tool yonetmek gerekir.
- Kullanici talimati acik: **"manuel `dotnet ef database update` YASAK"**.

Dolayisiyla uygulama her acilista kendi semasini garanti altina almalidir.

## Decision

`src/Api/Program.cs` icinde host build sonrasi, `app.Run()` oncesinde bir **scope** acarak `AppDbContext.Database.MigrateAsync()` cagrilacak. Migration ile birlikte **idempotent seed** (sabit referans verisi — ornegin 3 Spot sembolu: BTCUSDT/ETHUSDT/BNBUSDT ve varsayilan `RiskProfile` kayitlari) ayni scope da calisir.

Akis:

1. Bootstrap logger yaz ("applying migrations...").
2. `using var scope = app.Services.CreateScope();`
3. `var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();`
4. `await db.Database.MigrateAsync(ct);` — EF Core migration lar bir transaction icinde, sirasiyla uygulanir; basarisiz olursa exception ucar.
5. Seed handler calistir: `await startupSeeder.RunAsync(db, ct);` — `EXISTS` kontrollu, idempotent.
6. Hata varsa **uygulama baslamaz**: `Environment.ExitCode = 1; return;` ve structured log.

Transactional apply: EF Core her migration i kendi transaction i icinde yurutur (provider destekliyorsa; MSSQL destekler). Bu nedenle yari yollu gen bir sema olusmaz.

Boot failure semantics:

- Connection string hatasi -> `SqlException` -> exit 1.
- Migration SQL hatasi -> `DbUpdateException` -> exit 1. Kismen uygulanmis migration EF in `__EFMigrationsHistory` tablosu sayesinde tekrar run da atlar / devam eder.
- Seed hatasi -> log + exit 1.

Migration olusturma surecinde kullanici yine `dotnet ef migrations add <name>` calistirir (dev-time). Bu yasak degildir; yasak olan **runtime da elle update**.

## Consequences

### Pozitif

- Deploy = `dotnet publish` + uygulama yeniden baslatma. Operasyonel surtunme sifir.
- Schema drift imkansiz: her boot ta sema guncel.
- Idempotent seed ile referans veri (symbols, default risk profile) garanti edilir.
- Solo-dev workflow ile hizali.

### Negatif / Tradeoff

- Birden fazla instance ile (horizontal scale) ayni anda `MigrateAsync()` cagrirsa **race** olusabilir. MSSQL `__EFMigrationsHistory` tablosunda bir row insert edilir ama EF Core in lock mekanizmasi `HistoryRepository.AcquireDatabaseLockAsync` (EF 6+) iledir; yine de multi-instance senaryoda tek bir "migration runner" instance belirlemek (env flag `RUN_MIGRATIONS=true`) best practice.
- Uzun suren migration uygulamanin baslamasini geciktirir. Kubernetes/Azure health probe larda `Startup` probe suresi arttirilmalidir.
- Geri donus zor: migration applied olduktan sonra `Remove-Migration` production da calismaz. Rollback planı: compensating migration yaz.

### Notr

- Loglama zaten yapilanin ustune. `ILogger<Program>` ile bootstrap satirlari.

## Alternatifler

1. **Elle `dotnet ef database update`** — Kullanici yasakladi. Sebep: unutma riski, tool bagimliligi.
2. **Ayri migration worker** (console app, deploy-time tetiklenen) — Multi-instance sorununu cozer ama solo-dev icin over-engineering. Ileride k8s a geciste revize edilir.
3. **DbUp / FluentMigrator** — Kod-first EF semasiyla catisir; cifte truth source.
4. **Startup da sadece `EnsureCreated()`** — Migration gecmisi tutmuyor, prod icin **YASAK**. Veri kaybi riski.

## Kaynak

- [Microsoft Docs — Applying migrations at runtime](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#apply-migrations-at-runtime)
- [EF Core `MigrateAsync` ve lock davranisi](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/history-table)
- CLAUDE.md "Altin Kurallar" — manuel EF komut yasagi
- [docs/research/binance-research.md](../research/binance-research.md) — uretim guard i konsepti (testnet-first ile hizali)
