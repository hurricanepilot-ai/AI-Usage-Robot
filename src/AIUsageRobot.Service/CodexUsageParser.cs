using AIUsageRobot.Shared;
using System.Globalization;
using System.Text.Json;

namespace AIUsageRobot.Service;

public static class CodexUsageParser
{
    public static bool TryParse(JsonElement payload, DateTimeOffset collectedAt, out CodexUsageInput? usage)
    {
        usage = null;
        if (payload.ValueKind != JsonValueKind.Object ||
            !TryGet(payload, "summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
            return false;

        var daily = new List<CodexDailyUsageDto>();
        if (TryGet(payload, "dailyUsageBuckets", out var buckets) && buckets.ValueKind == JsonValueKind.Array)
        {
            foreach (var bucket in buckets.EnumerateArray())
            {
                if (!TryGetString(bucket, "startDate", out var dateText) ||
                    !DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                    !TryGetInt64(bucket, "tokens", out var tokens)) continue;
                daily.Add(new CodexDailyUsageDto(date, tokens));
            }
        }

        usage = new CodexUsageInput(
            ReadInt64(summary, "lifetimeTokens"),
            ReadInt64(summary, "peakDailyTokens"),
            ReadInt32(summary, "currentStreakDays"),
            ReadInt32(summary, "longestStreakDays"),
            ReadInt64(summary, "longestRunningTurnSec"),
            daily,
            collectedAt);
        return true;
    }

    private static long? ReadInt64(JsonElement element, string name) =>
        TryGetInt64(element, name, out var value) ? value : null;

    private static int? ReadInt32(JsonElement element, string name) =>
        TryGetInt64(element, name, out var value) && value is >= int.MinValue and <= int.MaxValue ? (int)value : null;

    private static bool TryGetInt64(JsonElement element, string name, out long value)
    {
        value = 0;
        if (!TryGet(element, name, out var property)) return false;
        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(property.GetString(), out value),
            _ => false
        };
    }

    private static bool TryGetString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!TryGet(element, name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString();
        return value is not null;
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;
        var snake = string.Concat(name.Select((character, index) => char.IsUpper(character)
            ? $"_{char.ToLowerInvariant(character)}"
            : character.ToString()));
        return element.TryGetProperty(snake, out value);
    }
}
