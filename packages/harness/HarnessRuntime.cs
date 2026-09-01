using PersonalAssistant.Harness.Bootstrap;
using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Persistence;
using PersonalAssistant.Harness.Policies;
using PersonalAssistant.Harness.Runtime;
using PersonalAssistant.Harness.Settings;

namespace PersonalAssistant.Harness;

public sealed class HarnessRuntime : IDisposable
{
    private readonly SqliteHarnessDatabase database;
    private readonly TmuxTerminalStream terminalStream;

    private HarnessRuntime(
        SettingsService settings,
        SqliteHarnessDatabase database,
        BootstrapConfiguration bootstrap,
        IAgentSessionService agents,
        TmuxTerminalStream terminalStream)
    {
        Settings = settings;
        this.database = database;
        Bootstrap = bootstrap;
        Agents = agents;
        this.terminalStream = terminalStream;
    }

    public SettingsService Settings { get; }
    public BootstrapConfiguration Bootstrap { get; }
    public IAgentSessionService Agents { get; }
    public TmuxTerminalStream TerminalStream => terminalStream;

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
        var database = new SqliteHarnessDatabase(databasePath);
        TmuxTerminalStream? terminalStream = null;
        try
        {
            var store = new SqliteSettingsOverrideStore(database);
            var service = new SettingsService(SettingsRegistry.CreateDefault(), context, store);
            _ = service.GetSnapshot();
            var registry = new AgentRegistry(root, bootstrap.TmuxPrefix);
            _ = registry.LoadPersonal();
            var agentStore = new SqliteAgentSessionStore(database);
            var tmux = new TmuxSessionManager(bootstrap.TmuxPrefix);
            var agents = new AgentSessionService(registry, agentStore, tmux, new ClaudeRuntimeAdapter(tmux));
            terminalStream = new TmuxTerminalStream(tmux, bootstrap.RuntimeDirectory);
            var runtime = new HarnessRuntime(service, database, bootstrap, agents, terminalStream);
            agents.ReconcilePersonal();
            return runtime;
        }
        catch
        {
            terminalStream?.Dispose();
            database.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        terminalStream.Dispose();
        database.Dispose();
    }
}
