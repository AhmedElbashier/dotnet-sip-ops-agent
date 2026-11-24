using System.Globalization;
using System.Text.Json;

namespace SipOpsAgent.Tools;

internal static class JsonArgs
{
    public static JsonElement? ParseObject(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        var doc = JsonDocument.Parse(argumentsJson);
        return doc.RootElement.ValueKind == JsonValueKind.Object
            ? doc.RootElement.Clone()
            : throw new JsonException("Tool arguments must be a JSON object.");
    }

    public static string? GetString(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGet(obj, name, out var value))
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            }
        }

        return null;
    }

    public static int? GetInt(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(obj, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
            {
                return n;
            }

            if (value.ValueKind == JsonValueKind.String &&
                int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    public static DateTimeOffset? GetTime(JsonElement obj, params string[] names)
    {
        var raw = GetString(obj, names);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public static IReadOnlyList<string> GetStringArray(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(obj, name, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return value.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToList();
        }

        return [];
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
