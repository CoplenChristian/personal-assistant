using Microsoft.Data.Sqlite;
using System.Text.Json;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Persistence;
using PersonalAssistant.Harness.Runtime;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Agents;

public sealed class AgentSessionServiceTests
{
    [Fact]
    public void Database_enables_foreign_keys_and_applies_ordered_migrations_once()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var database = new SqliteHarnessDatabase(connection);

        Assert.True(database.ForeignKeysEnabled);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_migrations ORDER BY version;";
        using var reader = command.ExecuteReader();
        var versions = new List<int>();
        while (reader.Read())
        {
            versions.Add(reader.GetInt32(0));
        }

        Assert.Equal([1, 2], versions);
    }

    [Fact]
    public void Database_enforces_foreign_keys_and_one_session_per_agent()
    {
        using var fixture = new AgentFixture();
        fixture.Store.EnsureAgent(fixture.Definition);
        using var command = fixture.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sessions (id, agent_id, tmux_session_name, runtime, observed_state)
            VALUES ('second', 'personal', 'test-pa-second', 'claude', 'missing');
            """;

        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public void Agent_and_session_state_survive_a_new_database_service_instance()
    {
        var directory = Directory.CreateTempSubdirectory("personal-assistant-agent-db-").FullName;
        var databasePath = Path.Combine(directory, "personal-assistant.sqlite");
        try
        {
            var definition = new AgentRegistry(FindRepositoryRoot(), "test-pa-").LoadPersonal();
            using (var firstDatabase = new SqliteHarnessDatabase(databasePath))
            {
                var firstStore = new SqliteAgentSessionStore(firstDatabase);
                firstStore.EnsureAgent(definition);
                firstStore.SetDesiredState(definition.Id, AgentDesiredState.Running);
                firstStore.RecordObservation(definition, SessionObservedState.Running, null, null, desiredState: AgentDesiredState.Running);
            }

            using var secondDatabase = new SqliteHarnessDatabase(databasePath);
            var secondStore = new SqliteAgentSessionStore(secondDatabase);
            var reloaded = secondStore.ReadStatus(definition);

            Assert.Equal(AgentDesiredState.Running, reloaded.DesiredState);
            Assert.Equal(SessionObservedState.Running, reloaded.Session.ObservedState);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Lifecycle_state_rolls_back_when_its_activity_insert_fails()
    {
        using var fixture = new AgentFixture();
        fixture.Store.EnsureAgent(fixture.Definition);
        var activity = new ActivityEvent(
            "duplicate-event",
            DateTimeOffset.UtcNow,
            fixture.Definition.Id,
            "personal",
            "agents",
            "start",
            fixture.Definition.TmuxSessionName,
            "success",
            null,
            "{}");
        using var activityStore = new SqliteSettingsOverrideStore(fixture.Database);
        activityStore.ApplyAtomic(new Dictionary<string, string?>(), activity);

        Assert.Throws<SqliteException>(() => fixture.Store.RecordObservation(
            fixture.Definition,
            SessionObservedState.Running,
            null,
            activity,
            desiredState: AgentDesiredState.Running));
        var status = fixture.Store.ReadStatus(fixture.Definition);

        Assert.Equal(AgentDesiredState.Stopped, status.DesiredState);
        Assert.Equal(SessionObservedState.Missing, status.Session.ObservedState);
    }

    [Fact]
    public void First_registration_uses_auto_start_but_reload_preserves_explicit_stop()
    {
        using var fixture = new AgentFixture();
        var initial = fixture.Store.EnsureAgent(fixture.Definition);

        Assert.Equal(AgentDesiredState.Stopped, initial.DesiredState);
        fixture.Store.SetDesiredState(fixture.Definition.Id, AgentDesiredState.Running);
        fixture.Store.SetDesiredState(fixture.Definition.Id, AgentDesiredState.Stopped);

        var reloaded = fixture.Store.EnsureAgent(fixture.Definition);

        Assert.Equal(AgentDesiredState.Stopped, reloaded.DesiredState);
        Assert.Equal(SessionObservedState.Missing, reloaded.Session.ObservedState);
    }

    [Fact]
    public void First_registration_can_initialize_running_from_auto_start()
    {
        using var fixture = new AgentFixture();
        var autoStartDefinition = fixture.Definition with { AutoStart = true };

        var status = fixture.Store.EnsureAgent(autoStartDefinition);

        Assert.Equal(AgentDesiredState.Running, status.DesiredState);
        Assert.Equal(SessionObservedState.Missing, status.Session.ObservedState);
    }

    [Fact]
    public void Start_creates_one_session_and_repeated_start_adopts_healthy_runtime()
    {
        using var fixture = new AgentFixture();
        var first = fixture.Service.StartPersonal();

        var respawnsAfterFirstStart = fixture.Tmux.Commands.Count(command => command[0] == "respawn-pane");
        var createsAfterFirstStart = fixture.Tmux.Commands.Count(command => command[0] == "new-session");
        var second = fixture.Service.StartPersonal();

        Assert.Equal(AgentDesiredState.Running, first.DesiredState);
        Assert.Equal(SessionObservedState.Running, first.Session.ObservedState);
        Assert.True(first.RuntimeHealthy);
        Assert.Equal(SessionObservedState.Running, second.Session.ObservedState);
        Assert.Equal(createsAfterFirstStart, fixture.Tmux.Commands.Count(command => command[0] == "new-session"));
        Assert.Equal(respawnsAfterFirstStart, fixture.Tmux.Commands.Count(command => command[0] == "respawn-pane"));
        Assert.Equal(2, fixture.Database.ReadActivityEvents().Count(eventItem => eventItem.Operation == "start"));
    }

    [Fact]
    public void Stop_retains_logical_agent_session_and_activity_history()
    {
        using var fixture = new AgentFixture();
        fixture.Service.StartPersonal();
        var stopped = fixture.Service.StopPersonal();
        var reloaded = fixture.Store.ReadStatus(fixture.Definition);

        Assert.Equal(AgentDesiredState.Stopped, stopped.DesiredState);
        Assert.Equal(SessionObservedState.Exited, stopped.Session.ObservedState);
        Assert.False(stopped.RuntimeHealthy);
        Assert.Equal(AgentDesiredState.Stopped, reloaded.DesiredState);
        Assert.Equal(SessionObservedState.Exited, reloaded.Session.ObservedState);
        Assert.Contains(fixture.Database.ReadActivityEvents(), eventItem => eventItem.Operation == "stop");
    }

    [Fact]
    public void Failed_stop_retains_stopped_intent_and_records_stopped_at()
    {
        using var fixture = new AgentFixture();
        fixture.Service.StartPersonal();
        fixture.Tmux.FailKill = true;

        Assert.ThrowsAny<AgentLifecycleException>(() => fixture.Service.StopPersonal());
        var status = fixture.Store.ReadStatus(fixture.Definition);

        Assert.Equal(AgentDesiredState.Stopped, status.DesiredState);
        Assert.Equal(SessionObservedState.Error, status.Session.ObservedState);
        Assert.NotNull(status.Session.StoppedAt);
    }

    [Fact]
    public void Reconcile_adopts_existing_healthy_claude_without_relaunching()
    {
        using var fixture = new AgentFixture();
        fixture.Store.EnsureAgent(fixture.Definition);
        fixture.Store.SetDesiredState(fixture.Definition.Id, AgentDesiredState.Running);
        fixture.Tmux.SessionExists = true;
        fixture.Tmux.NativeProcess = true;

        var status = fixture.Service.ReconcilePersonal();

        Assert.True(status.RuntimeHealthy);
        Assert.Equal(SessionObservedState.Running, status.Session.ObservedState);
        Assert.DoesNotContain(fixture.Tmux.Commands, command => command[0] is "new-session" or "respawn-pane");
        Assert.Contains("session.reconciled", fixture.Database.ReadActivityEvents().Single().MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Reconcile_adopts_versioned_claude_without_relaunching_or_killing_it()
    {
        using var fixture = new AgentFixture();
        fixture.Store.EnsureAgent(fixture.Definition);
        fixture.Store.SetDesiredState(fixture.Definition.Id, AgentDesiredState.Running);
        fixture.Tmux.SessionExists = true;
        fixture.Tmux.NativeProcess = true;
        fixture.Tmux.ProcessIdentityMatches = false;
        fixture.Tmux.PaneStartCommand = "claude";
        fixture.Tmux.PaneCurrentCommand = "2.1.112";

        var status = fixture.Service.ReconcilePersonal();

        Assert.True(status.RuntimeHealthy);
        Assert.Equal(SessionObservedState.Running, status.Session.ObservedState);
        Assert.DoesNotContain(fixture.Tmux.Commands, command => command[0] == "respawn-pane");
        var healthCommand = fixture.Tmux.Commands.Single(command => command[0] == "list-panes");
        Assert.Contains("#{pane_pid}", healthCommand[4], StringComparison.Ordinal);
        Assert.Contains("#{pane_dead}", healthCommand[4], StringComparison.Ordinal);
        Assert.Contains("#{pane_start_command}", healthCommand[4], StringComparison.Ordinal);
    }

    [Fact]
    public void Reconcile_does_not_destructively_repair_a_live_unverified_pane()
    {
        using var fixture = new AgentFixture();
        fixture.Store.EnsureAgent(fixture.Definition);
        fixture.Store.SetDesiredState(fixture.Definition.Id, AgentDesiredState.Running);
        fixture.Tmux.SessionExists = true;
        fixture.Tmux.NativeProcess = true;
        fixture.Tmux.PaneStartCommand = string.Empty;
        fixture.Tmux.PaneCurrentCommand = "2.1.112";

        var status = fixture.Service.ReconcilePersonal();
        var health = fixture.TmuxManager.GetHealth(fixture.Definition.TmuxSessionName, "claude");

        Assert.False(status.RuntimeHealthy);
        Assert.Equal(SessionObservedState.Error, status.Session.ObservedState);
        Assert.False(health.RepairEligible);
        Assert.DoesNotContain(fixture.Tmux.Commands, command => command[0] == "respawn-pane");
    }

    [Fact]
    public void Start_refuses_to_kill_a_live_unverified_pane()
    {
        using var fixture = new AgentFixture();
        fixture.Tmux.SessionExists = true;
        fixture.Tmux.NativeProcess = true;
        fixture.Tmux.PaneStartCommand = string.Empty;
        fixture.Tmux.PaneCurrentCommand = "2.1.112";

        Assert.ThrowsAny<AgentLifecycleException>(() => fixture.Service.StartPersonal());
        var status = fixture.Store.ReadStatus(fixture.Definition);

        Assert.Equal(AgentDesiredState.Running, status.DesiredState);
        Assert.Equal(SessionObservedState.Error, status.Session.ObservedState);
        Assert.DoesNotContain(fixture.Tmux.Commands, command => command[0] == "respawn-pane");
    }

    [Fact]
    public void Reconcile_recreates_a_missing_session_when_desired_state_is_running()
    {
        using var fixture = new AgentFixture();
        fixture.Store.EnsureAgent(fixture.Definition);
        fixture.Store.SetDesiredState(fixture.Definition.Id, AgentDesiredState.Running);

        var status = fixture.Service.ReconcilePersonal();

        Assert.True(status.RuntimeHealthy);
        Assert.Equal(SessionObservedState.Running, status.Session.ObservedState);
        Assert.Contains(fixture.Tmux.Commands, command => command[0] == "new-session");
        Assert.Contains(fixture.Tmux.Commands, command => command[0] == "respawn-pane");
    }

    [Fact]
    public void Stopped_agent_is_not_resurrected_when_session_is_missing()
    {
        using var fixture = new AgentFixture();
        fixture.Store.EnsureAgent(fixture.Definition);

        var status = fixture.Service.ReconcilePersonal();
        var health = fixture.TmuxManager.GetHealth(fixture.Definition.TmuxSessionName, "claude");

        Assert.Equal(AgentDesiredState.Stopped, status.DesiredState);
        Assert.Equal(SessionObservedState.Missing, status.Session.ObservedState);
        Assert.True(health.RepairEligible);
        Assert.DoesNotContain(fixture.Tmux.Commands, command => command[0] is "new-session" or "respawn-pane");
    }

    [Fact]
    public void Health_rejects_a_shell_even_when_tmux_session_exists()
    {
        using var fixture = new AgentFixture();
        fixture.Tmux.SessionExists = true;
        fixture.Tmux.NativeProcess = false;
        fixture.Tmux.PaneStartCommand = "/bin/sh";
        fixture.Tmux.PaneCurrentCommand = "sh";

        var health = fixture.TmuxManager.GetHealth(fixture.Definition.TmuxSessionName, "claude");

        Assert.True(health.SessionDetected);
        Assert.False(health.RuntimeHealthy);
        Assert.Equal(SessionObservedState.Exited, health.ObservedState);
    }

    [Fact]
    public void Resume_failure_falls_back_to_a_new_native_conversation()
    {
        using var fixture = new AgentFixture();
        fixture.Tmux.SessionExists = true;
        fixture.Tmux.PaneDead = true;
        fixture.Tmux.FailResume = true;
        fixture.Store.EnsureAgent(fixture.Definition);
        fixture.Store.RecordConversationReference(fixture.Definition.Id, "opaque-reference");

        var status = fixture.Service.StartPersonal();

        Assert.True(status.RuntimeHealthy);
        Assert.Equal(2, fixture.Tmux.Commands.Count(command => command[0] == "respawn-pane"));
        var startEvent = fixture.Database.ReadActivityEvents().Single(eventItem => eventItem.Operation == "start");
        using var metadata = JsonDocument.Parse(startEvent.MetadataJson);
        Assert.True(metadata.RootElement.GetProperty("resumeFallback").GetBoolean());
    }

    [Fact]
    public void Native_resume_exit_falls_back_to_a_new_native_conversation()
    {
        using var fixture = new AgentFixture();
        fixture.Tmux.SessionExists = true;
        fixture.Tmux.PaneDead = true;
        fixture.Tmux.ResumeProcessExits = true;
        fixture.Store.EnsureAgent(fixture.Definition);
        fixture.Store.RecordConversationReference(fixture.Definition.Id, "opaque-reference");

        var status = fixture.Service.StartPersonal();

        Assert.True(status.RuntimeHealthy);
        Assert.Equal(2, fixture.Tmux.Commands.Count(command => command[0] == "respawn-pane"));
    }

    [Fact]
    public void Conversation_reference_is_recorded_through_the_runtime_boundary()
    {
        using var fixture = new AgentFixture();

        fixture.Service.RecordPersonalConversationReference("opaque-reference");

        Assert.Equal("opaque-reference", fixture.Store.ReadStatus(fixture.Definition).Session.NativeConversationReference);
    }

    [Fact]
    public void Launch_uses_separate_tmux_arguments_instead_of_typed_input()
    {
        using var fixture = new AgentFixture();
        fixture.Tmux.SessionExists = true;

        fixture.TmuxManager.LaunchProcess(
            fixture.Definition.TmuxSessionName,
            fixture.Definition.WorkingDirectory,
            "claude",
            ["--resume", "ref with spaces"]);

        var command = fixture.Tmux.Commands.Single(item => item[0] == "respawn-pane");
        Assert.Equal("claude", command[^3]);
        Assert.Equal("--resume", command[^2]);
        Assert.Equal("ref with spaces", command[^1]);
        Assert.DoesNotContain("send-keys", fixture.Tmux.Commands.SelectMany(commandItem => commandItem));
    }

    [Fact]
    public void Tmux_manager_rejects_sessions_outside_the_configured_prefix()
    {
        using var fixture = new AgentFixture();

        Assert.Throws<AgentConfigurationException>(() => fixture.TmuxManager.HasSession("other-personal"));
    }

    [Fact]
    public void Process_health_does_not_match_an_argument_named_claude()
    {
        Assert.False(SystemNativeProcessInspector.IsExpectedExecutable("/bin/sh", "sleep 60 claude", "claude"));
        Assert.True(SystemNativeProcessInspector.IsExpectedExecutable("/usr/local/bin/claude", "claude --resume reference", "claude"));
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

        throw new InvalidOperationException("Unable to find repository root for agent tests.");
    }

    private sealed class AgentFixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");

        public AgentFixture()
        {
            RepositoryRoot = FindRepositoryRoot();
            Database = new SqliteHarnessDatabase(connection);
            Store = new SqliteAgentSessionStore(Database);
            Definition = new AgentRegistry(RepositoryRoot, "test-pa-").LoadPersonal();
            Tmux = new FakeTmuxExecutor();
            TmuxManager = new TmuxSessionManager("test-pa-", Tmux, new FakeProcessInspector(Tmux));
            Service = new AgentSessionService(
                new AgentRegistry(RepositoryRoot, "test-pa-"),
                Store,
                TmuxManager,
                new ClaudeRuntimeAdapter(TmuxManager));
        }

        public string RepositoryRoot { get; }
        public SqliteHarnessDatabase Database { get; }
        public SqliteConnection Connection => connection;
        public SqliteAgentSessionStore Store { get; }
        public AgentDefinition Definition { get; }
        public FakeTmuxExecutor Tmux { get; }
        public TmuxSessionManager TmuxManager { get; }
        public AgentSessionService Service { get; }

        public void Dispose()
        {
            Database.Dispose();
            connection.Dispose();
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

            throw new InvalidOperationException("Unable to find repository root for agent tests.");
        }
    }

    private sealed class FakeTmuxExecutor : ITmuxCommandExecutor
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];
        public bool SessionExists { get; set; }
        public bool NativeProcess { get; set; }
        public bool PaneDead { get; set; }
        public string PaneStartCommand { get; set; } = "claude";
        public string PaneCurrentCommand { get; set; } = "claude";
        public bool ProcessIdentityMatches { get; set; } = true;
        public bool FailResume { get; set; }
        public bool ResumeProcessExits { get; set; }
        public bool FailKill { get; set; }

        public TmuxCommandResult Execute(IReadOnlyList<string> arguments)
        {
            Commands.Add(arguments.ToArray());
            return arguments[0] switch
            {
                "has-session" => new TmuxCommandResult(SessionExists ? 0 : 1, string.Empty, string.Empty),
                "new-session" => CreateSession(),
                "respawn-pane" => LaunchProcess(arguments),
                "kill-session" => KillSession(),
                "list-panes" => ListPane(),
                "list-sessions" => new TmuxCommandResult(0, SessionExists ? "test-pa-personal\n" : string.Empty, string.Empty),
                _ => new TmuxCommandResult(1, string.Empty, "unexpected command")
            };
        }

        private TmuxCommandResult CreateSession()
        {
            SessionExists = true;
            NativeProcess = false;
            PaneDead = false;
            PaneStartCommand = "/bin/sh";
            PaneCurrentCommand = "sh";
            return new TmuxCommandResult(0, string.Empty, string.Empty);
        }

        private TmuxCommandResult LaunchProcess(IReadOnlyList<string> arguments)
        {
            if (FailResume && arguments.Contains("--resume", StringComparer.Ordinal))
            {
                return new TmuxCommandResult(1, string.Empty, "resume unavailable");
            }

            if (ResumeProcessExits && arguments.Contains("--resume", StringComparer.Ordinal))
            {
                NativeProcess = false;
                PaneDead = true;
                return new TmuxCommandResult(0, string.Empty, string.Empty);
            }

            NativeProcess = true;
            PaneDead = false;
            var separator = arguments.ToList().IndexOf("--");
            if (separator >= 0 && separator + 1 < arguments.Count)
            {
                PaneStartCommand = arguments[separator + 1];
            }

            PaneCurrentCommand = PaneStartCommand == "claude" ? "claude" : PaneStartCommand;
            return new TmuxCommandResult(0, string.Empty, string.Empty);
        }

        private TmuxCommandResult KillSession()
        {
            if (FailKill)
            {
                return new TmuxCommandResult(1, string.Empty, "kill failed");
            }

            SessionExists = false;
            NativeProcess = false;
            return new TmuxCommandResult(0, string.Empty, string.Empty);
        }

        private TmuxCommandResult ListPane() =>
            new(
                0,
                $"123\t{(PaneDead ? "1" : "0")}\t{PaneStartCommand}\t{PaneCurrentCommand}\n",
                string.Empty);
    }

    private sealed class FakeProcessInspector(FakeTmuxExecutor tmux) : INativeProcessInspector
    {
        public ProcessObservation Inspect(int processId, string expectedExecutable) =>
            new(tmux.NativeProcess, tmux.ProcessIdentityMatches);
    }
}
