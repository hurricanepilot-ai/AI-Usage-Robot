using AIUsageRobot.Service;
using System.Text.Json;

namespace AIUsageRobot.Tests;

public sealed class CodexRateLimitParserTests
{
    [Fact]
    public void Parse_PrefersCodexBucketAndLongestWindow()
    {
        using var document = JsonDocument.Parse("""
            {
              "rateLimits": {
                "primary": { "usedPercent": 99, "windowDurationMins": 60 }
              },
              "rateLimitsByLimitId": {
                "codex": {
                  "limitId": "codex",
                  "limitName": "Codex",
                  "planType": "pro",
                  "primary": { "usedPercent": 20, "windowDurationMins": 300, "resetsAt": 1800000000 },
                  "secondary": { "usedPercent": 56, "windowDurationMins": 10080, "resetsAt": 1800000100 }
                }
              }
            }
            """);

        var parsed = CodexRateLimitParser.TryParse(document.RootElement, DateTimeOffset.UnixEpoch, out var quota);

        Assert.True(parsed);
        Assert.NotNull(quota);
        Assert.Equal(44, quota.Value);
        Assert.Equal("remaining", quota.MetricSemantics);
        Assert.Equal("1_weeks", quota.Period);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_000_100), quota.ResetAt);
        Assert.Equal("Codex · pro", quota.Model);
        Assert.Equal(CodexRateLimitParser.SourceVersion, quota.ParserVersion);
    }

    [Fact]
    public void Parse_SupportsSnakeCaseLegacyPayload()
    {
        using var document = JsonDocument.Parse("""
            {
              "rate_limits": {
                "primary": {
                  "used_percent": 12,
                  "window_duration_mins": 300,
                  "resets_at": 1800000000
                }
              }
            }
            """);

        var parsed = CodexRateLimitParser.TryParse(document.RootElement, DateTimeOffset.UnixEpoch, out var quota);

        Assert.True(parsed);
        Assert.Equal(88, quota!.Value);
        Assert.Equal("5_hours", quota.Period);
    }

    [Fact]
    public void Parse_RejectsPayloadWithoutAUsageWindow()
    {
        using var document = JsonDocument.Parse("""{ "rateLimits": { "primary": null } }""");

        Assert.False(CodexRateLimitParser.TryParse(document.RootElement, DateTimeOffset.UnixEpoch, out _));
    }
}
