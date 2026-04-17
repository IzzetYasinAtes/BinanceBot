using BinanceBot.Domain.Common;
using BinanceBot.Domain.Strategies.Events;
using BinanceBot.Domain.ValueObjects;

namespace BinanceBot.Domain.Strategies;

public sealed class Strategy : AggregateRoot<long>
{
    private readonly List<StrategySignal> _signals = [];

    public string Name { get; private set; } = default!;
    public StrategyType Type { get; private set; }
    public StrategyStatus Status { get; private set; }
    public string SymbolsCsv { get; private set; } = default!;
    public string ParametersJson { get; private set; } = "{}";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }

    public IReadOnlyCollection<StrategySignal> Signals => _signals.AsReadOnly();

    private Strategy() { }

    public static Strategy Create(
        string name,
        StrategyType type,
        IReadOnlyCollection<Symbol> symbols,
        string parametersJson,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Name required.");
        }
        if (symbols.Count == 0)
        {
            throw new DomainException("At least one symbol required.");
        }

        var strategy = new Strategy
        {
            Name = name.Trim(),
            Type = type,
            Status = StrategyStatus.Draft,
            SymbolsCsv = string.Join(",", symbols.Select(s => s.Value)),
            ParametersJson = string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson,
            CreatedAt = now,
            UpdatedAt = now,
        };

        strategy.RaiseDomainEvent(new StrategyCreatedEvent(strategy.Id, strategy.Name, type));
        return strategy;
    }

    public void Activate(DateTimeOffset now)
    {
        if (Status == StrategyStatus.Active)
        {
            return;
        }
        if (Status is not StrategyStatus.Draft and not StrategyStatus.Paused)
        {
            throw new DomainException($"Cannot activate from {Status}.");
        }
        Status = StrategyStatus.Active;
        ActivatedAt = now;
        UpdatedAt = now;
        RaiseDomainEvent(new StrategyActivatedEvent(Id));
    }

    public void Deactivate(string reason, DateTimeOffset now)
    {
        if (Status != StrategyStatus.Active)
        {
            throw new DomainException("Only Active strategy can be deactivated.");
        }
        Status = StrategyStatus.Paused;
        UpdatedAt = now;
        RaiseDomainEvent(new StrategyDeactivatedEvent(Id, reason));
    }

    public void UpdateParameters(string parametersJson, DateTimeOffset now)
    {
        if (Status == StrategyStatus.Active)
        {
            throw new DomainException("Cannot update parameters while Active.");
        }
        ParametersJson = string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson;
        UpdatedAt = now;
        RaiseDomainEvent(new StrategyParametersUpdatedEvent(Id));
    }

    public StrategySignal EmitSignal(
        Symbol symbol,
        DateTimeOffset barOpenTime,
        StrategySignalDirection direction,
        decimal quantity,
        decimal? price,
        decimal? stopPrice,
        string contextJson,
        DateTimeOffset emittedAt)
    {
        if (Status != StrategyStatus.Active)
        {
            throw new DomainException("Signals can only be emitted while Active.");
        }

        var existing = _signals.FirstOrDefault(s =>
            s.Symbol == symbol && s.BarOpenTime == barOpenTime);
        if (existing is not null)
        {
            return existing;
        }

        var signal = StrategySignal.Emit(
            symbol, barOpenTime, direction, quantity,
            price, stopPrice, contextJson, emittedAt);
        _signals.Add(signal);

        RaiseDomainEvent(new StrategySignalEmittedEvent(Id, symbol.Value, direction, barOpenTime));
        return signal;
    }
}
