using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PersonalAssistant.Harness.Activity;

namespace PersonalAssistant.Harness.Persistence;

public sealed class SqliteHarnessDatabase : IDisposable
{
    private static readonly Regex MigrationNamePattern = new(
        @"\.Migrations\.(?<version>[0-9]+)_(?<name>[^.]+)\.sql$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static int sqliteProviderInitialized;
    private readonly object syncRoot = new();
    private readonly SqliteConnection connection;
    private readonly bool ownsConnection;

    public SqliteHarnessDatabase(string databasePath)
    {
        EnsureSqliteProvider();
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        ownsConnection = true;
        try
        {
            Initialize();
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public SqliteHarnessDatabase(SqliteConnection openConnection, bool ownsConnection = false)
    {
        EnsureSqliteProvider();
        connection = openConnection;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        this.ownsConnection = ownsConnection;
        try
        {
            Initialize();
        }
        catch
        {
            if (ownsConnection)
            {
                connection.Dispose();
            }

            throw;
        }
    }

    public bool ForeignKeysEnabled
    {
        get
        {
            lock (syncRoot)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA foreign_keys;";
                return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 1;
            }
        }
    }

    internal SqliteConnection Connection => connection;

    internal object SyncRoot => syncRoot;

    public void ExecuteInTransaction(Action<SqliteTransaction> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (syncRoot)
        {
            using var transaction = connection.BeginTransaction();
            operation(transaction);
            transaction.Commit();
        }
    }

    public IReadOnlyList<ActivityEvent> ReadActivityEvents()
    {
        lock (syncRoot)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, timestamp, agent_id, realm, category, operation, target, status, duration_ms, metadata_json
                FROM activity_events
                ORDER BY timestamp ASC;
                """;
            using var reader = command.ExecuteReader();
            var events = new List<ActivityEvent>();
            while (reader.Read())
            {
                events.Add(ReadActivityEvent(reader));
            }

            return events;
        }
    }

    public IReadOnlyList<ActivityEvent> ReadActivityEventsBetween(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        lock (syncRoot)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, timestamp, agent_id, realm, category, operation, target, status, duration_ms, metadata_json
                FROM activity_events
                WHERE timestamp >= $start AND timestamp < $end
                ORDER BY timestamp ASC, id ASC;
                """;
            command.Parameters.AddWithValue("$start", startUtc.ToString("O"));
            command.Parameters.AddWithValue("$end", endUtc.ToString("O"));
            using var reader = command.ExecuteReader();
            var events = new List<ActivityEvent>();
            while (reader.Read())
            {
                events.Add(ReadActivityEvent(reader));
            }

            return events;
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (ownsConnection)
            {
                connection.Dispose();
            }
        }
    }

    internal void InsertActivityEvent(SqliteTransaction transaction, ActivityEvent activityEvent)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO activity_events
                (id, timestamp, agent_id, realm, category, operation, target, status, duration_ms, metadata_json)
            VALUES ($id, $timestamp, $agent_id, $realm, $category, $operation, $target, $status, $duration_ms, $metadata_json);
            """;
        command.Parameters.AddWithValue("$id", activityEvent.Id);
        command.Parameters.AddWithValue("$timestamp", activityEvent.Timestamp.ToString("O"));
        command.Parameters.AddWithValue("$agent_id", (object?)activityEvent.AgentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$realm", (object?)activityEvent.Realm ?? DBNull.Value);
        command.Parameters.AddWithValue("$category", activityEvent.Category);
        command.Parameters.AddWithValue("$operation", activityEvent.Operation);
        command.Parameters.AddWithValue("$target", (object?)activityEvent.Target ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", activityEvent.Status);
        command.Parameters.AddWithValue("$duration_ms", (object?)activityEvent.DurationMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$metadata_json", activityEvent.MetadataJson);
        command.ExecuteNonQuery();
    }

    private static ActivityEvent ReadActivityEvent(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.GetString(9));

    private void Initialize()
    {
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        if (!ForeignKeysEnabled)
        {
            throw new HarnessDatabaseException("SQLite foreign key enforcement could not be enabled.");
        }

        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_at TEXT NOT NULL
                );
                """;
            schema.ExecuteNonQuery();
        }

        var migrations = Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Select(ParseMigration)
            .Where(migration => migration is not null)
            .Select(migration => migration!)
            .OrderBy(migration => migration.Version)
            .ToArray();

        if (migrations.Length == 0)
        {
            throw new HarnessDatabaseException("No embedded harness database migrations were found.");
        }

        var appliedMigrations = ReadAppliedMigrations();
        var embeddedVersions = migrations.Select(migration => migration.Version).ToHashSet();
        if (appliedMigrations.Keys.Any(version => !embeddedVersions.Contains(version)))
        {
            throw new HarnessDatabaseException("The database contains a migration that this harness does not know how to verify.");
        }

        if (migrations.GroupBy(migration => migration.Version).Any(group => group.Count() > 1))
        {
            throw new HarnessDatabaseException("Embedded migration versions must be unique.");
        }

        foreach (var migration in migrations)
        {
            if (appliedMigrations.TryGetValue(migration.Version, out var appliedName))
            {
                if (!string.Equals(appliedName, migration.Name, StringComparison.Ordinal))
                {
                    throw new HarnessDatabaseException($"Migration {migration.Version} was previously applied with a different name.");
                }

                continue;
            }

            if (appliedMigrations.Keys.Any(version => version > migration.Version))
            {
                throw new HarnessDatabaseException($"Migration {migration.Version} is missing before a later migration.");
            }

            ApplyMigration(migration);
            appliedMigrations[migration.Version] = migration.Name;
        }
    }

    private Dictionary<int, string> ReadAppliedMigrations()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version, name FROM schema_migrations ORDER BY version;";
        using var reader = command.ExecuteReader();
        var migrations = new Dictionary<int, string>();
        while (reader.Read())
        {
            migrations.Add(reader.GetInt32(0), reader.GetString(1));
        }

        return migrations;
    }

    private void ApplyMigration(Migration migration)
    {
        using var transaction = connection.BeginTransaction();
        using var migrationCommand = connection.CreateCommand();
        migrationCommand.Transaction = transaction;
        migrationCommand.CommandText = migration.Sql;
        migrationCommand.ExecuteNonQuery();

        using var record = connection.CreateCommand();
        record.Transaction = transaction;
        record.CommandText = """
            INSERT INTO schema_migrations (version, name, applied_at)
            VALUES ($version, $name, $applied_at);
            """;
        record.Parameters.AddWithValue("$version", migration.Version);
        record.Parameters.AddWithValue("$name", migration.Name);
        record.Parameters.AddWithValue("$applied_at", DateTimeOffset.UtcNow.ToString("O"));
        record.ExecuteNonQuery();
        transaction.Commit();
    }

    private static Migration? ParseMigration(string resourceName)
    {
        var match = MigrationNamePattern.Match(resourceName);
        if (!match.Success || !int.TryParse(match.Groups["version"].Value, out var version))
        {
            return null;
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new HarnessDatabaseException($"Embedded migration {resourceName} could not be loaded.");
        }

        using var reader = new StreamReader(stream);
        return new Migration(version, match.Groups["name"].Value, reader.ReadToEnd());
    }

    private static void EnsureSqliteProvider()
    {
        if (Interlocked.Exchange(ref sqliteProviderInitialized, 1) == 0)
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        }
    }

    private sealed record Migration(int Version, string Name, string Sql);
}

public sealed class HarnessDatabaseException(string message) : Exception(message);
