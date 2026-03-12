namespace Service.Infrastructure.Sqlite;

/// <summary>
/// Options for the local SQLite database.
/// </summary>
/// <param name="DatabasePath">Absolute path to the SQLite database file.</param>
public sealed record SqliteDatabaseOptions(string DatabasePath);
