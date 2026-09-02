using System.Text.Json;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Memory;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Memory;

public sealed class CheckpointCoordinatorTests
{
    [Fact]
    public async Task Writes_versioned_checkpoint_state_only_under_runtime_directory()
    {
        using var fixture = new CheckpointFixture();

        var result = await fixture.Coordinator.CreateAsync(
            fixture.Agent,
            fixture.Session,
            new CheckpointRequest("rotate", "remember the approved plan", "finish the review"));

        Assert.StartsWith(fixture.RuntimeDirectory, result.ArtifactPath, StringComparison.Ordinal);
        Assert.StartsWith(fixture.RuntimeDirectory, result.MemoryPath, StringComparison.Ordinal);
        Assert.StartsWith(fixture.RuntimeDirectory, result.HandoffPath, StringComparison.Ordinal);
        Assert.True(File.Exists(result.ArtifactPath));
        Assert.True(File.Exists(result.MemoryPath));
        Assert.True(File.Exists(result.HandoffPath));
        Assert.False(File.Exists(Path.Combine(fixture.RepositoryRoot, "agents", "personal", "MEMORY.md")));
        Assert.False(File.Exists(Path.Combine(fixture.RepositoryRoot, "agents", "personal", "HANDOFF.md")));

        var memory = await File.ReadAllTextAsync(result.MemoryPath);
        var handoff = await File.ReadAllTextAsync(result.HandoffPath);
        Assert.Contains("human memory note", memory, StringComparison.Ordinal);
        Assert.Contains("remember the approved plan", memory, StringComparison.Ordinal);
        Assert.Contains("unresolved handoff note", handoff, StringComparison.Ordinal);
        Assert.Contains("finish the review", handoff, StringComparison.Ordinal);

        var artifact = JsonSerializer.Deserialize<CheckpointArtifact>(
            await File.ReadAllTextAsync(result.ArtifactPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(artifact);
        Assert.Equal(CheckpointCoordinator.ArtifactSchemaVersion, artifact.SchemaVersion);
        Assert.Equal(result.CheckpointId, artifact.CheckpointId);
        Assert.Equal("personal", artifact.AgentId);
        Assert.Equal("rotate", artifact.Reason);
        Assert.Equal(fixture.Session.Id, artifact.SessionId);
        Assert.NotEmpty(artifact.MemorySha256);
        Assert.NotEmpty(artifact.HandoffSha256);

        var activity = Assert.Single(fixture.Events);
        Assert.Equal("memory", activity.Category);
        Assert.Equal("checkpoint", activity.Operation);
        Assert.Equal("success", activity.Status);
        Assert.Contains("memory.checkpoint", activity.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("rotate", activity.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("approved plan", activity.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.RuntimeDirectory, activity.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preserves_human_content_outside_generated_markers()
    {
        using var fixture = new CheckpointFixture();
        var first = await fixture.Coordinator.CreateAsync(
            fixture.Agent,
            fixture.Session,
            new CheckpointRequest("clear", "first generated memory", "first generated handoff"));

        var customMemory = "human header\n\n"
            + CheckpointCoordinator.BeginMemoryMarker
            + "\nold generated content\n"
            + CheckpointCoordinator.EndMemoryMarker
            + "\n\nhuman footer";
        var customHandoff = "# Personal handoff\n\n"
            + "human handoff header\n"
            + CheckpointCoordinator.BeginHandoffMarker
            + "\nold generated handoff\n"
            + CheckpointCoordinator.EndHandoffMarker
            + "\n\nhuman handoff footer";
        await File.WriteAllTextAsync(first.MemoryPath, customMemory);
        await File.WriteAllTextAsync(first.HandoffPath, customHandoff);

        var second = await fixture.Coordinator.CreateAsync(
            fixture.Agent,
            fixture.Session,
            new CheckpointRequest("compact", "replacement memory", "replacement handoff"));

        var memory = await File.ReadAllTextAsync(second.MemoryPath);
        var handoff = await File.ReadAllTextAsync(second.HandoffPath);
        Assert.Contains("human header", memory, StringComparison.Ordinal);
        Assert.Contains("human footer", memory, StringComparison.Ordinal);
        Assert.Contains("replacement memory", memory, StringComparison.Ordinal);
        Assert.DoesNotContain("old generated content", memory, StringComparison.Ordinal);
        Assert.Contains("human handoff header", handoff, StringComparison.Ordinal);
        Assert.Contains("human handoff footer", handoff, StringComparison.Ordinal);
        Assert.Contains("replacement handoff", handoff, StringComparison.Ordinal);
        Assert.DoesNotContain("old generated handoff", handoff, StringComparison.Ordinal);
        Assert.Equal(2, fixture.Events.Count);
    }

    [Fact]
    public async Task Missing_template_fails_without_writing_source_or_runtime_memory()
    {
        using var fixture = new CheckpointFixture();
        File.Delete(fixture.HandoffTemplatePath);

        var exception = await Assert.ThrowsAsync<CheckpointException>(() => fixture.Coordinator.CreateAsync(
            fixture.Agent,
            fixture.Session,
            new CheckpointRequest("rotate", "private memory", "private handoff")));

        Assert.Equal("checkpoint_template_missing", exception.Code);
        Assert.False(File.Exists(Path.Combine(fixture.RepositoryRoot, "agents", "personal", "MEMORY.md")));
        Assert.False(Directory.Exists(Path.Combine(fixture.RuntimeDirectory, "agents")));
        var activity = Assert.Single(fixture.Events);
        Assert.Equal("error", activity.Status);
        Assert.Contains("checkpoint_template_missing", activity.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("private memory", activity.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("private handoff", activity.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_before_write_leaves_runtime_directory_empty()
    {
        using var fixture = new CheckpointFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Coordinator.CreateAsync(
            fixture.Agent,
            fixture.Session,
            new CheckpointRequest("clear", "generated memory", "generated handoff"),
            cancellation.Token));

        Assert.False(Directory.Exists(Path.Combine(fixture.RuntimeDirectory, "agents")));
        var activity = Assert.Single(fixture.Events);
        Assert.Equal("error", activity.Status);
        Assert.Contains("cancelled", activity.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Marker_content_is_rejected_before_any_runtime_write()
    {
        using var fixture = new CheckpointFixture();

        var exception = await Assert.ThrowsAsync<CheckpointException>(() => fixture.Coordinator.CreateAsync(
            fixture.Agent,
            fixture.Session,
            new CheckpointRequest("compact", CheckpointCoordinator.BeginMemoryMarker, "safe handoff")));

        Assert.Equal("checkpoint_content_invalid", exception.Code);
        Assert.False(Directory.Exists(Path.Combine(fixture.RuntimeDirectory, "agents")));
        var activity = Assert.Single(fixture.Events);
        Assert.Equal("error", activity.Status);
        Assert.DoesNotContain(CheckpointCoordinator.BeginMemoryMarker, activity.MetadataJson, StringComparison.Ordinal);
    }

    private sealed class CheckpointFixture : IDisposable
    {
        public CheckpointFixture()
        {
            RepositoryRoot = Directory.CreateTempSubdirectory("personal-assistant-checkpoint-repository-").FullName;
            RuntimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-checkpoint-runtime-").FullName;
            var agentDirectory = Directory.CreateDirectory(Path.Combine(RepositoryRoot, "agents", "personal"));
            MemoryTemplatePath = Path.Combine(agentDirectory.FullName, "MEMORY.template.md");
            HandoffTemplatePath = Path.Combine(agentDirectory.FullName, "HANDOFF.template.md");
            File.WriteAllText(
                MemoryTemplatePath,
                "# Personal memory\n\nhuman memory note\n\n"
                + CheckpointCoordinator.BeginMemoryMarker
                + "\n"
                + CheckpointCoordinator.EndMemoryMarker
                + "\n\nhuman memory footer");
            File.WriteAllText(
                HandoffTemplatePath,
                "# Personal handoff\n\n"
                + "unresolved handoff note\n"
                + CheckpointCoordinator.BeginHandoffMarker
                + "\n"
                + CheckpointCoordinator.EndHandoffMarker
                + "\n\n## Context for the next native session");

            Agent = new AgentDefinition(
                "personal",
                "Personal",
                "claude",
                RepositoryRoot,
                ["personal"],
                ["memory"],
                false,
                "personal",
                "personal",
                [],
                "test-pa-personal",
                Path.Combine(agentDirectory.FullName, "agent.yaml"));
            Session = new PersistedSession(
                "session-123",
                "personal",
                "test-pa-personal",
                "claude",
                "native-ref-123",
                SessionObservedState.Running,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                null);
            Coordinator = new CheckpointCoordinator(RepositoryRoot, RuntimeDirectory, new RecordingActivitySink(Events));
        }

        public string RepositoryRoot { get; }
        public string RuntimeDirectory { get; }
        public string MemoryTemplatePath { get; }
        public string HandoffTemplatePath { get; }
        public AgentDefinition Agent { get; }
        public PersistedSession Session { get; }
        public List<ActivityEvent> Events { get; } = [];
        public CheckpointCoordinator Coordinator { get; }

        public void Dispose()
        {
            if (Directory.Exists(RepositoryRoot))
            {
                Directory.Delete(RepositoryRoot, recursive: true);
            }

            if (Directory.Exists(RuntimeDirectory))
            {
                Directory.Delete(RuntimeDirectory, recursive: true);
            }
        }
    }

    private sealed class RecordingActivitySink(List<ActivityEvent> events) : IActivityEventSink
    {
        public void Append(ActivityEvent activityEvent) => events.Add(activityEvent);
    }
}
