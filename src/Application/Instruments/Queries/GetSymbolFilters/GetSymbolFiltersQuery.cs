using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Instruments.Queries;
using BinanceBot.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Instruments.Queries.GetSymbolFilters;

public sealed record GetSymbolFiltersQuery(string Symbol) : IRequest<Result<SymbolFiltersDto>>;

public sealed class GetSymbolFiltersQueryValidator : AbstractValidator<GetSymbolFiltersQuery>
{
    public GetSymbolFiltersQueryValidator()
    {
        RuleFor(q => q.Symbol).NotEmpty();
    }
}

public sealed class GetSymbolFiltersQueryHandler
    : IRequestHandler<GetSymbolFiltersQuery, Result<SymbolFiltersDto>>
{
    private readonly IApplicationDbContext _db;

    public GetSymbolFiltersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<SymbolFiltersDto>> Handle(GetSymbolFiltersQuery request, CancellationToken ct)
    {
        var symbolVo = Symbol.From(request.Symbol);
        var instrument = await _db.Instruments
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Symbol == symbolVo, ct);

        if (instrument is null)
        {
            return Result<SymbolFiltersDto>.NotFound($"Symbol '{symbolVo}' not registered.");
        }

        return Result.Success(new SymbolFiltersDto(
            instrument.Symbol.Value,
            instrument.TickSize,
            instrument.StepSize,
            instrument.MinNotional,
            instrument.MinQty,
            instrument.MaxQty,
            instrument.Status.ToString()));
    }
}
