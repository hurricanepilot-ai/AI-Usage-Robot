using AIUsageRobot.Shared;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace AIUsageRobot.Service;

public sealed record StoredCodexWindow(
    string Name,
    string? Model,
    int RemainingPercentage,
    string? Period,
    DateTimeOffset? ResetAt,
    DateTimeOffset CollectedAt,
    string ParserVersion);

public sealed class ChatGptQuotaRepository : SqliteRepositoryBase
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(LocalAppStorage.RootDirectory);
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS codex_quota_windows (
                name TEXT PRIMARY KEY,
                model TEXT NULL,
                remaining_percentage INTEGER NOT NULL CHECK (remaining_percentage BETWEEN 0 AND 100),
                period TEXT NULL,
                reset_at_utc TEXT NULL,
                collected_at_utc TEXT NOT NULL,
                parser_version TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS codex_usage_summary (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                lifetime_tokens INTEGER NULL,
                peak_daily_tokens INTEGER NULL,
                current_streak_days INTEGER NULL,
                longest_streak_days INTEGER NULL,
                longest_running_turn_seconds INTEGER NULL,
                collected_at_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS codex_daily_usage (
                start_date TEXT PRIMARY KEY,
                tokens INTEGER NOT NULL,
                collected_at_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveSnapshotAsync(CodexQuotaSnapshotInput input, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var clearObsolete = connection.CreateCommand();
        clearObsolete.Transaction = (SqliteTransaction)transaction;
        clearObsolete.CommandText = "DELETE FROM codex_quota_windows WHERE name NOT IN ('primary', 'secondary')";
        await clearObsolete.ExecuteNonQueryAsync(cancellationToken);
        foreach (var window in input.Windows)
        {
            var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO codex_quota_windows
                    (name, model, remaining_percentage, period, reset_at_utc, collected_at_utc, parser_version)
                VALUES ($name, $model, $remaining, $period, $reset, $collected, $parser)
                ON CONFLICT(name) DO UPDATE SET
                    model = excluded.model,
                    remaining_percentage = excluded.remaining_percentage,
                    period = excluded.period,
                    reset_at_utc = excluded.reset_at_utc,
                    collected_at_utc = excluded.collected_at_utc,
                    parser_version = excluded.parser_version;
                """;
            command.Parameters.AddWithValue("$name", window.Name);
            command.Parameters.AddWithValue("$model", (object?)input.Model ?? DBNull.Value);
            command.Parameters.AddWithValue("$remaining", window.RemainingPercentage);
            command.Parameters.AddWithValue("$period", (object?)window.Period ?? DBNull.Value);
            command.Parameters.AddWithValue("$reset", window.ResetAt is null ? DBNull.Value : window.ResetAt.Value.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$collected", input.CollectedAt.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$parser", input.ParserVersion);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveUsageAsync(CodexUsageInput input, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var summary = connection.CreateCommand();
        summary.Transaction = (SqliteTransaction)transaction;
        summary.CommandText = """
            INSERT INTO codex_usage_summary
                (id, lifetime_tokens, peak_daily_tokens, current_streak_days, longest_streak_days, longest_running_turn_seconds, collected_at_utc)
            VALUES (1, $lifetime, $peak, $current, $longest, $turn, $collected)
            ON CONFLICT(id) DO UPDATE SET
                lifetime_tokens = excluded.lifetime_tokens,
                peak_daily_tokens = excluded.peak_daily_tokens,
                current_streak_days = excluded.current_streak_days,
                longest_streak_days = excluded.longest_streak_days,
                longest_running_turn_seconds = excluded.longest_running_turn_seconds,
                collected_at_utc = excluded.collected_at_utc;
            """;
        summary.Parameters.AddWithValue("$lifetime", (object?)input.LifetimeTokens ?? DBNull.Value);
        summary.Parameters.AddWithValue("$peak", (object?)input.PeakDailyTokens ?? DBNull.Value);
        summary.Parameters.AddWithValue("$current", (object?)input.CurrentStreakDays ?? DBNull.Value);
        summary.Parameters.AddWithValue("$longest", (object?)input.LongestStreakDays ?? DBNull.Value);
        summary.Parameters.AddWithValue("$turn", (object?)input.LongestRunningTurnSeconds ?? DBNull.Value);
        summary.Parameters.AddWithValue("$collected", input.CollectedAt.UtcDateTime.ToString("O"));
        await summary.ExecuteNonQueryAsync(cancellationToken);

        foreach (var bucket in input.DailyUsage)
        {
            var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO codex_daily_usage (start_date, tokens, collected_at_utc)
                VALUES ($date, $tokens, $collected)
                ON CONFLICT(start_date) DO UPDATE SET tokens = excluded.tokens, collected_at_utc = excluded.collected_at_utc;
                """;
            command.Parameters.AddWithValue("$date", bucket.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$tokens", bucket.Tokens);
            command.Parameters.AddWithValue("$collected", input.CollectedAt.UtcDateTime.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredCodexWindow>> GetWindowsAsync(CancellationToken cancellationToken)
    {
        var result = new List<StoredCodexWindow>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT name, model, remaining_percentage, period, reset_at_utc, collected_at_utc, parser_version FROM codex_quota_windows";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new StoredCodexWindow(
                reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), ParseNullableDate(reader, 4),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture), reader.GetString(6)));
        return result;
    }

    public async Task<CodexUsageSummaryDto?> GetUsageAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var daily = new List<CodexDailyUsageDto>();
        var dailyCommand = connection.CreateCommand();
        dailyCommand.CommandText = "SELECT start_date, tokens FROM codex_daily_usage ORDER BY start_date DESC LIMIT 31";
        await using (var reader = await dailyCommand.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                daily.Add(new CodexDailyUsageDto(DateOnly.Parse(reader.GetString(0), CultureInfo.InvariantCulture), reader.GetInt64(1)));

        var command = connection.CreateCommand();
        command.CommandText = "SELECT lifetime_tokens, peak_daily_tokens, current_streak_days, longest_streak_days, longest_running_turn_seconds, collected_at_utc FROM codex_usage_summary WHERE id = 1";
        await using var summaryReader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await summaryReader.ReadAsync(cancellationToken)) return null;
        return new CodexUsageSummaryDto(
            GetNullableInt64(summaryReader, 0), GetNullableInt64(summaryReader, 1), GetNullableInt32(summaryReader, 2),
            GetNullableInt32(summaryReader, 3), GetNullableInt64(summaryReader, 4), daily,
            DateTimeOffset.Parse(summaryReader.GetString(5), CultureInfo.InvariantCulture));
    }

    private static DateTimeOffset? ParseNullableDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
    private static long? GetNullableInt64(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    private static int? GetNullableInt32(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
}

public sealed class ChatGptQuotaState(ChatGptQuotaRepository repository, MonitoringHistoryRepository history)
{
    private string? _failureMessage;

    public async Task SaveAsync(CodexQuotaSnapshotInput input, CancellationToken cancellationToken)
    {
        await repository.SaveSnapshotAsync(input, cancellationToken);
        foreach (var window in input.Windows)
            await history.SaveAsync(new ProviderSnapshotDto("codex", $"quota_{window.Name}", window.RemainingPercentage, "percent", input.CollectedAt), cancellationToken);
        Interlocked.Exchange(ref _failureMessage, null);
    }

    public Task SaveUsageAsync(CodexUsageInput input, CancellationToken cancellationToken) => repository.SaveUsageAsync(input, cancellationToken);
    public void ReportFailure(string message) => Interlocked.Exchange(ref _failureMessage, message);

    public async Task<ChatGptQuotaDto> GetAsync(CancellationToken cancellationToken)
    {
        var stored = await repository.GetWindowsAsync(cancellationToken);
        if (stored.Count == 0)
            return new ChatGptQuotaDto(null,
                new Metric<int?>(null, string.IsNullOrWhiteSpace(_failureMessage) ? DataStatus.Unknown : DataStatus.Unavailable, null,
                    _failureMessage ?? "正在连接 Codex 额度服务"),
                "unknown", null, null, CodexRateLimitParser.SourceVersion, [], await repository.GetUsageAsync(cancellationToken));

        var windows = stored.Select(ToDto).ToArray();
        var selectedDto = CodexQuotaWindowPolicy.SelectFocusWindow(windows)!;
        var selected = stored.First(item => item.Name == selectedDto.Name);
        return new ChatGptQuotaDto(selected.Model, selectedDto.RemainingPercentage, "remaining", selectedDto.Period,
            selectedDto.ResetAt, selected.ParserVersion, windows, await repository.GetUsageAsync(cancellationToken));
    }

    private CodexQuotaWindowDto ToDto(StoredCodexWindow window)
    {
        var age = DateTimeOffset.UtcNow - window.CollectedAt;
        var status = age < TimeSpan.FromMinutes(15) ? DataStatus.Fresh : age <= TimeSpan.FromHours(24) ? DataStatus.Stale : DataStatus.Unavailable;
        var message = status switch { DataStatus.Stale => $"数据已过期 {FormatAge(age)}", DataStatus.Unavailable => "超过 24 小时未采集", _ => null };
        if (!string.IsNullOrWhiteSpace(_failureMessage) && status != DataStatus.Fresh) message = _failureMessage;
        return new CodexQuotaWindowDto(window.Name, new Metric<int?>(window.RemainingPercentage, status, window.CollectedAt, message), window.Period, window.ResetAt);
    }

    private static string FormatAge(TimeSpan age) => age.TotalHours >= 1 ? $"{(int)age.TotalHours}h" : $"{Math.Max(1, (int)age.TotalMinutes)}m";
}
