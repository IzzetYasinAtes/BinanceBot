using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.System.Queries;
using BinanceBot.Domain.SystemEvents;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.System.Queries.TailSystemEvents;

public sealed record TailSystemEventsQuery(
    long? Since,
    string? Level,
    int Limit) : IRequest<Result<SystemEventTailDto>>;

public sealed class TailSystemEventsQueryValidator : AbstractValidator<TailSystemEventsQuery>
{
    public TailSystemEventsQueryValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, 500);
        RuleFor(q => q.Level)
            .Must(l => l is null || Enum.TryParse<SystemEventSeverity>(l, true, out _))
            .WithMessage("Level must be Info/Warning/Error/Critical or null.");
    }
}

public sealed class TailSystemEventsQueryHandler
    : IRequestHandler<TailSystemEventsQuery, Result<SystemEventTailDto>>
{
    private readonly IApplicationDbContext _db;

    public TailSystemEventsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<SystemEventTailDto>> Handle(TailSystemEventsQuery request, CancellationToken ct)
    {
        var query = _db.SystemEvents.AsNoTracking().AsQueryable();

        if (request.Since is not null)
        {
            query = query.Where(e => e.Id > request.Since);
        }

        if (!string.IsNullOrWhiteSpace(request.Level)
            && Enum.TryParse<SystemEventSeverity>(request.Level, true, out var sev))
        {
            query = query.Where(e => e.Severity == sev);
        }

        var rows = await query
            .OrderByDescending(e => e.Id)
            .Take(request.Limit)
            .ToListAsync(ct);

        var items = rows.Select(e => new SystemEventRowDto(
            e.Id,
            e.EventType,
            e.Severity.ToString(),
            e.Source,
            e.CorrelationId,
            e.OccurredAt,
            e.PayloadJson)).ToList();

        var cursor = rows.Count > 0 ? rows.Max(e => e.Id) : request.Since ?? 0L;
        return Result.Success(new SystemEventTailDto(cursor, items));
    }
}
