using PersonalAssistant.Harness.Bootstrap;
using PersonalAssistant.Harness.Persistence;
using PersonalAssistant.Harness.Policies;
using PersonalAssistant.Harness.Settings;

namespace PersonalAssistant.Harness;

public sealed class HarnessRuntime : IDisposable
{
    private readonly ISettingsOverrideStore store;

    private HarnessRuntime(SettingsService settings, ISettingsOverrideStore store, BootstrapConfiguration bootstrap)
    {
        Settings = settings;
        this.store = store;
        Bootstrap = bootstrap;
    }

    public SettingsService Settings { get; }
    public BootstrapConfiguration Bootstrap { get; }

    public static HarnessRuntime Create(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? baseDirectory = null,
        string? homeDirectory = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var bootstrap = BootstrapResolver.Resolve(root, environment, baseDirectory, homeDirectory);
        var defaults = RepositoryDefaultsLoader.Load(Path.Combine(root, "policies", "defaults", "runtime.yaml"));
        var policies = PolicyLoader.Load(
            Path.Combine(root, "policies", "defaults", "capability-policy.yaml"),
            Path.Combine(root, "policies", "defaults", "realm-policy.yaml"),
            defaults.CheckpointBeforeRotation);
        var context = new SettingsContext(root, bootstrap, defaults, policies);
        var databasePath = Path.Combine(bootstrap.RuntimeDirectory, "personal-assistant.sqlite");
        var store = new SqliteSettingsOverrideStore(databasePath);
        try
        {
            var service = new SettingsService(SettingsRegistry.CreateDefault(), context, store);
            _ = service.GetSnapshot();
            return new HarnessRuntime(service, store, bootstrap);
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    public void Dispose() => store.Dispose();
}
