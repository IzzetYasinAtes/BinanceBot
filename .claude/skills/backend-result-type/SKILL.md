---
name: backend-result-type
description: Ardalis.Result<T> pattern kullanım rehberi. Exception-for-control-flow yerine Result<T>. Error taşıma, validation error accumulation, HTTP status eşlemesi, Map/Bind chain. backend-dev agent'ının tüm handler'larda kullandığı disiplin.
---

# backend-result-type

.NET 10'da kontrol akışı için exception atma → YASAK. Yerine `Result<T>` (Ardalis.Result paketi).

## Neden?

- Exception pahalı (stack unwinding).
- Business error'u exception'la modellemek caller'ı karıştırır.
- Result explicit — caller ignore edemez.

## Temel Kullanım

```csharp
using Ardalis.Result;

public sealed class IngestKlineCommandHandler : IRequestHandler<IngestKlineCommand, Result<long>>
{
    public async Task<Result<long>> Handle(IngestKlineCommand cmd, CancellationToken ct)
    {
        if (!_symbols.Contains(cmd.Symbol))
            return Result.NotFound($"Symbol {cmd.Symbol} izinli değil.");

        var existing = await _db.Klines.FindAsync([cmd.Symbol, cmd.OpenTime], ct);
        if (existing is not null)
            return Result.Conflict("Bu kline zaten kaydedilmiş.");

        var kline = new Kline(cmd.Symbol, cmd.OpenTime, /* ... */);
        _db.Klines.Add(kline);
        await _db.SaveChangesAsync(ct);

        return Result.Success(kline.Id);
    }
}
```

## Ana Factory Metodları

| Metod | Ne zaman |
|---|---|
| `Result.Success(value)` | OK 200/201 |
| `Result.Created(value)` | 201 |
| `Result.NotFound(msg)` | 404 |
| `Result.Invalid(validationError...)` | 400 validation |
| `Result.Error(msg)` | 500 / generic |
| `Result.Conflict(msg)` | 409 |
| `Result.Unauthorized()` | 401 |
| `Result.Forbidden()` | 403 |
| `Result.Unavailable(msg)` | 503 |

## Validation Accumulation

```csharp
var errors = new List<ValidationError>();
if (cmd.Symbol.Length > 20)
    errors.Add(new ValidationError(nameof(cmd.Symbol), "Max 20 karakter"));
if (errors.Any())
    return Result<long>.Invalid(errors.ToArray());
```

**Aslında FluentValidation zaten pipeline'da bunu yapar** — handler validation yazmamalı. Handler'da kalan, domain level business check.

## Chain (Map / Bind)

```csharp
var klineResult = await _db.FindKlineResult(id, ct);
return klineResult
    .Bind(kline => kline.CloseAsResult())       // Result<decimal>
    .Map(price => new KlinePriceDto(id, price));
```

Domain aggregate'ta behavior'lar Result döndürürse chain doğrudan çalışır.

## Exception Ne Zaman?

- Programmer error (null yerine reference, impossible switch case). Hâlâ throw at, ama:
- I/O failure (DB timeout, WS disconnect) — exception at **ama catch edip Result'a dönüştür**:

```csharp
try { await _db.SaveChangesAsync(ct); }
catch (DbUpdateConcurrencyException ex)
{
    _logger.LogWarning(ex, "Concurrency conflict");
    return Result.Conflict("Kaynak başkası tarafından değiştirildi.");
}
```

## Kural

- Handler metodu ya Result<T> ya da Result döner. Başka bir şey YOK.
- Result<T>.Success'u ihmal etme — successful path da explicit.
- HTTP layer'da `ToHttpResult()` extension method ile çevir (bkz. `backend-endpoint` skill).
- Domain aggregate behavior'ları mümkünse Result<T> döndürsün (iş kuralı ihlali → Result.Invalid).

## Kaynak

- https://github.com/ardalis/Result — resmi repo + README
- https://github.com/ardalis/Result/tree/main/sample
