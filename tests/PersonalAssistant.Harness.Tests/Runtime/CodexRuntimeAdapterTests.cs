using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Runtime;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Runtime;

public sealed class CodexRuntimeAdapterTests
{
    [Fact]
    public void StartNewConversation_launches_codex_without_arguments()
    {
        using var fixture = new CodexFixture();
        var definition = fixture.Registry.LoadWork();

        fixture.Adapter.StartNewConversation(definition, fixture.Session);

        var command = fixture.Tmux.Commands.Single(item => item[0] == "respawn-pane");
        Assert.Equal("codex", command[^1]);
        Assert.Equal("--", command[^2]);
    }

    [Fact]
    public void TryResume_launches_codex_resume_with_opaque_reference()
    {
        using var fixture = new CodexFixture();
        var definition = fixture.Registry.LoadWork();
        var session = fixture.Session with { NativeConversationReference = "opaque-session-id" };

        var result = fixture.Adapter.TryResume(definition, session);

        Assert.True(result.Attempted);
        Assert.True(result.Available);
        var command = fixture.Tmux.Commands.Single(item => item[0] == "respawn-pane");
        Assert.Equal("codex", command[^3]);
        Assert.Equal("resume", command[^2]);
        Assert.Equal("opaque-session-id", command[^1]);
    }

    [Fact]
    public void TryResume_returns_unavailable_when_tmux_launch_fails()
    {
        using var fixture = new CodexFixture { FailLaunch = true };
        var definition = fixture.Registry.LoadWork();
        var session = fixture.Session with { NativeConversationReference = "opaque-session-id" };

        var result = fixture.Adapter.TryResume(definition, session);

        Assert.True(result.Attempted);
        Assert.False(result.Available);
    }

    [Fact]
    public void RecordConversationReference_rejects_invalid_values()
    {
        using var fixture = new CodexFixture();
        var definition = fixture.Registry.LoadWork();

        Assert.Throws<AgentConfigurationException>(() =>
            fixture.Adapter.RecordConversationReference(definition, fixture.Session, string.Empty));
        Assert.Throws<AgentConfigurationException>(() =>
            fixture.Adapter.RecordConversationReference(definition, fixture.Session, new string('x', 513)));
    }

    [Fact]
    public void GetStatus_checks_codex_executable()
    {
        using var fixture = new CodexFixture { SessionExists = true, NativeProcess = true, PaneStartCommand = "codex" };
        var definition = fixture.Registry.LoadWork();

        var health = fixture.Adapter.GetStatus(definition, fixture.Session);

        Assert.True(health.RuntimeHealthy);
        Assert.Equal(SessionObservedState.Running, health.ObservedState);
    }

    private sealed class CodexFixture : IDisposable
    {
        public CodexFixture()
        {
            RepositoryRoot = FindRepositoryRoot();
            Registry = new AgentRegistry(RepositoryRoot, "test-pa-");
            Tmux = new CodexFakeTmuxExecutor();
            TmuxManager = new TmuxSessionManager("test-pa-", Tmux, new CodexFakeProcessInspector(Tmux));
            Adapter = new CodexRuntimeAdapter(TmuxManager);
            var definition = Registry.LoadWork();
            Session = new PersistedSession(
                "session-work",
                definition.Id,
                definition.TmuxSessionName,
                definition.Runtime,
                null,
                SessionObservedState.Missing,
                null,
                null,
                null,
                null);
            Tmux.SessionName = definition.TmuxSessionName;
            Tmux.WorkingDirectory = definition.WorkingDirectory;
        }

        public bool FailLaunch
        {
            set => Tmux.FailLaunch = value;
        }

        public bool SessionExists
        {
            set => Tmux.SessionExists = value;
        }

        public bool NativeProcess
        {
            set => Tmux.NativeProcess = value;
        }

        public string PaneStartCommand
        {
            set => Tmux.PaneStartCommand = value;
        }

        public string RepositoryRoot { get; }
        public AgentRegistry Registry { get; }
        public CodexFakeTmuxExecutor Tmux { get; }
        public TmuxSessionManager TmuxManager { get; }
        public CodexRuntimeAdapter Adapter { get; }
        public PersistedSession Session { get; }

        public void Dispose()
        {
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

            throw new InvalidOperationException("Unable to find repository root for Codex adapter tests.");
        }
    }

    private sealed class CodexFakeTmuxExecutor : ITmuxCommandExecutor
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];
        public string SessionName { get; set; } = "test-pa-work";
        public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();
        public bool SessionExists { get; set; } = true;
        public bool NativeProcess { get; set; }
        public string PaneStartCommand { get; set; } = "codex";
        public bool FailLaunch { get; set; }

        public TmuxCommandResult Execute(IReadOnlyList<string> arguments)
        {
            Commands.Add(arguments.ToArray());
            return arguments[0] switch
            {
                "has-session" => new TmuxCommandResult(SessionExists ? 0 : 1, string.Empty, string.Empty),
                "respawn-pane" => FailLaunch
                    ? new TmuxCommandResult(1, string.Empty, "launch failed")
                    : new TmuxCommandResult(0, string.Empty, string.Empty),
                "list-panes" => new TmuxCommandResult(
                    0,
                    $"123\t0\t{PaneStartCommand}\t{PaneStartCommand}\n",
                    string.Empty),
                _ => new TmuxCommandResult(0, string.Empty, string.Empty)
            };
        }
    }

    private sealed class CodexFakeProcessInspector(CodexFakeTmuxExecutor tmux) : INativeProcessInspector
    {
        public ProcessObservation Inspect(int processId, string expectedExecutable) =>
            new(tmux.NativeProcess, tmux.NativeProcess);
    }
}
