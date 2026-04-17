using BinanceBot.Application.System.Queries.GetSystemStatus;
using BinanceBot.Infrastructure.Binance;
using BinanceBot.Infrastructure.Binance.Streams;
using BinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Monitoring;

public sealed class SystemStatusProvider : ISystemStatusProvider
{
    private readonly BinanceWsSupervisor _supervisor;
    private readonly IOptionsMonitor<BinanceOptions> _options;
    private readonly ApplicationDbContext _db;

    public SystemStatusProvider(
        BinanceWsSupervisor supervisor,
        IOptionsMonitor<BinanceOptions> options,
        ApplicationDbContext db)
    {
        _supervisor = supervisor;
        _options = options;
        _db = db;
    }

    public string WsState => _supervisor.State.ToString();

    public bool TestnetOnly => !_options.CurrentValue.AllowMainnet;

    public async Task<(bool Up, string[] Pending)> GetDatabaseStateAsync(CancellationToken ct)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            var pending = await _db.Database.GetPendingMigrationsAsync(ct);
            return (canConnect, pending.ToArray());
        }
        catch
        {
            return (false, []);
        }
    }
}
