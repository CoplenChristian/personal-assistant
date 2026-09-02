using System.Globalization;
using PersonalAssistant.Harness.Bootstrap;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Memory;
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
    private readonly ICheckpointCoordinator checkpointCoordinator;
    private readonly ISessionHygieneService sessionHygiene;
    private readonly IActivityEventSink activitySink;
    private readonly ActivityQueryService activityQuery;

    private HarnessRuntime(
        SettingsService settings,
        SqliteHarnessDatabase database,
        BootstrapConfiguration bootstrap,
        IAgentSessionService agents,
        TmuxSessionManager tmux,
        TmuxTerminalStream terminalStream,
        TerminalInputSerializer terminalInput,
        TerminalActivityStateTracker terminalState,
        ICheckpointCoordinator checkpointCoordinator,
        ISessionHygieneService sessionHygiene,
        IActivityEventSink activitySink,
        ActivityQueryService activityQuery)
    {
        Settings = settings;
        this.database = database;
        Bootstrap = bootstrap;
        Agents = agents;
        this.tmux = tmux;
        this.terminalStream = terminalStream;
        this.terminalInput = terminalInput;
        this.terminalState = terminalState;
        this.checkpointCoordinator = checkpointCoordinator;
        this.sessionHygiene = sessionHygiene;
        this.activitySink = activitySink;
        this.activityQuery = activityQuery;
    }

    public SettingsService Settings { get; }
    public BootstrapConfiguration Bootstrap { get; }
    public IAgentSessionService Agents { get; }
    public TmuxSessionManager Tmux => tmux;
    public TmuxTerminalStream TerminalStream => terminalStream;
    public TerminalInputSerializer TerminalInput => terminalInput;
    public TerminalActivityStateTracker TerminalState => terminalState;
    public ICheckpointCoordinator Checkpoints => checkpointCoordinator;
    public ISessionHygieneService SessionHygiene => sessionHygiene;
    public IActivityEventSink ActivitySink => activitySink;
    public ActivityQueryService ActivityQuery => activityQuery;

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
        SessionHygieneService? sessionHygiene = null;
        try
        {
            var store = new SqliteSettingsOverrideStore(database);
            var service = new SettingsService(SettingsRegistry.CreateDefault(), context, store);
            var settingsSnapshot = service.GetSnapshot();
            var registry = new AgentRegistry(root, bootstrap.TmuxPrefix);
            var personalDefinition = registry.LoadPersonal();
            var agentStore = new SqliteAgentSessionStore(database);
            var tmux = new TmuxSessionManager(bootstrap.TmuxPrefix);
            var claude = new ClaudeRuntimeAdapter(tmux);
            var codex = new CodexRuntimeAdapter(tmux);
            var runtimeAdapters = new RuntimeAdapterResolver(
            [
                new KeyValuePair<string, IAgentRuntimeAdapter>("claude", claude),
                new KeyValuePair<string, IAgentRuntimeAdapter>("codex", codex)
            ]);
            var agents = new AgentSessionService(registry, agentStore, tmux, runtimeAdapters);
            var activitySink = new SqliteActivityEventSink(database);
            var activityQuery = new ActivityQueryService(database);
            var checkpointCoordinator = new CheckpointCoordinator(root, bootstrap.RuntimeDirectory, activitySink);
            sessionHygiene = new SessionHygieneService(registry, agentStore, claude, checkpointCoordinator, activitySink);
            var terminalWarningBytes = ReadInt64Setting(settingsSnapshot, "sessions.terminalLogWarningBytes");
            var configuredRotationBytes = ReadInt64Setting(settingsSnapshot, "sessions.nativeSessionRotateBytes");
            var terminalRotationBytes = Math.Max(terminalWarningBytes + 1, configuredRotationBytes);
            var retainedLogFiles = checked((int)ReadInt64Setting(settingsSnapshot, "sessions.terminalLogRotatedFiles"));
            var terminalLogWriter = new TerminalLogWriter(
                bootstrap.RuntimeDirectory,
                personalDefinition.Id,
                terminalWarningBytes,
                terminalRotationBytes,
                retainedLogFiles,
                activitySink,
                personalDefinition.Realms.FirstOrDefault());
            terminalStream = new TmuxTerminalStream(tmux, bootstrap.RuntimeDirectory, terminalLogWriter);
            terminalInput = new TerminalInputSerializer(
                personalDefinition.Id,
                (request, cancellationToken) => tmux.SendLiteralInputAsync(
                    personalDefinition.TmuxSessionName,
                    request.Data,
                    cancellationToken));
            terminalState = new TerminalActivityStateTracker(personalDefinition.Id);
            var runtime = new HarnessRuntime(
                service,
                database,
                bootstrap,
                agents,
                tmux,
                terminalStream,
                terminalInput,
                terminalState,
                checkpointCoordinator,
                sessionHygiene,
                activitySink,
                activityQuery);
            agents.ReconcilePersonal();
            return runtime;
        }
        catch
        {
            terminalState?.Dispose();
            terminalInput?.Dispose();
            terminalStream?.Dispose();
            sessionHygiene?.Dispose();
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

    private static long ReadInt64Setting(SettingsSnapshot snapshot, string key) =>
        Convert.ToInt64(
            snapshot.Settings.Single(setting => string.Equals(setting.Key, key, StringComparison.Ordinal)).Value,
            CultureInfo.InvariantCulture);
}
