---
name: backend-ef-migration
description: EF Core Code First migration ekleme rehberi. Migration isimlendirme, Fluent API yapılandırma (Infrastructure/Persistence/Configurations/), lazy loading yasağı, migration dosyası konumu (Infrastructure projede), dotnet ef CLI komutları. backend-dev agent'ının migration yazarken kullandığı skill.
---

# backend-ef-migration

Code First migration'ı projeye doğru yerde eklemek için checklist.

## Proje Yapısı

```
src/
  Domain/                 # entity'ler + VO'lar (EF attribute YOK)
  Application/            # handler'lar
  Infrastructure/
    Persistence/
      ApplicationDbContext.cs
      Configurations/
        <Entity>Configuration.cs    # IEntityTypeConfiguration
      Migrations/                    # EF CLI buraya üretir
    InfrastructureServiceCollection.cs
  Api/
    Program.cs
```

## Yeni Migration Sırası

1. **Entity'yi Domain'e ekle/güncelle** (EF attribute'u KULLANMA — fluent API Infrastructure'da).
2. **Fluent configuration** yaz: `Infrastructure/Persistence/Configurations/<Entity>Configuration.cs`

```csharp
using BinanceBot.Domain.<Aggregate>;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinanceBot.Infrastructure.Persistence.Configurations;

public sealed class <Entity>Configuration : IEntityTypeConfiguration<<Entity>>
{
    public void Configure(EntityTypeBuilder<<Entity>> builder)
    {
        builder.ToTable("<Entities>");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Symbol)
               .HasMaxLength(20)
               .IsRequired();

        builder.HasIndex(x => new { x.Symbol, x.OpenTime })
               .IsUnique();

        // Value Object owned
        // builder.OwnsOne(x => x.Price, pb => { pb.Property(p => p.Amount).HasColumnName("Price"); });

        // Navigation relationship
        // builder.HasMany(x => x.Children).WithOne().HasForeignKey(y => y.ParentId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

3. **DbContext'e DbSet ekle:**

```csharp
public DbSet<<Entity>> <Entities> => Set<<Entity>>();

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    base.OnModelCreating(modelBuilder);
}
```

4. **Migration ekle:**

```bash
cd src
dotnet ef migrations add Add<Action> --project Infrastructure --startup-project Api --context ApplicationDbContext
```

Migration adı: `Add<X>`, `Update<X>`, `Remove<X>`, `Rename<X>Column` formatında.

5. **Migration dosyasını incele** — generate edilmiş kod mantıklı mı?
6. **Database update:**

```bash
dotnet ef database update --project Infrastructure --startup-project Api --context ApplicationDbContext
```

## Kurallar

- **Lazy loading yasak** — `UseLazyLoadingProxies()` KULLANMA. Include veya projection.
- **Change Tracker** write path'te açık, read path'te `AsNoTracking()`.
- **Owned types** value object için (Money, Price, Symbol).
- **OnDelete** stratejisi explicit yaz — default Restrict, gerekirse Cascade.
- **Index** performans kritik query'ler için zorunlu (özellikle symbol + time).

## Rollback

```bash
dotnet ef migrations remove --project Infrastructure --startup-project Api
# veya önceki migration'a dön
dotnet ef database update <PreviousMigrationName> --project Infrastructure --startup-project Api
```

**Prod'da rollback nadiren güvenli** — data loss riski; her zaman "forward migration" ile düzelt.

## Seed Data

- Runtime seed: `Program.cs`'te `db.Database.Migrate()` + conditional seed method.
- Migration'da seed: `migrationBuilder.InsertData(...)` — küçük referans tabloları için.

## Kaynak

- https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/
- https://github.com/jasontaylordev/CleanArchitecture/tree/main/src/Infrastructure/Data
