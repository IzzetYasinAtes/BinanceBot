---
name: backend-endpoint
description: ASP.NET Core endpoint üretim rehberi — Minimal API (tercih) veya Controller. Request DTO → MediatR → Result<T> → HTTP status eşlemesi. Validation pipeline otomatik. Versioning, route organization, OpenAPI. backend-dev agent'ının endpoint yazarken kullandığı skill.
---

# backend-endpoint

Yeni endpoint eklemenin tutarlı yolu. Tercih: **Minimal API** (ASP.NET Core 10 style).

## Layout

```
src/Backend/Api/
  Program.cs
  Endpoints/
    <Feature>Endpoints.cs           # IEndpointRouteBuilder extension
  DependencyInjection.cs            # Endpoint groups
```

## Minimal API Şablonu

```csharp
// src/Backend/Api/Endpoints/KlineEndpoints.cs
using MediatR;
using BinanceBot.Application.Klines.Commands;
using BinanceBot.Application.Klines.Queries;

namespace BinanceBot.Api.Endpoints;

public static class KlineEndpoints
{
    public static IEndpointRouteBuilder MapKlineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/klines").WithTags("Klines");

        group.MapGet("/", GetKlineSeries)
             .WithName("GetKlineSeries")
             .WithOpenApi();

        group.MapPost("/", IngestKline)
             .WithName("IngestKline")
             .WithOpenApi();

        return app;
    }

    private static async Task<IResult> GetKlineSeries(
        [AsParameters] GetKlineSeriesQuery query,
        ISender mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(query, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> IngestKline(
        IngestKlineCommand cmd,
        ISender mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(cmd, ct);
        return result.ToHttpResult();
    }
}
```

## Result → HTTP Eşlemesi

Extension method (Api projesine bir kez ekle):

```csharp
// src/Backend/Api/ResultExtensions.cs
using Ardalis.Result;

namespace BinanceBot.Api;

public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> r) => r.Status switch
    {
        ResultStatus.Ok       => Results.Ok(r.Value),
        ResultStatus.Created  => Results.Created(string.Empty, r.Value),
        ResultStatus.NotFound => Results.NotFound(r.Errors),
        ResultStatus.Invalid  => Results.ValidationProblem(r.ValidationErrors.ToDictionary(e => e.Identifier, e => new[] { e.ErrorMessage })),
        ResultStatus.Unauthorized => Results.Unauthorized(),
        ResultStatus.Forbidden    => Results.Forbid(),
        ResultStatus.Conflict     => Results.Conflict(r.Errors),
        _ => Results.Problem(string.Join("; ", r.Errors))
    };

    public static IResult ToHttpResult(this Result r) => r.Status switch
    {
        ResultStatus.Ok       => Results.NoContent(),
        ResultStatus.NotFound => Results.NotFound(r.Errors),
        ResultStatus.Invalid  => Results.ValidationProblem(r.ValidationErrors.ToDictionary(e => e.Identifier, e => new[] { e.ErrorMessage })),
        _ => Results.Problem(string.Join("; ", r.Errors))
    };
}
```

## Program.cs Kısmı

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()         // MediatR + FluentValidation pipeline
    .AddInfrastructure(builder.Configuration)   // DbContext + HttpClient + Polly
    .AddEndpointsApiExplorer()
    .AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.MapKlineEndpoints();
// app.MapOrderEndpoints();
app.MapOpenApi();

app.Run();
```

## Kural

- Controller'lar sadece legacy / cookie auth gibi spesifik ihtiyaçlarda. Default minimal API.
- Endpoint method'u sadece mediator.Send ve result mapping yapar — iş kuralı YOK.
- Request DTO yoksa `[AsParameters]` query binding; JSON body için record command doğrudan.
- OpenAPI metadata (`.WithName`, `.WithOpenApi`, `.Produces<T>`) eklemek zorunlu.
- Auth: `.RequireAuthorization()` explicit; varsayılan anonymous olmasın — `Program.cs`'te `app.MapGroup("/api").RequireAuthorization()`.

## Versioning

İhtiyaç olunca `Asp.Versioning.Http` paketi; v1/v2 ayrı MapGroup.

## Kaynak

- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis
- https://github.com/ardalis/Result/blob/main/README.md
