using AIUsageRobot.Shared;
using Microsoft.Data.Sqlite;

namespace AIUsageRobot.Service;

public sealed record StoredChatGptQuota(
    string? Model,
    int Value,
    string MetricSemantics,
    string? Period,
    DateTimeOffset? ResetAt,
    DateTimeOffset CollectedAt,
    string ParserVersion);

public sealed class ChatGptQuotaRepository
{
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = LocalAppStorage.DatabasePath }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(LocalAppStorage.RootDirectory);
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS chatgpt_quota (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                model TEXT NULL,
                value INTEGER NOT NULL CHECK (value BETWEEN 0 AND 100),
                metric_semantics TEXT NOT NULL,
                period TEXT NULL,
                reset_at_utc TEXT NULL,
                collected_at_utc TEXT NOT NULL,
                parser_version TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(ChatGptQuotaInput input, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chatgpt_quota
                (id, model, value, metric_semantics, period, reset_at_utc, collected_at_utc, parser_version)
            VALUES (1, $model, $value, $semantics, $period, $reset, $collected, $parser)
            ON CONFLICT(id) DO UPDATE SET
                model = excluded.model,
                value = excluded.value,
                metric_semantics = excluded.metric_semantics,
                period = excluded.period,
                reset_at_utc = excluded.reset_at_utc,
                collected_at_utc = excluded.collected_at_utc,
                parser_version = excluded.parser_version;
            """;
        command.Parameters.AddWithValue("$model", (object?)input.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("$value", input.Value);
        command.Parameters.AddWithValue("$semantics", input.MetricSemantics);
        command.Parameters.AddWithValue("$period", (object?)input.Period ?? DBNull.Value);
        command.Parameters.AddWithValue("$reset", input.ResetAt is null ? DBNull.Value : input.ResetAt.Value.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$collected", input.CollectedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$parser", input.ParserVersion);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredChatGptQuota?> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT model, value, metric_semantics, period, reset_at_utc, collected_at_utc, parser_version FROM chatgpt_quota WHERE id = 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new StoredChatGptQuota(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture),
            reader.GetString(6));
    }
}

public sealed class ChatGptQuotaState(ChatGptQuotaRepository repository)
{
    private string? _failureMessage;

    public async Task SaveAsync(ChatGptQuotaInput input, CancellationToken cancellationToken)
    {
        await repository.SaveAsync(input, cancellationToken);
        Interlocked.Exchange(ref _failureMessage, null);
    }

    public void ReportFailure(string message) => Interlocked.Exchange(ref _failureMessage, message);

    public async Task<ChatGptQuotaDto> GetAsync(CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(cancellationToken);
        if (stored is null)
            return new ChatGptQuotaDto(null,
                new Metric<int?>(null,
                    string.IsNullOrWhiteSpace(_failureMessage) ? DataStatus.Unknown : DataStatus.Unavailable,
                    null,
                    _failureMessage ?? "正在连接 Codex 额度服务"),
                "unknown", null, null, CodexRateLimitParser.SourceVersion);

        var age = DateTimeOffset.UtcNow - stored.CollectedAt;
        var status = age < TimeSpan.FromMinutes(15) ? DataStatus.Fresh
            : age <= TimeSpan.FromHours(24) ? DataStatus.Stale
            : DataStatus.Unavailable;
        var message = status switch
        {
            DataStatus.Stale => $"数据已过期 {FormatAge(age)}",
            DataStatus.Unavailable => "超过 24 小时未采集",
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(_failureMessage) && status != DataStatus.Fresh)
            message = _failureMessage;
        return new ChatGptQuotaDto(stored.Model,
            new Metric<int?>(stored.Value, status, stored.CollectedAt, message),
            stored.MetricSemantics, stored.Period, stored.ResetAt, stored.ParserVersion);
    }

    private static string FormatAge(TimeSpan age) => age.TotalHours >= 1 ? $"{(int)age.TotalHours}h" : $"{Math.Max(1, (int)age.TotalMinutes)}m";
}
