namespace BinanceBot.Application.Abstractions;

public interface ICorrelationIdAccessor
{
    Guid CorrelationId { get; }
}
