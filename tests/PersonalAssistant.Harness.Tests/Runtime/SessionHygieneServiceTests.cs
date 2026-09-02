using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Memory;
using PersonalAssistant.Harness.Persistence;
using PersonalAssistant.Harness.Runtime;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Runtime;

public sealed class SessionHygieneServiceTests
{
    [Theory]
    [InlineData(SessionHygieneAction.Compact, "compact")]
    [InlineData(SessionHygieneAction.Clear, "clear")]
    [InlineData(SessionHygieneAction.Rotate, "rotate")]
    public async Task Checkpoint_precedes_every_typed_native_action_and_preserves_desired_state(
        SessionHygieneAction action,
        string reason)
    {
        using var fixture = new HygieneFixture();
        fixture.Store.SetDesiredState(fixture.Definition.Id, AgentDesiredState.Running);
        fixture.Store.RecordConversationReference(fixture.Definition.Id, "opaque-native-reference");

        var result = await fixture.Service.ExecutePersonalAsync(
            new SessionHygieneRequest(
                "request-1",
                action,
                new CheckpointRequest(reason, "generated memory", "generated handoff")));

        Assert.True(result.NativeActionPerformed);
        Assert.Equal(AgentDesiredState.Running, result.DesiredState);
        Assert.Equal(SessionObservedState.Running, result.ObservedState);
        Assert.True(fixture.Order.IndexOf("checkpoint") < fixture.Order.IndexOf(reason));
        Assert.DoesNotContain(fixture.Runtime.Calls, call => call.StartsWith("arbitrary", StringComparison.Ordinal));
        if (action == SessionHygieneAction.Rotate)
        {
            Assert.Null(fixture.Store.ReadStatus(fixture.Definition).Session.NativeConversationReference);
            Assert.Equal("opaque-native-reference", fixture.Checkpoints.LastSession!.NativeConversationReference);
        }
    }

    [Fact]
    public async Task Successful_request_is_idempotent_and_conflicting_replay_is_rejected()
    {
        using var fixture = new HygieneFixture();
        fixture.Store.SetDesiredState(fixture.Definition.Id, AgentDesiredState.Running);
        var request = new SessionHygieneRequest(
            "request-idempotent",
            SessionHygieneAction.Compact,
            new CheckpointRequest("compact", "memory", "handoff"));

        var first = await fixture.Service.ExecutePersonalAsync(request);
        var second = await fixture.Service.ExecutePersonalAsync(request);

        Assert.Equal(first, second);
        Assert.Single(fixture.Runtime.Calls, call => call == "compact");
        var conflict = await Assert.ThrowsAsync<SessionHygieneException>(() => fixture.Service.ExecutePersonalAsync(
            request with { Checkpoint = request.Checkpoint with { GeneratedMemory = "different" } }));
        Assert.Equal("hygiene_request_conflict", conflict.Code);
    }

    [Fact]
    public async Task Failed_checkpoint_blocks_native_action_and_keeps_request_retryable()
    {
        using var fixture = new HygieneFixture();
        fixture.Store.SetDesiredState(fixture.Definition.Id, AgentDesiredState.Running);
        fixture.Checkpoints.Failure = new CheckpointException("checkpoint_write_failed", "checkpoint unavailable");
        var request = new SessionHygieneRequest(
            "request-failed-checkpoint",
            SessionHygieneAction.Clear,
            new CheckpointRequest("clear", "private memory", "private handoff"));

        var exception = await Assert.ThrowsAsync<CheckpointException>(() => fixture.Service.ExecutePersonalAsync(request));

        Assert.Equal("checkpoint_write_failed", exception.Code);
        Assert.DoesNotContain(fixture.Runtime.Calls, call => call is "compact" or "clear" or "rotate");
        Assert.Equal(AgentDesiredState.Running, fixture.Store.ReadStatus(fixture.Definition).DesiredState);
        var activity = Assert.Single(fixture.Events, item => item.Operation == "clear");
        Assert.Equal("blocked", activity.Status);
        Assert.DoesNotContain("private memory", activity.MetadataJson, StringComparison.Ordinal);

        fixture.Checkpoints.Failure = null;
        var retry = await fixture.Service.ExecutePersonalAsync(request);
        Assert.True(retry.NativeActionPerformed);
        Assert.Single(fixture.Runtime.Calls, call => call == "clear");
    }

    [Fact]
    public async Task Concurrent_action_is_rejected_instead_of_queued()
    {
        using var fixture = new HygieneFixture();
        fixture.Store.SetDesiredState(fixture.Definition.Id, AgentDesiredState.Running);
        fixture.Checkpoints.Blocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRequest = Request("first", "compact");
        var secondRequest = Request("second", "clear");

        var firstTask = fixture.Service.ExecutePersonalAsync(firstRequest);
        await fixture.Checkpoints.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var exception = await Assert.ThrowsAsync<SessionHygieneException>(() => fixture.Service.ExecutePersonalAsync(secondRequest));

        Assert.Equal("hygiene_in_progress", exception.Code);
        fixture.Checkpoints.Blocked.SetResult(true);
        await firstTask;
    }

