using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Instruments.Commands.HaltSymbol;

public sealed record HaltSymbolCommand(string Symbol, string Reason) : IRequest<Result>;

public sealed class HaltSymbolCommandValidator : AbstractValidator<HaltSymbolCommand>
{
    public HaltSymbolCommandValidator()
    {
        RuleFor(c => c.Symbol).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(200);
    }
}

public sealed class HaltSymbolCommandHandler : IRequestHandler<HaltSymbolCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public HaltSymbolCommandHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result> Handle(HaltSymbolCommand request, CancellationToken ct)
    {
        var vo = Symbol.From(request.Symbol);
        var instrument = await _db.Instruments.FirstOrDefaultAsync(i => i.Symbol == vo, ct);
        if (instrument is null) return Result.NotFound($"Instrument {vo} not found.");

        instrument.UpdateFilters(
            instrument.TickSize, instrument.StepSize, instrument.MinNotional,
            instrument.MinQty, instrument.MaxQty,
            InstrumentStatus.Halt, _clock.UtcNow);

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
