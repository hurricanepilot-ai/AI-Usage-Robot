using AIUsageRobot.Shared;
using Microsoft.Data.Sqlite;

namespace AIUsageRobot.Service;

public abstract class SqliteRepositoryBase
{
    protected string ConnectionString => new SqliteConnectionStringBuilder { DataSource = LocalAppStorage.DatabasePath }.ToString();
}
