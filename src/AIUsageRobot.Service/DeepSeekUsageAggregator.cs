using AIUsageRobot.Shared;

namespace AIUsageRobot.Service;

public static class DeepSeekUsageAggregator
{
    public const string CalculationMethod = "sum-of-balance-decreases";

    public static DeepSeekUsageTrendDto Aggregate(
        IEnumerable<ProviderSnapshotDto> snapshots,
        int days,
        DateOnly endDate,
        TimeZoneInfo? timeZone = null)
    {
        days = Math.Clamp(days, 1, 90);
        timeZone ??= TimeZoneInfo.Local;
        var firstDate = endDate.AddDays(-(days - 1));
        var ordered = snapshots
            .Where(snapshot => snapshot.Provider == "deepseek" && snapshot.Metric == "balance")
            .OrderBy(snapshot => snapshot.CollectedAt)
            .ToArray();

        var accumulators = Enumerable.Range(0, days)
            .Select(offset => firstDate.AddDays(offset))
            .ToDictionary(date => date, date => new DailyAccumulator(date));

        ProviderSnapshotDto? previous = null;
        foreach (var snapshot in ordered)
        {
            var localTime = TimeZoneInfo.ConvertTime(snapshot.CollectedAt, timeZone);
            var date = DateOnly.FromDateTime(localTime.DateTime);
            if (accumulators.TryGetValue(date, out var day))
            {
                day.SampleCount++;
                day.Currency = snapshot.Unit;
                day.OpeningBalance ??= previous?.Value ?? snapshot.Value;
                day.ClosingBalance = snapshot.Value;
                if (previous is not null && previous.Value > snapshot.Value)
                    day.AmountUsed += previous.Value - snapshot.Value;
            }
            previous = snapshot;
        }

        var result = accumulators.Values
            .OrderBy(day => day.Date)
            .Select(day => new DeepSeekDailyAmountDto(
                day.Date,
                decimal.Round(day.AmountUsed, 4),
                day.SampleCount > 0,
                day.SampleCount,
                day.OpeningBalance,
                day.ClosingBalance,
                day.Currency ?? ordered.LastOrDefault()?.Unit ?? "CNY"))
            .ToArray();
        return new DeepSeekUsageTrendDto(result, ordered.FirstOrDefault()?.CollectedAt, CalculationMethod);
    }

    private sealed class DailyAccumulator(DateOnly date)
    {
        public DateOnly Date { get; } = date;
        public decimal AmountUsed { get; set; }
        public int SampleCount { get; set; }
        public decimal? OpeningBalance { get; set; }
        public decimal? ClosingBalance { get; set; }
        public string? Currency { get; set; }
    }
}

public sealed class DeepSeekUsageService(MonitoringHistoryRepository history)
{
    public async Task<DeepSeekUsageTrendDto> GetDailyAsync(int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 90);
        var snapshots = await history.GetAsync("deepseek", days * 24 + 24, cancellationToken);
        return DeepSeekUsageAggregator.Aggregate(
            snapshots,
            days,
            DateOnly.FromDateTime(DateTime.Today));
    }
}
