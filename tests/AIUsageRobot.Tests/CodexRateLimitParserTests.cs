using AIUsageRobot.Service;
using System.Text.Json;

namespace AIUsageRobot.Tests;

public sealed class CodexRateLimitParserTests
{
    [Fact]
    public void ParseSnapshot_PrefersCodexBucketAndKeepsBothWindows()
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

        var parsed = CodexRateLimitParser.TryParseSnapshot(document.RootElement, DateTimeOffset.UnixEpoch, out var quota);

        Assert.True(parsed);
        Assert.NotNull(quota);
        Assert.Equal("Codex · pro", quota.Model);
        Assert.Equal(CodexRateLimitParser.SourceVersion, quota.ParserVersion);
        Assert.Equal(2, quota.Windows.Count);
        Assert.Equal(80, quota.Windows.Single(window => window.Name == "primary").RemainingPercentage);
        var secondary = quota.Windows.Single(window => window.Name == "secondary");
        Assert.Equal(44, secondary.RemainingPercentage);
        Assert.Equal("1_weeks", secondary.Period);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_000_100), secondary.ResetAt);
    }

    [Fact]
    public void ParseSnapshot_SupportsSnakeCasePayload()
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

        var parsed = CodexRateLimitParser.TryParseSnapshot(document.RootElement, DateTimeOffset.UnixEpoch, out var quota);

        Assert.True(parsed);
        var primary = Assert.Single(quota!.Windows);
        Assert.Equal(88, primary.RemainingPercentage);
        Assert.Equal("5_hours", primary.Period);
    }

    [Fact]
    public void Parse_RejectsPayloadWithoutAUsageWindow()
    {
        using var document = JsonDocument.Parse("""{ "rateLimits": { "primary": null } }""");

        Assert.False(CodexRateLimitParser.TryParseSnapshot(document.RootElement, DateTimeOffset.UnixEpoch, out _));
    }
}
