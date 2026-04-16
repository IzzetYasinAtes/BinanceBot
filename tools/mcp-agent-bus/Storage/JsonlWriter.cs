using System.Text;
using System.Text.Json;

namespace BinanceBot.AgentBus.Storage;

/// <summary>
/// Append-only JSONL writer. Dosya kilidi ile concurrent yazımı güvenli tutar.
/// </summary>
public sealed class JsonlWriter
{
    private readonly string _traceDir;
    private readonly object _lock = new();
    private long _seq;

    public JsonlWriter(string traceDir)
    {
        _traceDir = traceDir;
        Directory.CreateDirectory(traceDir);
        _seq = InitSeqFromDisk();
    }

    public long Append(string relativeFile, IDictionary<string, object?> payload)
    {
        lock (_lock)
        {
            var nextSeq = Interlocked.Increment(ref _seq);
            payload["seq"] = nextSeq;
            payload["ts"] = DateTime.UtcNow.ToString("O");

            var json = JsonSerializer.Serialize(payload);
            var path = Path.Combine(_traceDir, relativeFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            AppendWithRetry(path, json + "\n");
            return nextSeq;
        }
    }

    public IReadOnlyList<Dictionary<string, JsonElement>> Read(string relativeFile, int lastN = int.MaxValue, long sinceSeq = 0)
    {
        var path = Path.Combine(_traceDir, relativeFile);
        if (!File.Exists(path)) return Array.Empty<Dictionary<string, JsonElement>>();

        var lines = ReadAllLinesWithRetry(path);
        var result = new List<Dictionary<string, JsonElement>>(Math.Min(lines.Length, lastN));
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            Dictionary<string, JsonElement>? rec;
            try { rec = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line); }
            catch { continue; }
            if (rec is null) continue;
            if (sinceSeq > 0 && rec.TryGetValue("seq", out var s) && s.TryGetInt64(out var si) && si <= sinceSeq) continue;
            result.Add(rec);
        }
        if (result.Count > lastN) result = result.GetRange(result.Count - lastN, lastN);
        return result;
    }

    private static void AppendWithRetry(string path, string content)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, FileOptions.WriteThrough);
                var bytes = Encoding.UTF8.GetBytes(content);
                fs.Write(bytes);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private static string[] ReadAllLinesWithRetry(string path)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                var text = reader.ReadToEnd();
                return text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }
        return Array.Empty<string>();
    }

    private long InitSeqFromDisk()
    {
        long max = 0;
        foreach (var file in new[] { "decisions.jsonl", "handoffs.jsonl", "user-notes.jsonl" })
        {
            var path = Path.Combine(_traceDir, file);
            if (!File.Exists(path)) continue;
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var rec = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line);
                    if (rec is not null && rec.TryGetValue("seq", out var s) && s.TryGetInt64(out var si) && si > max) max = si;
                }
                catch { /* ignore bad lines */ }
            }
        }
        return max;
    }
}
