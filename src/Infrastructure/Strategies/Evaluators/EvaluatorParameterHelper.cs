using System.Text.Json;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

internal static class EvaluatorParameterHelper
{
    public static T? TryParse<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch
        {
            return null;
        }
    }

    public static string SerializeContext<T>(T context) =>
        JsonSerializer.Serialize(context, new JsonSerializerOptions
        {
            WriteIndented = false,
        });
}
