using Microsoft.Data.Sqlite;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Settings;
using System.Reflection;

namespace PersonalAssistant.Harness.Persistence;

public sealed class SqliteSettingsOverrideStore : ISettingsOverrideStore
{
    private static int sqliteProviderInitialized;
    private readonly object syncRoot = new();
    private readonly SqliteConnection connection;
    private readonly bool ownsConnection;

    public SqliteSettingsOverrideStore(string databasePath)
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
        EnsureSchema();
    }

    public SqliteSettingsOverrideStore(SqliteConnection openConnection, bool ownsConnection = false)
    {
        EnsureSqliteProvider();
        connection = openConnection;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        this.ownsConnection = ownsConnection;
        EnsureSchema();
    }

    public IReadOnlyDictionary<string, string> ReadGlobalOverrides()
    {
        lock (syncRoot)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT scope_type, scope_id, key, value_json FROM settings_overrides";
            using var reader = command.ExecuteReader();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                var scopeType = reader.GetString(0);
                var scopeId = reader.GetString(1);
                if (!string.Equals(scopeType, "global", StringComparison.Ordinal) || scopeId.Length != 0)
                {
                    throw new SettingsStoreException("Non-global settings rows are not supported in Phase 0A.");
                }

                values.Add(reader.GetString(2), reader.GetString(3));
            }

            return values;
        }
    }

    public void ApplyAtomic(IReadOnlyDictionary<string, string?> changes, ActivityEvent? activityEvent)
    {
        lock (syncRoot)
        {
            using var transaction = connection.BeginTransaction();
            foreach (var change in changes)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                if (change.Value is null)
                {
                    command.CommandText = "DELETE FROM settings_overrides WHERE scope_type = 'global' AND scope_id = '' AND key = $key";
                    command.Parameters.AddWithValue("$key", change.Key);
                }
                else
                {
                    command.CommandText = """
                        INSERT INTO settings_overrides (scope_type, scope_id, key, value_json, updated_at)
                        VALUES ('global', '', $key, $value, $updated_at)
                        ON CONFLICT(scope_type, scope_id, key)
                        DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at;
                        """;
                    command.Parameters.AddWithValue("$key", change.Key);
                    command.Parameters.AddWithValue("$value", change.Value);
                    command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
                }

                command.ExecuteNonQuery();
            }

            if (activityEvent is not null)
            {
                using var activityCommand = connection.CreateCommand();
                activityCommand.Transaction = transaction;
                activityCommand.CommandText = """
                    INSERT INTO activity_events
                        (id, timestamp, agent_id, realm, category, operation, target, status, duration_ms, metadata_json)
                    VALUES ($id, $timestamp, $agent_id, $realm, $category, $operation, $target, $status, $duration_ms, $metadata_json);
                    """;
                activityCommand.Parameters.AddWithValue("$id", activityEvent.Id);
                activityCommand.Parameters.AddWithValue("$timestamp", activityEvent.Timestamp.ToString("O"));
                activityCommand.Parameters.AddWithValue("$agent_id", (object?)activityEvent.AgentId ?? DBNull.Value);
                activityCommand.Parameters.AddWithValue("$realm", (object?)activityEvent.Realm ?? DBNull.Value);
                activityCommand.Parameters.AddWithValue("$category", activityEvent.Category);
                activityCommand.Parameters.AddWithValue("$operation", activityEvent.Operation);
                activityCommand.Parameters.AddWithValue("$target", (object?)activityEvent.Target ?? DBNull.Value);
                activityCommand.Parameters.AddWithValue("$status", activityEvent.Status);
                activityCommand.Parameters.AddWithValue("$duration_ms", (object?)activityEvent.DurationMs ?? DBNull.Value);
                activityCommand.Parameters.AddWithValue("$metadata_json", activityEvent.MetadataJson);
                activityCommand.ExecuteNonQuery();
            }

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
                events.Add(new ActivityEvent(
                    reader.GetString(0),
                    DateTimeOffset.Parse(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    reader.GetString(9)));
            }

            return events;
        }
    }

    public void Dispose()
    {
        if (ownsConnection)
        {
            connection.Dispose();
        }
    }

    private void EnsureSchema()
    {
        using var migration = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "PersonalAssistant.Harness.Persistence.Migrations.001_settings_overrides.sql");
        if (migration is null)
        {
            throw new InvalidOperationException("The settings database migration is missing from the harness assembly.");
        }

        using var migrationReader = new StreamReader(migration);
        using var command = connection.CreateCommand();
        command.CommandText = migrationReader.ReadToEnd();
        command.ExecuteNonQuery();
    }

    private static void EnsureSqliteProvider()
    {
        if (Interlocked.Exchange(ref sqliteProviderInitialized, 1) == 0)
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        }
    }
}
