---
name: backend-cqrs-trio
description: MediatR + FluentValidation ile Command+Handler+Validator üçlüsü (veya Query trio) iskeleti üretir. Proje layout'una (Application/<Feature>/Commands|Queries/) uyar. backend-dev agent'ının kod üretirken kullandığı skill.
---

# backend-cqrs-trio

Her yeni command/query için deterministic iskelet. Kural: 3 dosya, tek feature klasöründe.

## Command Trio

Dosyalar: `src/Application/<Feature>/Commands/<Action>Command.cs`, `<Action>CommandHandler.cs`, `<Action>CommandValidator.cs`.

```csharp
// <Action>Command.cs
using MediatR;
using Ardalis.Result;

namespace BinanceBot.Application.<Feature>.Commands;

public sealed record <Action>Command(
    // inputs
) : IRequest<Result<<ReturnType>>>;
```

```csharp
// <Action>CommandHandler.cs
using MediatR;
using Ardalis.Result;
using Microsoft.Extensions.Logging;
using BinanceBot.Domain.<Aggregate>;

namespace BinanceBot.Application.<Feature>.Commands;

public sealed class <Action>CommandHandler : IRequestHandler<<Action>Command, Result<<ReturnType>>>
{
    private readonly I<Aggregate>Repository _repo;
    private readonly ILogger<<Action>CommandHandler> _logger;

    public <Action>CommandHandler(I<Aggregate>Repository repo, ILogger<<Action>CommandHandler> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<Result<<ReturnType>>> Handle(<Action>Command request, CancellationToken ct)
    {
        // 1) Domain aggregate'ı yükle veya oluştur
        // 2) İş kuralını domain'e delege et
        // 3) Kaydet + domain event publish (outbox varsa)
        // 4) Result.Success(...) ya da Result.Error(...)
    }
}
```

```csharp
// <Action>CommandValidator.cs
using FluentValidation;

namespace BinanceBot.Application.<Feature>.Commands;

public sealed class <Action>CommandValidator : AbstractValidator<<Action>Command>
{
    public <Action>CommandValidator()
    {
        // RuleFor(x => x.Field).NotEmpty().Length(3, 50);
    }
}
```

## Query Trio

Dosyalar: `src/Application/<Feature>/Queries/Get<X>Query.cs`, `Get<X>QueryHandler.cs`, `<X>Dto.cs`.

```csharp
// <X>Dto.cs
namespace BinanceBot.Application.<Feature>.Queries;

public sealed record <X>Dto(
    // read-model fields
);
```

```csharp
// Get<X>Query.cs
using MediatR;
using Ardalis.Result;

namespace BinanceBot.Application.<Feature>.Queries;

public sealed record Get<X>Query(/* filters */) : IRequest<Result<IReadOnlyList<<X>Dto>>>;
```

```csharp
// Get<X>QueryHandler.cs
using MediatR;
using Ardalis.Result;
using Microsoft.EntityFrameworkCore;
using BinanceBot.Infrastructure;

namespace BinanceBot.Application.<Feature>.Queries;

public sealed class Get<X>QueryHandler : IRequestHandler<Get<X>Query, Result<IReadOnlyList<<X>Dto>>>
{
    private readonly ApplicationDbContext _db;

    public Get<X>QueryHandler(ApplicationDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<<X>Dto>>> Handle(Get<X>Query request, CancellationToken ct)
    {
        var items = await _db.<DbSet>
            .AsNoTracking()
            .Where(/* filter */)
            .Select(x => new <X>Dto(/* projection */))
            .ToListAsync(ct);
        return Result.Success((IReadOnlyList<<X>Dto>)items);
    }
}
```

## Kural

- Command = write, Query = read. Karıştırma.
- Query handler `AsNoTracking()` + `Select` projection — full entity yükleme yasak.
- Validator yoksa `MediatR.Extensions.FluentValidation` pipeline 400 döner — bu beklenen.
- Handler'da iş kuralı YASAK — domain aggregate behavior metoduna delege et.

## Kaynak

- https://github.com/jasontaylordev/CleanArchitecture/tree/main/src/Application
- https://github.com/jbogard/MediatR
- https://github.com/FluentValidation/FluentValidation
