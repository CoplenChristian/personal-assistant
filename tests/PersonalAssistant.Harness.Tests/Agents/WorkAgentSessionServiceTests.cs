using Microsoft.Data.Sqlite;
using System.Text.Json;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Persistence;
using PersonalAssistant.Harness.Runtime;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Agents;

public sealed class WorkAgentSessionServiceTests
{
    [Fact]
    public void Start_creates_one_codex_session_and_repeated_start_adopts_healthy_runtime()
    {
        using var fixture = new WorkFixture();
        var first = fixture.Service.StartWork();

        var respawnsAfterFirstStart = fixture.Tmux.Commands.Count(command => command[0] == "respawn-pane");
        var createsAfterFirstStart = fixture.Tmux.Commands.Count(command => command[0] == "new-session");
        var second = fixture.Service.StartWork();

        Assert.Equal(AgentDesiredState.Running, first.DesiredState);
        Assert.Equal(SessionObservedState.Running, first.Session.ObservedState);
        Assert.Equal("codex", first.Definition.Runtime);
        Assert.True(first.RuntimeHealthy);
        Assert.Equal(SessionObservedState.Running, second.Session.ObservedState);
        Assert.Equal(createsAfterFirstStart, fixture.Tmux.Commands.Count(command => command[0] == "new-session"));
        Assert.Equal(respawnsAfterFirstStart, fixture.Tmux.Commands.Count(command => command[0] == "respawn-pane"));
    }

    [Fact]
    public void Stop_retains_logical_work_agent_session_and_activity_history()
    {
        using var fixture = new WorkFixture();
        fixture.Service.StartWork();
        var stopped = fixture.Service.StopWork();
        var reloaded = fixture.Store.ReadStatus(fixture.Definition);

        Assert.Equal(AgentDesiredState.Stopped, stopped.DesiredState);
        Assert.Equal(SessionObservedState.Exited, stopped.Session.ObservedState);
        Assert.Equal("codex", stopped.Session.Runtime);
        Assert.Equal(AgentDesiredState.Stopped, reloaded.DesiredState);
        Assert.Contains(fixture.Database.ReadActivityEvents(), eventItem => eventItem.Operation == "stop" && eventItem.Realm == "work");
    }

    [Fact]
    public void Reconcile_adopts_existing_healthy_codex_without_relaunching()
    {
        using var fixture = new WorkFixture();
        fixture.Store.EnsureAgent(fixture.Definition);
        fixture.Store.SetDesiredState(fixture.Definition.Id, AgentDesiredState.Running);
        fixture.Tmux.SessionExists = true;
        fixture.Tmux.NativeProcess = true;
        fixture.Tmux.PaneStartCommand = "codex";

        var status = fixture.Service.ReconcileWork();

        Assert.True(status.RuntimeHealthy);
        Assert.Equal(SessionObservedState.Running, status.Session.ObservedState);
        Assert.DoesNotContain(fixture.Tmux.Commands, command => command[0] is "new-session" or "respawn-pane");
        Assert.All(fixture.Database.ReadActivityEvents(), eventItem => Assert.Equal("work", eventItem.Realm));
    }

    [Fact]
    public void Reconcile_recreates_a_missing_session_when_desired_state_is_running()
    {
        using var fixture = new WorkFixture();
        fixture.Store.EnsureAgent(fixture.Definition);
        fixture.Store.SetDesiredState(fixture.Definition.Id, AgentDesiredState.Running);

        var status = fixture.Service.ReconcileWork();

        Assert.True(status.RuntimeHealthy);
        Assert.Equal(SessionObservedState.Running, status.Session.ObservedState);
        Assert.Contains(fixture.Tmux.Commands, command => command[0] == "new-session");
        Assert.Contains(fixture.Tmux.Commands, command => command[0] == "respawn-pane");
    }

    [Fact]
    public void Stopped_work_agent_is_not_resurrected_when_session_is_missing()
    {
        using var fixture = new WorkFixture();
        fixture.Store.EnsureAgent(fixture.Definition);

        var status = fixture.Service.ReconcileWork();

        Assert.Equal(AgentDesiredState.Stopped, status.DesiredState);
        Assert.Equal(SessionObservedState.Missing, status.Session.ObservedState);
        Assert.DoesNotContain(fixture.Tmux.Commands, command => command[0] is "new-session" or "respawn-pane");
    }

