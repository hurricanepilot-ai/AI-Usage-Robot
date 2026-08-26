using AIUsageRobot.Shared;

namespace AIUsageRobot.Tests;

public sealed class CodexQuotaWindowPolicyTests
{
    [Fact]
    public void SelectFocusWindow_PrefersFiveHourWindowOverWeeklyWindow()
    {
        var collectedAt = DateTimeOffset.UnixEpoch;
        CodexQuotaWindowDto[] windows =
        [
            new("secondary", new Metric<int?>(95, DataStatus.Fresh, collectedAt, null), "1_weeks", collectedAt.AddDays(7)),
            new("primary", new Metric<int?>(77, DataStatus.Fresh, collectedAt, null), "5_hours", collectedAt.AddHours(5))
        ];

        var selected = CodexQuotaWindowPolicy.SelectFocusWindow(windows);

        Assert.NotNull(selected);
        Assert.Equal("primary", selected.Name);
        Assert.Equal(77, selected.RemainingPercentage.Value);
        Assert.Equal("5小时", CodexQuotaWindowPolicy.DisplayLabel(selected));
    }

    [Fact]
    public void SelectFocusWindow_FallsBackToPrimaryForUnknownDurations()
    {
        var metric = new Metric<int?>(50, DataStatus.Fresh, DateTimeOffset.UnixEpoch, null);
        CodexQuotaWindowDto[] windows =
        [
            new("secondary", metric, "unknown", null),
            new("primary", metric, "unknown", null)
        ];

        Assert.Equal("primary", CodexQuotaWindowPolicy.SelectFocusWindow(windows)?.Name);
    }
}
