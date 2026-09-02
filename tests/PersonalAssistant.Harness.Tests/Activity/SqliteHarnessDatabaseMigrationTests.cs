using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Persistence;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Activity;

public sealed class SqliteHarnessDatabaseMigrationTests
{
    [Fact]
    public void Migration_003_backfills_timestamp_utc_ms_for_legacy_activity_rows()
    {
        ActivityTelemetry.ResetForTests();
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"pa-activity-migration-{Guid.NewGuid():N}.sqlite");
        using (var bootstrap = new SqliteHarnessDatabase(Path.Combine(
            Path.GetTempPath(),
            $"pa-bootstrap-{Guid.NewGuid():N}.sqlite")))
        {
            // Initialize the SQLite provider for subsequent raw connections in this test.
        }

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        ApplyEmbeddedMigrationsThrough(connection, 2);

        var legacyTimestamp = new DateTimeOffset(2026, 9, 1, 8, 30, 0, TimeSpan.FromHours(5));
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO activity_events
                    (id, timestamp, agent_id, realm, category, operation, target, status, duration_ms, metadata_json)
                VALUES ($id, $timestamp, $agent_id, $realm, $category, $operation, $target, $status, $duration_ms, $metadata_json);
                """;
            insert.Parameters.AddWithValue("$id", "legacy-event");
            insert.Parameters.AddWithValue("$timestamp", legacyTimestamp.ToString("O"));
            insert.Parameters.AddWithValue("$agent_id", "personal");
            insert.Parameters.AddWithValue("$realm", "personal");
            insert.Parameters.AddWithValue("$category", "agents");
            insert.Parameters.AddWithValue("$operation", "start");
            insert.Parameters.AddWithValue("$target", "runtime-session");
            insert.Parameters.AddWithValue("$status", "success");
            insert.Parameters.AddWithValue("$duration_ms", DBNull.Value);
            insert.Parameters.AddWithValue("$metadata_json", """{"eventType":"test.event","outcome":"observed"}""");
            insert.ExecuteNonQuery();
        }

        using var database = new SqliteHarnessDatabase(connection);
        using (var verify = connection.CreateCommand())
        {
            verify.CommandText = """
                SELECT timestamp_utc_ms, timestamp
                FROM activity_events
                WHERE id = 'legacy-event';
                """;
            using var reader = verify.ExecuteReader();
            Assert.True(reader.Read());
            Assert.False(reader.IsDBNull(0));
            Assert.Equal(
                legacyTimestamp.ToUniversalTime().ToUnixTimeMilliseconds(),
                reader.GetInt64(0));
            Assert.Equal(legacyTimestamp.ToUniversalTime().ToString("O"), reader.GetString(1));
        }

        var service = new ActivityQueryService(database);
        ActivityTelemetry.ResetForTests();
        var result = service.Query(new ActivityQueryRequest("2026-09-01", "UTC", null));

        Assert.Equal(1, result.Counters[ActivityCategoryKeys.AgentStarts]);
        Assert.Single(result.RecentEvents);
        Assert.False(result.AuditDegraded);

        using (var nullCheck = connection.CreateCommand())
        {
            nullCheck.CommandText = "SELECT 1 FROM activity_events WHERE timestamp_utc_ms IS NULL LIMIT 1;";
            Assert.Null(nullCheck.ExecuteScalar());
        }

        try
        {
            File.Delete(databasePath);
        }
        catch (IOException)
        {
            // Best-effort cleanup for temp proof databases.
        }
    }

    private static void ApplyEmbeddedMigrationsThrough(SqliteConnection connection, int maxVersion)
    {
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

        var harnessAssembly = typeof(SqliteHarnessDatabase).Assembly;
        var migrations = harnessAssembly
            .GetManifestResourceNames()
            .Select(ParseMigrationResource)
            .Where(migration => migration is not null)
            .Select(migration => migration!)
            .Where(migration => migration.Version <= maxVersion)
            .OrderBy(migration => migration.Version)
            .ToArray();

        foreach (var migration in migrations)
        {
            using var transaction = connection.BeginTransaction();
            using (var migrationCommand = connection.CreateCommand())
            {
                migrationCommand.Transaction = transaction;
                migrationCommand.CommandText = migration.Sql;
                migrationCommand.ExecuteNonQuery();
            }

            using (var record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = """
                    INSERT INTO schema_migrations (version, name, applied_at)
                    VALUES ($version, $name, $applied_at);
                    """;
                record.Parameters.AddWithValue("$version", migration.Version);
                record.Parameters.AddWithValue("$name", migration.Name);
                record.Parameters.AddWithValue("$applied_at", DateTimeOffset.UtcNow.ToString("O"));
                record.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    private static MigrationResource? ParseMigrationResource(string resourceName)
    {
        var match = Regex.Match(
            resourceName,
            @"\.Migrations\.(?<version>[0-9]+)_(?<name>[^.]+)\.sql$",
            RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups["version"].Value, out var version))
        {
            return null;
        }

        using var stream = typeof(SqliteHarnessDatabase).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return new MigrationResource(version, match.Groups["name"].Value, reader.ReadToEnd());
    }

    private sealed record MigrationResource(int Version, string Name, string Sql);
}