    [Fact]
    public void Resume_failure_falls_back_to_a_new_codex_conversation()
    {
        using var fixture = new WorkFixture();
        fixture.Tmux.SessionExists = true;
        fixture.Tmux.PaneDead = true;
        fixture.Tmux.FailResume = true;
        fixture.Tmux.PaneStartCommand = "codex";
        fixture.Store.EnsureAgent(fixture.Definition);
        fixture.Store.RecordConversationReference(fixture.Definition.Id, "opaque-reference");

        var status = fixture.Service.StartWork();

        Assert.True(status.RuntimeHealthy);
        Assert.Equal(2, fixture.Tmux.Commands.Count(command => command[0] == "respawn-pane"));
        var launchCommands = fixture.Tmux.Commands.Where(command => command[0] == "respawn-pane").ToArray();
        Assert.Equal("codex", launchCommands[0][^3]);
        Assert.Equal("resume", launchCommands[0][^2]);
        Assert.Equal("codex", launchCommands[1][^1]);
        var startEvent = fixture.Database.ReadActivityEvents().Single(eventItem => eventItem.Operation == "start");
        using var metadata = JsonDocument.Parse(startEvent.MetadataJson);
        Assert.True(metadata.RootElement.GetProperty("resumeFallback").GetBoolean());
    }

    [Fact]
    public void Launch_uses_codex_resume_subcommand_instead_of_claude_flag()
    {
        using var fixture = new WorkFixture();
        fixture.Tmux.SessionExists = true;

        fixture.TmuxManager.LaunchProcess(
            fixture.Definition.TmuxSessionName,
            fixture.Definition.WorkingDirectory,
            "codex",
            ["resume", "session-id"]);

        var command = fixture.Tmux.Commands.Single(item => item[0] == "respawn-pane");
        Assert.Equal("codex", command[^3]);
        Assert.Equal("resume", command[^2]);
        Assert.Equal("session-id", command[^1]);
        Assert.DoesNotContain("send-keys", fixture.Tmux.Commands.SelectMany(commandItem => commandItem));
        Assert.DoesNotContain("--resume", command);
    }

    private sealed class WorkFixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");

        public WorkFixture()
        {
            RepositoryRoot = FindRepositoryRoot();
            Database = new SqliteHarnessDatabase(connection);
            Store = new SqliteAgentSessionStore(Database);
            Definition = new AgentRegistry(RepositoryRoot, "test-pa-").LoadWork();
            Tmux = new WorkFakeTmuxExecutor();
            TmuxManager = new TmuxSessionManager("test-pa-", Tmux, new WorkFakeProcessInspector(Tmux));
            var codex = new CodexRuntimeAdapter(TmuxManager);
            var runtimeAdapters = new RuntimeAdapterResolver(
            [
                new KeyValuePair<string, IAgentRuntimeAdapter>("claude", new ClaudeRuntimeAdapter(TmuxManager)),
                new KeyValuePair<string, IAgentRuntimeAdapter>("codex", codex)
            ]);
            Service = new AgentSessionService(
                new AgentRegistry(RepositoryRoot, "test-pa-"),
                Store,
                TmuxManager,
                runtimeAdapters);
        }

        public string RepositoryRoot { get; }
        public SqliteHarnessDatabase Database { get; }
        public SqliteAgentSessionStore Store { get; }
        public AgentDefinition Definition { get; }
        public WorkFakeTmuxExecutor Tmux { get; }
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

            throw new InvalidOperationException("Unable to find repository root for work agent tests.");
        }
    }

    private sealed class WorkFakeTmuxExecutor : ITmuxCommandExecutor
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];
        public bool SessionExists { get; set; }
        public bool NativeProcess { get; set; }
        public bool PaneDead { get; set; }
        public string PaneStartCommand { get; set; } = "codex";
        public string PaneCurrentCommand { get; set; } = "codex";
        public bool FailResume { get; set; }

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
                "list-sessions" => new TmuxCommandResult(0, SessionExists ? "test-pa-work\n" : string.Empty, string.Empty),
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
            if (FailResume && IsResumeLaunch(arguments))
            {
                return new TmuxCommandResult(1, string.Empty, "resume unavailable");
            }

            NativeProcess = true;
            PaneDead = false;
            var separator = arguments.ToList().IndexOf("--");
            if (separator >= 0 && separator + 1 < arguments.Count)
            {
                PaneStartCommand = arguments[separator + 1];
            }

            PaneCurrentCommand = PaneStartCommand;
            return new TmuxCommandResult(0, string.Empty, string.Empty);
        }

        private static bool IsResumeLaunch(IReadOnlyList<string> arguments)
        {
            if (arguments.Contains("--resume", StringComparer.Ordinal))
            {
                return true;
            }

            var separator = arguments.ToList().IndexOf("--");
            return separator >= 0
                && separator + 2 < arguments.Count
                && string.Equals(arguments[separator + 2], "resume", StringComparison.Ordinal);
        }

        private TmuxCommandResult KillSession()
        {
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

    private sealed class WorkFakeProcessInspector(WorkFakeTmuxExecutor tmux) : INativeProcessInspector
    {
        public ProcessObservation Inspect(int processId, string expectedExecutable) =>
            new(tmux.NativeProcess, tmux.NativeProcess);
    }
}
