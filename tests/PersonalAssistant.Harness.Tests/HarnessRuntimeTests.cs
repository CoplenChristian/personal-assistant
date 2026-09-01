using System.Text.Json;
using PersonalAssistant.Harness.Persistence;
using PersonalAssistant.Harness.Settings;
using Xunit;

namespace PersonalAssistant.Harness.Tests;

public sealed class HarnessRuntimeTests
{
    [Fact]
    public void Create_fails_before_returning_for_an_invalid_persisted_override()
    {
        var runtimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-runtime-").FullName;
        try
        {
            var databasePath = Path.Combine(runtimeDirectory, "personal-assistant.sqlite");
            SeedOverrides(databasePath, new Dictionary<string, string?>
            {
                ["appearance.theme"] = "not-json"
            });

            var exception = Assert.Throws<SettingsStoreException>(() => HarnessRuntime.Create(
                FindRepositoryRoot(),
                ValidEnvironment(runtimeDirectory),
                FindRepositoryRoot(),
                runtimeDirectory));

            Assert.Equal("settings_store_invalid", exception.Code);
            using var reopenedStore = new SqliteSettingsOverrideStore(databasePath);
            Assert.Single(reopenedStore.ReadGlobalOverrides());
        }
        finally
        {
            if (Directory.Exists(runtimeDirectory))
            {
                Directory.Delete(runtimeDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Create_fails_before_returning_for_an_invalid_persisted_cross_setting_state()
    {
        var runtimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-runtime-").FullName;
        try
        {
            var databasePath = Path.Combine(runtimeDirectory, "personal-assistant.sqlite");
            SeedOverrides(databasePath, new Dictionary<string, string?>
            {
                ["sessions.nativeSessionWarningBytes"] = JsonSerializer.Serialize(1_000L),
                ["sessions.nativeSessionRotateBytes"] = JsonSerializer.Serialize(500L)
            });

            var exception = Assert.Throws<SettingsException>(() => HarnessRuntime.Create(
                FindRepositoryRoot(),
                ValidEnvironment(runtimeDirectory),
                FindRepositoryRoot(),
                runtimeDirectory));

            Assert.Equal("cross_setting_invalid", exception.Code);
        }
        finally
        {
            if (Directory.Exists(runtimeDirectory))
            {
                Directory.Delete(runtimeDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Create_fails_before_returning_for_an_invalid_cross_setting_yaml_default()
    {
        var repositoryRoot = CreateRepositoryFixture();
        var runtimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-runtime-").FullName;
        try
        {
            var runtimeDefaultsPath = Path.Combine(repositoryRoot, "policies", "defaults", "runtime.yaml");
            var runtimeDefaults = File.ReadAllText(runtimeDefaultsPath)
                .Replace("rotate_bytes: 52428800", "rotate_bytes: 1000", StringComparison.Ordinal);
            File.WriteAllText(runtimeDefaultsPath, runtimeDefaults);

            var exception = Assert.Throws<SettingsException>(() => HarnessRuntime.Create(
                repositoryRoot,
                ValidEnvironment(runtimeDirectory),
                repositoryRoot,
                runtimeDirectory));

            Assert.Equal("cross_setting_invalid", exception.Code);
        }
        finally
        {
            if (Directory.Exists(runtimeDirectory))
            {
                Directory.Delete(runtimeDirectory, recursive: true);
            }

            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    private static void SeedOverrides(string databasePath, IReadOnlyDictionary<string, string?> overrides)
    {
        using var store = new SqliteSettingsOverrideStore(databasePath);
        store.ApplyAtomic(overrides, null);
    }

    private static IReadOnlyDictionary<string, string?> ValidEnvironment(string runtimeDirectory) =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PA_RUNTIME_DIR"] = runtimeDirectory,
            ["PA_SERVER_HOST"] = "127.0.0.1",
            ["PA_SERVER_PORT"] = "4317",
            ["PA_TMUX_PREFIX"] = "test-pa-",
            ["PA_VAULT_DIR"] = Path.Combine(runtimeDirectory, "vault")
        };

    private static string CreateRepositoryFixture()
    {
        var sourceRoot = FindRepositoryRoot();
        var repositoryRoot = Directory.CreateTempSubdirectory("personal-assistant-repository-").FullName;
        var defaultsDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "policies", "defaults"));
        foreach (var fileName in new[] { "runtime.yaml", "capability-policy.yaml", "realm-policy.yaml" })
        {
            File.Copy(
                Path.Combine(sourceRoot, "policies", "defaults", fileName),
                Path.Combine(defaultsDirectory.FullName, fileName));
        }

        return repositoryRoot;
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

        throw new InvalidOperationException("Unable to find repository root for harness runtime tests.");
    }
}
