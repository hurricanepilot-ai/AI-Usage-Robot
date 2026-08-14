using AIUsageRobot.Service;
using AIUsageRobot.Shared;

namespace AIUsageRobot.Tests;

public sealed class DeepSeekUsageAggregatorTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Aggregate_SumsBalanceDropsAndIgnoresTopUps()
    {
        var snapshots = new[]
        {
            Snapshot(100m, "2026-08-14T00:00:00Z"),
            Snapshot(97m, "2026-08-14T01:00:00Z"),
            Snapshot(110m, "2026-08-14T02:00:00Z"),
            Snapshot(108m, "2026-08-14T03:00:00Z")
        };

        var result = DeepSeekUsageAggregator.Aggregate(snapshots, 1, new DateOnly(2026, 8, 14), Utc);

        var day = Assert.Single(result.Days);
        Assert.Equal(5m, day.AmountUsed);
        Assert.True(day.HasData);
        Assert.Equal(4, day.SampleCount);
        Assert.Equal(100m, day.OpeningBalance);
        Assert.Equal(108m, day.ClosingBalance);
        Assert.Equal(DeepSeekUsageAggregator.CalculationMethod, result.CalculationMethod);
    }

    [Fact]
    public void Aggregate_UsesPreviousSampleAcrossDayBoundary()
    {
        var snapshots = new[]
        {
            Snapshot(50m, "2026-08-13T23:55:00Z"),
            Snapshot(49.25m, "2026-08-14T00:05:00Z")
        };

        var result = DeepSeekUsageAggregator.Aggregate(snapshots, 1, new DateOnly(2026, 8, 14), Utc);

        var day = Assert.Single(result.Days);
        Assert.Equal(0.75m, day.AmountUsed);
        Assert.Equal(50m, day.OpeningBalance);
        Assert.Equal(49.25m, day.ClosingBalance);
    }

    [Fact]
    public void Aggregate_ReturnsMissingDaysWithoutInventingUsage()
    {
        var snapshots = new[] { Snapshot(50m, "2026-08-14T00:00:00Z") };

        var result = DeepSeekUsageAggregator.Aggregate(snapshots, 3, new DateOnly(2026, 8, 14), Utc);

        Assert.Equal(3, result.Days.Count);
        Assert.False(result.Days[0].HasData);
        Assert.False(result.Days[1].HasData);
        Assert.True(result.Days[2].HasData);
        Assert.Equal(0m, result.Days[2].AmountUsed);
    }

    private static ProviderSnapshotDto Snapshot(decimal value, string timestamp) =>
        new("deepseek", "balance", value, "CNY", DateTimeOffset.Parse(timestamp));
}
