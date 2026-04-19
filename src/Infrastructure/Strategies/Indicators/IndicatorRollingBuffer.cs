using BinanceBot.Application.Abstractions.Binance;

namespace BinanceBot.Infrastructure.Strategies.Indicators;

/// <summary>
/// ADR-0015 §15.6. Fixed-capacity, single-writer circular buffer of closed bars
/// keyed by open-time. Supports idempotent replace-on-upsert so REST backfill and
/// WS replay-on-reconnect cannot inflate the buffer by re-inserting the same bar.
///
/// Not thread-safe by itself — callers must serialise writes (the indicator
/// service routes all writes through a single <c>Channel</c> consumer loop, and
/// guards reads with its own lock).
/// </summary>
internal sealed class IndicatorRollingBuffer
{
    private readonly int _capacity;
    private readonly LinkedList<WsKlinePayload> _bars = new();

    public IndicatorRollingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        _capacity = capacity;
    }

    public int Count => _bars.Count;

    public int Capacity => _capacity;

    /// <summary>
    /// Append/replace a closed bar. Bars must arrive in non-decreasing OpenTime order
    /// (duplicates replace the existing entry; older bars are rejected silently).
    /// </summary>
    public void Upsert(WsKlinePayload bar)
    {
        if (_bars.Last is { } tail)
        {
            if (bar.OpenTime == tail.Value.OpenTime)
            {
                tail.Value = bar;
                return;
            }
            if (bar.OpenTime < tail.Value.OpenTime)
            {
                // Late/duplicate bar — scan for a match.
                var node = _bars.Last;
                while (node is not null)
                {
                    if (node.Value.OpenTime == bar.OpenTime)
                    {
                        node.Value = bar;
                        return;
                    }
                    if (node.Value.OpenTime < bar.OpenTime)
                    {
                        return; // older than any buffered bar — drop.
                    }
                    node = node.Previous;
                }
                return;
            }
        }

        _bars.AddLast(bar);
        while (_bars.Count > _capacity)
        {
            _bars.RemoveFirst();
        }
    }

    /// <summary>Materialise the buffer into a fresh read-only list (oldest-first).</summary>
    public IReadOnlyList<WsKlinePayload> Snapshot() => _bars.ToList();
}
