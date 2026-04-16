using System.Text;
using System.Text.Json;

namespace BinanceBot.AgentBus.Storage;

public enum TaskStatus { Open, InProgress, Blocked, Done }

public sealed record TaskRecord(
    string TaskId,
    TaskStatus Status,
    string? Owner,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    List<string> History);

public sealed class TaskStateStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public TaskStateStore(string traceDir)
    {
        Directory.CreateDirectory(traceDir);
        _path = Path.Combine(traceDir, "task-state.json");
        if (!File.Exists(_path))
        {
            File.WriteAllText(_path, "{}", Encoding.UTF8);
        }
    }

    public TaskRecord? Get(string taskId)
    {
        lock (_lock)
        {
            var all = Load();
            return all.TryGetValue(taskId, out var rec) ? rec : null;
        }
    }

    public (bool ok, string? conflictOwner) Claim(string agentId, string taskId)
    {
        lock (_lock)
        {
            var all = Load();
            if (all.TryGetValue(taskId, out var existing) && existing.Status == TaskStatus.InProgress && existing.Owner != agentId)
                return (false, existing.Owner);

            var now = DateTime.UtcNow;
            var history = existing?.History ?? new List<string>();
            history.Add($"{now:O} claimed by {agentId}");
            all[taskId] = new TaskRecord(taskId, TaskStatus.InProgress, agentId, existing?.CreatedUtc ?? now, now, history);
            Save(all);
            return (true, null);
        }
    }

    public void Release(string agentId, string taskId, TaskStatus newStatus = TaskStatus.Done)
    {
        lock (_lock)
        {
            var all = Load();
            if (!all.TryGetValue(taskId, out var existing)) return;
            var now = DateTime.UtcNow;
            var history = existing.History;
            history.Add($"{now:O} released by {agentId} -> {newStatus}");
            all[taskId] = existing with { Status = newStatus, Owner = null, UpdatedUtc = now, History = history };
            Save(all);
        }
    }

    private Dictionary<string, TaskRecord> Load()
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var result = JsonSerializer.Deserialize<Dictionary<string, TaskRecord>>(fs, Opts);
                return result ?? new();
            }
            catch (IOException) when (attempt < 3) { Thread.Sleep(50 * attempt); }
            catch (JsonException) { return new(); }
        }
        return new();
    }

    private void Save(Dictionary<string, TaskRecord> data)
    {
        var json = JsonSerializer.Serialize(data, Opts);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json, Encoding.UTF8);
        File.Move(tmp, _path, overwrite: true);
    }
}
