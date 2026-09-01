using Microsoft.Data.Sqlite;
using PersonalAssistant.Harness;
using PersonalAssistant.Harness.Bootstrap;
using PersonalAssistant.Harness.Persistence;
using PersonalAssistant.Harness.Policies;
using PersonalAssistant.Harness.Settings;

namespace PersonalAssistant.Harness.Tests;

public sealed class SettingsTestContext : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly string tempDirectory;

    public SettingsTestContext()
    {
        RepositoryRoot = FindRepositoryRoot();
        tempDirectory = Directory.CreateTempSubdirectory("personal-assistant-settings-").FullName;
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PA_RUNTIME_DIR"] = tempDirectory,
            ["PA_SERVER_HOST"] = "127.0.0.1",
            ["PA_SERVER_PORT"] = "4317",
            ["PA_TMUX_PREFIX"] = "test-pa-",
            ["PA_VAULT_DIR"] = Path.Combine(tempDirectory, "vault")
        };
        Bootstrap = BootstrapResolver.Resolve(RepositoryRoot, environment, RepositoryRoot, tempDirectory);
        Defaults = RepositoryDefaultsLoader.Load(Path.Combine(RepositoryRoot, "policies", "defaults", "runtime.yaml"));
        Policies = PolicyLoader.Load(
            Path.Combine(RepositoryRoot, "policies", "defaults", "capability-policy.yaml"),
            Path.Combine(RepositoryRoot, "policies", "defaults", "realm-policy.yaml"),
            Defaults.CheckpointBeforeRotation);
        SettingsContext = new SettingsContext(RepositoryRoot, Bootstrap, Defaults, Policies);
        connection = new SqliteConnection("Data Source=:memory:");
        Store = new SqliteSettingsOverrideStore(connection);
        Registry = SettingsRegistry.CreateDefault();
    }

    public string RepositoryRoot { get; }
    public BootstrapConfiguration Bootstrap { get; }
    public RuntimeDefaults Defaults { get; }
    public PolicySnapshot Policies { get; }
    public SettingsContext SettingsContext { get; }
    public SqliteSettingsOverrideStore Store { get; }
    public SettingsRegistry Registry { get; }

    public SettingsService CreateService(SettingsRegistry? registry = null) =>
        new(registry ?? Registry, SettingsContext, Store);

    public void SeedOverride(string scopeType, string scopeId, string key, string valueJson)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings_overrides (scope_type, scope_id, key, value_json, updated_at)
            VALUES ($scope_type, $scope_id, $key, $value_json, $updated_at);
            """;
        command.Parameters.AddWithValue("$scope_type", scopeType);
        command.Parameters.AddWithValue("$scope_id", scopeId);
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value_json", valueJson);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Store.Dispose();
        connection.Dispose();
        Directory.Delete(tempDirectory, recursive: true);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "policies", "defaults", "runtime.yaml")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to find repository root for settings tests.");
    }
}
