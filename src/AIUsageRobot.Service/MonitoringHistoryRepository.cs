using AIUsageRobot.Shared;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace AIUsageRobot.Service;

public sealed class MonitoringHistoryRepository
{
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = LocalAppStorage.DatabasePath }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(LocalAppStorage.RootDirectory);
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS provider_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider TEXT NOT NULL,
                metric TEXT NOT NULL,
                value TEXT NOT NULL,
                unit TEXT NOT NULL,
                collected_at_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_provider_snapshots_lookup
                ON provider_snapshots(provider, metric, collected_at_utc DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(ProviderSnapshotDto snapshot, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO provider_snapshots(provider, metric, value, unit, collected_at_utc)
            VALUES ($provider, $metric, $value, $unit, $collected);
            """;
        command.Parameters.AddWithValue("$provider", snapshot.Provider);
        command.Parameters.AddWithValue("$metric", snapshot.Metric);
        command.Parameters.AddWithValue("$value", snapshot.Value.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$unit", snapshot.Unit);
        command.Parameters.AddWithValue("$collected", snapshot.CollectedAt.UtcDateTime.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderSnapshotDto>> GetAsync(string provider, int hours, CancellationToken cancellationToken)
    {
        var result = new List<ProviderSnapshotDto>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT provider, metric, value, unit, collected_at_utc
            FROM provider_snapshots
            WHERE provider = $provider AND collected_at_utc >= $since
            ORDER BY collected_at_utc DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$since", DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours, 1, 24 * 90)).UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$limit", Math.Clamp(hours * 20, 2_000, 50_000));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new ProviderSnapshotDto(reader.GetString(0), reader.GetString(1),
                decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture), reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture)));
        return result;
    }
}
