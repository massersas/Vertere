using Microsoft.Data.Sqlite;

namespace Service.Infrastructure.Sqlite;

/// <summary>
/// Creates and initializes the local SQLite database.
/// </summary>
public static class SqliteBootstrapper
{
    /// <summary>
    /// Gets the default database path under the user profile.
    /// </summary>
    /// <returns>Absolute path for the local SQLite database file.</returns>
    public static string GetDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SchedulerPDV", "schedulerpdv.db");
    }

    /// <summary>
    /// Ensures the database and required tables exist.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public static async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath();
        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ScheduleHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CreatedAt TEXT NOT NULL,
                PayloadJson TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
