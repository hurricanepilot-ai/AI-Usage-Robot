using AIUsageRobot.Shared;
using Microsoft.Data.Sqlite;

namespace AIUsageRobot.Service;

public sealed record StoredBalance(decimal Total, string Currency, bool IsAvailable, DateTimeOffset UpdatedAt);

public sealed class BalanceRepository
{
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = LocalAppStorage.DatabasePath }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(LocalAppStorage.RootDirectory);
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS deepseek_balance (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                total_balance TEXT NOT NULL,
                currency TEXT NOT NULL,
                is_available INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(StoredBalance balance, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO deepseek_balance (id, total_balance, currency, is_available, updated_at_utc)
            VALUES (1, $total, $currency, $available, $updated)
            ON CONFLICT(id) DO UPDATE SET
                total_balance = excluded.total_balance,
                currency = excluded.currency,
                is_available = excluded.is_available,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$total", balance.Total.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$currency", balance.Currency);
        command.Parameters.AddWithValue("$available", balance.IsAvailable ? 1 : 0);
        command.Parameters.AddWithValue("$updated", balance.UpdatedAt.UtcDateTime.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredBalance?> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT total_balance, currency, is_available, updated_at_utc FROM deepseek_balance WHERE id = 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new StoredBalance(
            decimal.Parse(reader.GetString(0), System.Globalization.CultureInfo.InvariantCulture),
            reader.GetString(1),
            reader.GetInt32(2) == 1,
            DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM deepseek_balance";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
