using AIUsageRobot.Service;
using System.Text.Json;

namespace AIUsageRobot.Tests;

public sealed class CodexUsageParserTests
{
    [Fact]
    public void Parse_ReadsSummaryAndDailyBuckets()
    {
        using var document = JsonDocument.Parse("""
            {
              "dailyUsageBuckets": [
                { "startDate": "2026-08-13", "tokens": 12345 },
                { "startDate": "2026-08-14", "tokens": 67890 }
              ],
              "summary": {
                "currentStreakDays": 4,
                "lifetimeTokens": 1234567,
                "longestRunningTurnSec": 321,
                "longestStreakDays": 9,
                "peakDailyTokens": 67890
              }
            }
            """);

        Assert.True(CodexUsageParser.TryParse(document.RootElement, DateTimeOffset.UnixEpoch, out var usage));
        Assert.NotNull(usage);
        Assert.Equal(1_234_567, usage.LifetimeTokens);
        Assert.Equal(67_890, usage.PeakDailyTokens);
        Assert.Equal(2, usage.DailyUsage.Count);
        Assert.Equal(new DateOnly(2026, 8, 14), usage.DailyUsage[1].StartDate);
    }

    [Fact]
    public void Parse_SupportsSnakeCase()
    {
        using var document = JsonDocument.Parse("""
            {
              "daily_usage_buckets": [{ "start_date": "2026-08-14", "tokens": "42" }],
              "summary": { "lifetime_tokens": "100", "current_streak_days": 1 }
            }
            """);

        Assert.True(CodexUsageParser.TryParse(document.RootElement, DateTimeOffset.UnixEpoch, out var usage));
        Assert.Equal(100, usage!.LifetimeTokens);
        Assert.Equal(42, usage.DailyUsage.Single().Tokens);
    }
}
