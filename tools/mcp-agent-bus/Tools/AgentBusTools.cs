using System.ComponentModel;
using System.Text.Json;
using BinanceBot.AgentBus.Storage;
using ModelContextProtocol.Server;

namespace BinanceBot.AgentBus.Tools;

/// <summary>
/// BinanceBot agent-bus MCP tool'ları. Tüm agent'lar buraya yazar; kullanıcı notları da burada kuyrukta.
/// </summary>
[McpServerToolType]
public sealed class AgentBusTools
{
    private const string DecisionsFile = "decisions.jsonl";
    private const string HandoffsFile = "handoffs.jsonl";
    private const string UserNotesFile = "user-notes.jsonl";

    [McpServerTool(Name = "ping"), Description("Sağlık kontrolü. Server çalışıyor mu, versiyon ne?")]
    public static object Ping() => new
    {
        ok = true,
        server = "mcp-agent-bus",
        version = "1.0.0",
        ts = DateTime.UtcNow.ToString("O")
    };

    [McpServerTool(Name = "append_decision"), Description("Bir agent'ın aldığı kararı audit log'a yazar (decisions.jsonl). SIRAYLA çağır: karar, sonra açıklama.")]
    public static object AppendDecision(
        JsonlWriter writer,
        [Description("Karar alan agent'ın kimliği (pm, architect, backend-dev, ...)")] string agent_id,
        [Description("İlgili task id'si (yoksa 'adhoc')")] string task_id,
        [Description("Alınan karar — tek cümle Türkçe")] string decision,
        [Description("Kararın gerekçesi — kısa paragraf")] string? rationale = null,
        [Description("Ek metadata, JSON string formatında (opsiyonel)")] string? metadata_json = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["kind"] = "decision",
            ["agent"] = agent_id,
            ["task_id"] = task_id,
            ["decision"] = decision,
            ["rationale"] = rationale,
            ["metadata"] = ParseJsonOrNull(metadata_json)
        };
        var seq = writer.Append(DecisionsFile, payload);
        return new { ok = true, seq };
    }

    [McpServerTool(Name = "append_handoff"), Description("PM'den subagent'a (veya agent'tan agent'a) yapılan görev devrini handoffs.jsonl'a yazar.")]
    public static object AppendHandoff(
        JsonlWriter writer,
        [Description("Devir eden agent (genelde 'pm')")] string from_agent,
        [Description("Devir alan agent (architect, backend-dev, ...)")] string to_agent,
        [Description("Devredilen task id'si")] string task_id,
        [Description("Görev kapsamı — TR özet")] string scope,
        [Description("Bitiş tanımı — ne yapılınca 'done'")] string done_definition,
        [Description("Dokunulması yasak olan path/dosyalar (comma-separated)")] string? forbidden_paths = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["kind"] = "handoff",
            ["from"] = from_agent,
            ["to"] = to_agent,
            ["task_id"] = task_id,
            ["scope"] = scope,
            ["done_definition"] = done_definition,
            ["forbidden_paths"] = forbidden_paths
        };
        var seq = writer.Append(HandoffsFile, payload);
        return new { ok = true, seq };
    }

    [McpServerTool(Name = "read_handoffs"), Description("Son N handoff kaydını okur. PM /status için kullanır.")]
    public static object ReadHandoffs(
        JsonlWriter writer,
        [Description("Kaç kayıt döneceği (varsayılan 10)")] int last_n = 10,
        [Description("from_agent filtresi (opsiyonel)")] string? filter_from = null,
        [Description("to_agent filtresi (opsiyonel)")] string? filter_to = null)
    {
        var records = writer.Read(HandoffsFile, lastN: Math.Max(1, last_n * 4));
        IEnumerable<Dictionary<string, JsonElement>> filtered = records;
        if (!string.IsNullOrWhiteSpace(filter_from))
            filtered = filtered.Where(r => r.TryGetValue("from", out var v) && v.ValueKind == JsonValueKind.String && v.GetString() == filter_from);
        if (!string.IsNullOrWhiteSpace(filter_to))
            filtered = filtered.Where(r => r.TryGetValue("to", out var v) && v.ValueKind == JsonValueKind.String && v.GetString() == filter_to);

        var list = filtered.ToList();
        if (list.Count > last_n) list = list.GetRange(list.Count - last_n, last_n);
        return new { ok = true, handoffs = list };
    }

    [McpServerTool(Name = "get_task_state"), Description("Belirli bir task'ın mevcut durumunu ve geçmişini döner.")]
    public static object GetTaskState(
        TaskStateStore store,
        [Description("Durumu sorgulanacak task id")] string task_id)
    {
        var rec = store.Get(task_id);
        if (rec is null) return new { ok = true, found = false };
        return new
        {
            ok = true,
            found = true,
            task_id = rec.TaskId,
            status = rec.Status.ToString(),
            owner = rec.Owner,
            created_utc = rec.CreatedUtc.ToString("O"),
            updated_utc = rec.UpdatedUtc.ToString("O"),
            history = rec.History
        };
    }

    [McpServerTool(Name = "claim_task"), Description("Bir agent'ın task'ı sahiplenmesi. Başkası tutuyorsa conflict döner.")]
    public static object ClaimTask(
        TaskStateStore store,
        [Description("Task'ı sahiplenen agent id'si")] string agent_id,
        [Description("Sahiplenilen task id'si")] string task_id)
    {
        var (ok, conflict) = store.Claim(agent_id, task_id);
        return ok
            ? new { ok = true, owner = agent_id }
            : new { ok = false, reason = "conflict", current_owner = conflict };
    }

    [McpServerTool(Name = "release_task"), Description("Agent'ın task'ı serbest bırakması. new_status: open|inprogress|blocked|done (varsayılan done).")]
    public static object ReleaseTask(
        TaskStateStore store,
        [Description("Task'ı serbest bırakan agent id'si")] string agent_id,
        [Description("Serbest bırakılan task id'si")] string task_id,
        [Description("Yeni durum: open|inprogress|blocked|done")] string new_status = "done")
    {
        if (!Enum.TryParse<Storage.TaskStatus>(NormalizeStatus(new_status), ignoreCase: true, out var s))
            s = Storage.TaskStatus.Done;
        store.Release(agent_id, task_id, s);
        return new { ok = true, status = s.ToString() };
    }

    [McpServerTool(Name = "append_user_note"), Description("Kullanıcının /tell-pm ile gönderdiği notu kuyruğa ekler. PM checkpoint'te okuyacak.")]
    public static object AppendUserNote(
        JsonlWriter writer,
        [Description("Kullanıcının mesajı")] string message)
    {
        var payload = new Dictionary<string, object?>
        {
            ["kind"] = "user_note",
            ["message"] = message,
            ["consumed"] = false
        };
        var seq = writer.Append(UserNotesFile, payload);
        return new { ok = true, seq };
    }

    [McpServerTool(Name = "read_user_notes"), Description("Henüz okunmamış user-notes kayıtlarını döner. since_seq üstündekileri getirir.")]
    public static object ReadUserNotes(
        JsonlWriter writer,
        [Description("Bu seq'ten sonraki notları getir (0 = hepsi)")] long since_seq = 0)
    {
        var records = writer.Read(UserNotesFile, lastN: int.MaxValue, sinceSeq: since_seq);
        long maxSeq = since_seq;
        foreach (var rec in records)
        {
            if (rec.TryGetValue("seq", out var s) && s.TryGetInt64(out var si) && si > maxSeq) maxSeq = si;
        }
        return new { ok = true, notes = records, new_since_seq = maxSeq };
    }

    private static object? ParseJsonOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return json; }
    }

    private static string NormalizeStatus(string input) => input.Replace("-", "").Replace("_", "").Replace(" ", "");
}