    [Fact]
    public async Task Native_failure_retains_logical_session_and_records_safe_failure_activity()
    {
        using var fixture = new HygieneFixture();
        fixture.Store.SetDesiredState(fixture.Definition.Id, AgentDesiredState.Running);
        fixture.Store.RecordConversationReference(fixture.Definition.Id, "opaque-reference");
        fixture.Runtime.Failure = new TmuxOperationException("agent_runtime_unavailable", "native action failed");
        var before = fixture.Store.ReadStatus(fixture.Definition);

        var exception = await Assert.ThrowsAsync<SessionHygieneException>(() => fixture.Service.ExecutePersonalAsync(
            Request("request-native-failure", "rotate")));
        var after = fixture.Store.ReadStatus(fixture.Definition);

        Assert.Equal("agent_runtime_unavailable", exception.Code);
        Assert.Equal(before.Session.Id, after.Session.Id);
        Assert.Equal(before.DesiredState, after.DesiredState);
        Assert.Equal(SessionObservedState.Error, after.Session.ObservedState);
        var activity = Assert.Single(fixture.Database.ReadActivityEvents(), item => item.Operation == "rotate");
        Assert.Equal("failure", activity.Status);
        Assert.DoesNotContain("opaque-reference", activity.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Runtime.WorkingDirectory, activity.MetadataJson, StringComparison.Ordinal);
    }

    private static SessionHygieneRequest Request(string requestId, string reason) =>
        new(requestId, Enum.Parse<SessionHygieneAction>(reason, ignoreCase: true), new CheckpointRequest(reason, "memory", "handoff"));

    private sealed class HygieneFixture : IDisposable
    {
        private readonly Microsoft.Data.Sqlite.SqliteConnection connection = new("Data Source=:memory:");

        public HygieneFixture()
        {
            RepositoryRoot = FindRepositoryRoot();
            RuntimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-hygiene-runtime-").FullName;
            Database = new SqliteHarnessDatabase(connection);
            Store = new SqliteAgentSessionStore(Database);
            Definition = new AgentRegistry(RepositoryRoot, "test-pa-").LoadPersonal();
            Runtime = new RecordingRuntime(Definition.WorkingDirectory, Order);
            Checkpoints = new RecordingCheckpoints(Order);
            Events = [];
            Service = new SessionHygieneService(
                new AgentRegistry(RepositoryRoot, "test-pa-"),
                Store,
                Runtime,
                Checkpoints,
                new RecordingActivitySink(Events));
            Store.EnsureAgent(Definition);
        }

        public string RepositoryRoot { get; }
        public string RuntimeDirectory { get; }
        public SqliteHarnessDatabase Database { get; }
        public SqliteAgentSessionStore Store { get; }
        public AgentDefinition Definition { get; }
        public RecordingRuntime Runtime { get; }
        public RecordingCheckpoints Checkpoints { get; }
        public List<ActivityEvent> Events { get; }
        public List<string> Order { get; } = [];
        public SessionHygieneService Service { get; }

        public void Dispose()
        {
            Service.Dispose();
            Database.Dispose();
            connection.Dispose();
            if (Directory.Exists(RuntimeDirectory))
            {
                Directory.Delete(RuntimeDirectory, recursive: true);
            }
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

            throw new InvalidOperationException("Unable to find repository root for hygiene tests.");
        }
    }

    private sealed class RecordingRuntime(string workingDirectory, List<string> order) : IClaudeRuntimeAdapter
    {
        public List<string> Calls { get; } = [];
        public Exception? Failure { get; set; }
        public string WorkingDirectory { get; } = workingDirectory;

        public RuntimeStartResult Start(AgentDefinition agent, PersistedSession session) => throw new NotSupportedException();

        public TmuxHealth GetStatus(AgentDefinition agent, PersistedSession session)
        {
            Calls.Add("status");
            order.Add("status");
            return new TmuxHealth(true, true, SessionObservedState.Running, null);
        }

        public RuntimeResumeResult TryResume(AgentDefinition agent, PersistedSession session) => throw new NotSupportedException();

        public void StartNewConversation(AgentDefinition agent, PersistedSession session) => throw new NotSupportedException();

        public string RecordConversationReference(AgentDefinition agent, PersistedSession session, string reference) => reference.Trim();

        public void Compact(AgentDefinition agent, PersistedSession session) => Perform("compact");

        public void Clear(AgentDefinition agent, PersistedSession session) => Perform("clear");

        public void Rotate(AgentDefinition agent, PersistedSession session) => Perform("rotate");

        public void Stop(AgentDefinition agent, PersistedSession session) => throw new NotSupportedException();

        private void Perform(string operation)
        {
            Calls.Add(operation);
            order.Add(operation);
            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }

    private sealed class RecordingCheckpoints(List<string> order) : ICheckpointCoordinator
    {
        public List<string> Calls { get; } = [];
        public Exception? Failure { get; set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool>? Blocked { get; set; }
        public PersistedSession? LastSession { get; private set; }

        public async Task<CheckpointResult> CreateAsync(
            AgentDefinition agent,
            PersistedSession session,
            CheckpointRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("checkpoint");
            order.Add("checkpoint");
            Started.TrySetResult(true);
            LastSession = session;
            if (Blocked is not null)
            {
                await Blocked.Task.WaitAsync(cancellationToken);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            return new CheckpointResult(
                "checkpoint-1",
                DateTimeOffset.UtcNow,
                "/runtime/checkpoint.json",
                "/runtime/MEMORY.md",
                "/runtime/HANDOFF.md");
        }
    }

    private sealed class RecordingActivitySink(List<ActivityEvent> events) : IActivityEventSink
    {
        public void Append(ActivityEvent activityEvent) => events.Add(activityEvent);
    }
}
