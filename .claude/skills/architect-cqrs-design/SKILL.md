---
name: architect-cqrs-design
description: CQRS (Command Query Responsibility Segregation) tasarım denetimi. Command vs Query ayrımı, handler-per-action kuralı, read-model vs write-model, MediatR request-handler pattern, validator yeri. architect agent'ın CQRS konularında kullandığı skill.
---

# architect-cqrs-design

CQRS'in yanlış uygulanması DDD'yi şişirmek değil, bozmaktır. Bu skill doğru kalıbı zorlar.

## Temel Kural

- **Command** — state değiştirir, genelde `Task<Result>` döner (void + Result).
- **Query** — state değiştirmez, DTO döner.
- Aynı handler hem command hem query DEĞİL.

## Proje Layout'u

```
src/
  Application/
    <Feature>/
      Commands/
        <Action>Command.cs                    // request record
        <Action>CommandHandler.cs             // IRequestHandler<...>
        <Action>CommandValidator.cs           // AbstractValidator<...>
      Queries/
        Get<X>Query.cs
        Get<X>QueryHandler.cs
        <X>Dto.cs                             // query response shape
```

**Feature = bir aggregate veya yakın ilişkili bir grup.** Örn: `Klines/`, `Orders/`, `MarketData/`.

## Command Design

```csharp
// Record request — immutable
public sealed record IngestKlineCommand(
    string Symbol,
    string Interval,
    long OpenTime,
    decimal Open, decimal High, decimal Low, decimal Close,
    decimal Volume
) : IRequest<Result<long>>;

// Handler — iş kuralı burada DEĞİL (Domain'de)
public sealed class IngestKlineCommandHandler : IRequestHandler<IngestKlineCommand, Result<long>>
{
    // DbContext + Domain services inject
    public async Task<Result<long>> Handle(IngestKlineCommand cmd, CancellationToken ct) { ... }
}

// Validator — kuralları burada (FluentValidation)
public sealed class IngestKlineCommandValidator : AbstractValidator<IngestKlineCommand>
{
    public IngestKlineCommandValidator()
    {
        RuleFor(x => x.Symbol).NotEmpty().Length(3, 20);
        RuleFor(x => x.OpenTime).GreaterThan(0);
        // ...
    }
}
```

## Query Design

```csharp
public sealed record GetKlineSeriesQuery(string Symbol, string Interval, int Limit)
    : IRequest<Result<IReadOnlyList<KlineDto>>>;

// Query handler read-model'e gider; domain aggregate'ını YÜKLEMEZ
public sealed class GetKlineSeriesQueryHandler : IRequestHandler<...>
{
    // DbContext ile AsNoTracking + Projection
}
```

## Checklist (architect denetimi)

- [ ] Command `Task<Result<T>>` veya `Task<Result>` döner, plain value YOK.
- [ ] Command handler Domain aggregate'larını kullanıyor — DB direct access YOK (ORM evet, ama domain kuralları Domain'de).
- [ ] Query handler `AsNoTracking()` + projection kullanıyor mu? (read-only path)
- [ ] Validator var mı? Handler'da validation kodu var mı? (olmamalı — validator'da)
- [ ] Handler tek bir şey yapıyor mu? (Single Responsibility — "save order + send email + notify telegram" → event-driven böl)
- [ ] `MediatR.Send()` yerine handler doğrudan çağrılmış mı? (YASAK — her zaman `ISender`/`IMediator` üzerinden)

## Command/Query Ayrım Trap'leri

- "GetOrCreate" metodu → iki tane ayır: `GetXQuery` + `CreateXCommand`.
- "Idempotent POST" → hala command; idempotency key ayrı mesele.
- Query tarafında cache gerekirse handler içinde; command tarafında cache invalidation event ile.

## Skill'den Çıktı

- Tasarım öneri dokümanı (kısa, gerektirirse ADR bağla)
- Command/Query layout — dizin ağacı
- Validator kuralları listesi
- Read-model farklı mı? Ayrı DbContext/view?

## Kural

- Command handler'dan başka handler `ISender.Send()` ÇAĞIRMASIN. Composition = event-driven.
- Query handler'da write YASAK.
- Validator side-effect yapmaz — sadece kural ifade eder.

## Kaynak

- https://github.com/jasontaylordev/CleanArchitecture — referans proje
- https://github.com/jbogard/MediatR — MediatR repo
- https://github.com/FluentValidation/FluentValidation
