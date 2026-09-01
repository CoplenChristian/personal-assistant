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
    private readonly TmuxSessionManager tmux;
    private readonly TmuxTerminalStream terminalStream;
    private readonly TerminalInputSerializer terminalInput;
    private readonly TerminalActivityStateTracker terminalState;

    private HarnessRuntime(
        SettingsService settings,
        SqliteHarnessDatabase database,
        BootstrapConfiguration bootstrap,
        IAgentSessionService agents,
        TmuxSessionManager tmux,
        TmuxTerminalStream terminalStream,
        TerminalInputSerializer terminalInput,
        TerminalActivityStateTracker terminalState)
    {
        Settings = settings;
        this.database = database;
        Bootstrap = bootstrap;
        Agents = agents;
        this.tmux = tmux;
        this.terminalStream = terminalStream;
        this.terminalInput = terminalInput;
        this.terminalState = terminalState;
    }

    public SettingsService Settings { get; }
    public BootstrapConfiguration Bootstrap { get; }
    public IAgentSessionService Agents { get; }
    public TmuxSessionManager Tmux => tmux;
    public TmuxTerminalStream TerminalStream => terminalStream;
    public TerminalInputSerializer TerminalInput => terminalInput;
    public TerminalActivityStateTracker TerminalState => terminalState;

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
        TerminalInputSerializer? terminalInput = null;
        TerminalActivityStateTracker? terminalState = null;
        try
        {
            var store = new SqliteSettingsOverrideStore(database);
            var service = new SettingsService(SettingsRegistry.CreateDefault(), context, store);
            _ = service.GetSnapshot();
            var registry = new AgentRegistry(root, bootstrap.TmuxPrefix);
            var personalDefinition = registry.LoadPersonal();
            var agentStore = new SqliteAgentSessionStore(database);
            var tmux = new TmuxSessionManager(bootstrap.TmuxPrefix);
            var agents = new AgentSessionService(registry, agentStore, tmux, new ClaudeRuntimeAdapter(tmux));
            terminalStream = new TmuxTerminalStream(tmux, bootstrap.RuntimeDirectory);
            terminalInput = new TerminalInputSerializer(
                personalDefinition.Id,
                (request, cancellationToken) => tmux.SendLiteralInputAsync(
                    personalDefinition.TmuxSessionName,
                    request.Data,
                    cancellationToken));
            terminalState = new TerminalActivityStateTracker(personalDefinition.Id);
            var runtime = new HarnessRuntime(service, database, bootstrap, agents, tmux, terminalStream, terminalInput, terminalState);
            agents.ReconcilePersonal();
            return runtime;
        }
        catch
        {
            terminalState?.Dispose();
            terminalInput?.Dispose();
            terminalStream?.Dispose();
            database.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        terminalInput.Dispose();
        terminalState.Dispose();
        terminalStream.Dispose();
        database.Dispose();
    }
}
