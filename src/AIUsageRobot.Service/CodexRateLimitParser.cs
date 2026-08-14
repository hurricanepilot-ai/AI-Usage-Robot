using AIUsageRobot.Shared;
using System.Text.Json;

namespace AIUsageRobot.Service;

public static class CodexRateLimitParser
{
    public const string SourceVersion = "codex-app-server-v2";

    public static bool TryParseSnapshot(JsonElement payload, DateTimeOffset collectedAt, out CodexQuotaSnapshotInput? quota)
    {
        quota = null;
        if (!TrySelectSnapshot(payload, out var snapshot)) return false;

        var windows = new List<CodexQuotaWindowInput>();
        AddParsedWindow(snapshot, "primary", "primary", windows);
        AddParsedWindow(snapshot, "secondary", "secondary", windows);
        if (windows.Count == 0) return false;

        var limitName = ReadString(snapshot, "limitName", "limit_name");
        var planType = ReadString(snapshot, "planType", "plan_type");
        var model = string.IsNullOrWhiteSpace(limitName) ? "Codex" : limitName;
        if (!string.IsNullOrWhiteSpace(planType)) model = $"{model} · {planType}";

        quota = new CodexQuotaSnapshotInput(model, windows, collectedAt, SourceVersion);
        return true;
    }

    public static bool TryParse(JsonElement payload, DateTimeOffset collectedAt, out ChatGptQuotaInput? quota)
    {
        quota = null;
        if (!TryParseSnapshot(payload, collectedAt, out var snapshot) || snapshot is null) return false;
        var selected = snapshot.Windows.MaxBy(item => ParsePeriodMinutes(item.Period))!;

        quota = new ChatGptQuotaInput(
            "chatgpt",
            snapshot.Model,
            selected.RemainingPercentage,
            "remaining",
            selected.Period,
            selected.ResetAt,
            collectedAt,
            SourceVersion,
            null);
        return true;
    }

    private static void AddParsedWindow(JsonElement snapshot, string propertyName, string displayName, List<CodexQuotaWindowInput> windows)
    {
        if (!snapshot.TryGetProperty(propertyName, out var window) || window.ValueKind != JsonValueKind.Object) return;
        if (!TryReadNumber(window, "usedPercent", "used_percent", out var usedPercent)) return;
        var duration = TryReadNumber(window, "windowDurationMins", "window_duration_mins", out var durationValue)
            ? Math.Max(0, (long)durationValue)
            : 0;
        var resetAt = TryReadNumber(window, "resetsAt", "resets_at", out var resetSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds((long)resetSeconds)
            : (DateTimeOffset?)null;
        var remaining = Math.Clamp(100 - (int)Math.Round(usedPercent, MidpointRounding.AwayFromZero), 0, 100);
        windows.Add(new CodexQuotaWindowInput(displayName, remaining, FormatPeriod(duration), resetAt));
    }

    private static long ParsePeriodMinutes(string? period)
    {
        if (string.IsNullOrWhiteSpace(period)) return 0;
        var parts = period.Split('_', 2);
        if (parts.Length != 2 || !long.TryParse(parts[0], out var value)) return 0;
        return parts[1] switch
        {
            "minutes" => value,
            "hours" => value * 60,
            "days" => value * 1_440,
            "weeks" => value * 10_080,
            _ => 0
        };
    }

    private static bool TrySelectSnapshot(JsonElement payload, out JsonElement snapshot)
    {
        if (TryGetProperty(payload, "rateLimitsByLimitId", "rate_limits_by_limit_id", out var buckets) &&
            buckets.ValueKind == JsonValueKind.Object)
        {
            if (buckets.TryGetProperty("codex", out snapshot) && snapshot.ValueKind == JsonValueKind.Object)
                return true;

            foreach (var property in buckets.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    snapshot = property.Value;
                    return true;
                }
            }
        }

        if (TryGetProperty(payload, "rateLimits", "rate_limits", out snapshot) && snapshot.ValueKind == JsonValueKind.Object)
            return true;

        if (payload.ValueKind == JsonValueKind.Object &&
            (payload.TryGetProperty("primary", out _) || payload.TryGetProperty("secondary", out _)))
        {
            snapshot = payload;
            return true;
        }

        snapshot = default;
        return false;
    }

    private static void AddWindow(JsonElement snapshot, string name, List<(JsonElement Window, long Duration)> windows)
    {
        if (!snapshot.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object) return;
        if (!TryReadNumber(window, "usedPercent", "used_percent", out _)) return;
        var duration = TryReadNumber(window, "windowDurationMins", "window_duration_mins", out var value)
            ? Math.Max(0, (long)value)
            : 0;
        windows.Add((window, duration));
    }

    private static string FormatPeriod(long minutes)
    {
        if (minutes > 0 && minutes % 10_080 == 0) return $"{minutes / 10_080}_weeks";
        if (minutes > 0 && minutes % 1_440 == 0) return $"{minutes / 1_440}_days";
        if (minutes > 0 && minutes % 60 == 0) return $"{minutes / 60}_hours";
        return minutes > 0 ? $"{minutes}_minutes" : "unknown";
    }

    private static string? ReadString(JsonElement element, string camelName, string snakeName)
    {
        if (!TryGetProperty(element, camelName, snakeName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }

    private static bool TryReadNumber(JsonElement element, string camelName, string snakeName, out double value)
    {
        value = 0;
        if (!TryGetProperty(element, camelName, snakeName, out var property)) return false;
        if (property.ValueKind == JsonValueKind.Number) return property.TryGetDouble(out value);
        return property.ValueKind == JsonValueKind.String &&
               double.TryParse(property.GetString(), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetProperty(JsonElement element, string camelName, string snakeName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (element.TryGetProperty(camelName, out value)) return true;
        return element.TryGetProperty(snakeName, out value);
    }
}
