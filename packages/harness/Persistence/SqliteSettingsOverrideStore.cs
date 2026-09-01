using Microsoft.Data.Sqlite;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Settings;

namespace PersonalAssistant.Harness.Persistence;

public sealed class SqliteSettingsOverrideStore : ISettingsOverrideStore
{
    private readonly SqliteHarnessDatabase database;
    private readonly bool ownsDatabase;

    public SqliteSettingsOverrideStore(string databasePath)
    {
        database = new SqliteHarnessDatabase(databasePath);
        ownsDatabase = true;
    }

    public SqliteSettingsOverrideStore(SqliteHarnessDatabase database)
    {
        this.database = database;
        ownsDatabase = false;
    }

    public SqliteSettingsOverrideStore(SqliteConnection openConnection, bool ownsConnection = false)
    {
        database = new SqliteHarnessDatabase(openConnection, ownsConnection);
        ownsDatabase = true;
    }

    public IReadOnlyDictionary<string, string> ReadGlobalOverrides()
    {
        lock (database.SyncRoot)
        {
            using var command = database.Connection.CreateCommand();
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
        database.ExecuteInTransaction(transaction =>
        {
            foreach (var change in changes)
            {
                using var command = database.Connection.CreateCommand();
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
                database.InsertActivityEvent(transaction, activityEvent);
            }
        });
    }

    public IReadOnlyList<ActivityEvent> ReadActivityEvents() => database.ReadActivityEvents();

    public void Dispose()
    {
        if (ownsDatabase)
        {
            database.Dispose();
        }
    }
}
