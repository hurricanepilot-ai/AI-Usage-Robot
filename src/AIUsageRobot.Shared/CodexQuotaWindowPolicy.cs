namespace AIUsageRobot.Shared;

public static class CodexQuotaWindowPolicy
{
    public const long FiveHourWindowMinutes = 300;

    public static CodexQuotaWindowDto? SelectFocusWindow(IEnumerable<CodexQuotaWindowDto>? windows)
    {
        var candidates = windows?.ToArray() ?? [];
        if (candidates.Length == 0) return null;

        return candidates.FirstOrDefault(window => PeriodMinutes(window.Period) == FiveHourWindowMinutes)
            ?? candidates.FirstOrDefault(window => string.Equals(window.Name, "primary", StringComparison.OrdinalIgnoreCase))
            ?? candidates.Where(window => PeriodMinutes(window.Period) > 0)
                .MinBy(window => PeriodMinutes(window.Period))
            ?? candidates[0];
    }

    public static string DisplayLabel(CodexQuotaWindowDto window) => PeriodMinutes(window.Period) switch
    {
        FiveHourWindowMinutes => "5小时",
        10_080 => "7天",
        var minutes when minutes > 0 && minutes % 1_440 == 0 => $"{minutes / 1_440}天",
        var minutes when minutes > 0 && minutes % 60 == 0 => $"{minutes / 60}小时",
        var minutes when minutes > 0 => $"{minutes}分钟",
        _ => string.Equals(window.Name, "primary", StringComparison.OrdinalIgnoreCase) ? "短周期" : "长周期"
    };

    public static long PeriodMinutes(string? period)
    {
        var parts = period?.Split('_', 2);
        if (parts is not { Length: 2 } || !long.TryParse(parts[0], out var value)) return 0;
        return parts[1] switch
        {
            "minutes" => value,
            "hours" => value * 60,
            "days" => value * 1_440,
            "weeks" => value * 10_080,
            _ => 0
        };
    }
}
