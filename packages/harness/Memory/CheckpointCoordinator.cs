using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Bootstrap;

namespace PersonalAssistant.Harness.Memory;

public interface ICheckpointCoordinator
{
    Task<CheckpointResult> CreateAsync(
        AgentDefinition agent,
        PersistedSession session,
        CheckpointRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record CheckpointRequest(
    string Reason,
    string GeneratedMemory,
    string GeneratedHandoff);

public sealed record CheckpointResult(
    string CheckpointId,
    DateTimeOffset CreatedAt,
    string ArtifactPath,
    string MemoryPath,
    string HandoffPath);

public sealed record CheckpointArtifact(
    int SchemaVersion,
    string CheckpointId,
    DateTimeOffset CreatedAt,
    string AgentId,
    string? Realm,
    string Reason,
    string SessionId,
    string? NativeConversationReference,
    string MemorySha256,
    string HandoffSha256);

public sealed class CheckpointCoordinator : ICheckpointCoordinator
{
    public const int ArtifactSchemaVersion = 1;
    public const int MaxGeneratedSectionBytes = 256 * 1024;
    public const string BeginMemoryMarker = "<!-- BEGIN AUTO MEMORY -->";
    public const string EndMemoryMarker = "<!-- END AUTO MEMORY -->";
    public const string BeginHandoffMarker = "<!-- BEGIN AUTO HANDOFF -->";
    public const string EndHandoffMarker = "<!-- END AUTO HANDOFF -->";

    private static readonly HashSet<string> AllowedReasons = new(StringComparer.Ordinal)
    {
        "compact",
        "clear",
        "rotate"
    };
    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly string repositoryRoot;
    private readonly string runtimeDirectory;
    private readonly IActivityEventSink activitySink;

    public CheckpointCoordinator(
        string repositoryRoot,
        string runtimeDirectory,
        IActivityEventSink activitySink)
    {
        this.repositoryRoot = Path.GetFullPath(repositoryRoot);
        this.runtimeDirectory = Path.GetFullPath(runtimeDirectory);
        this.activitySink = activitySink ?? throw new ArgumentNullException(nameof(activitySink));
    }

    public async Task<CheckpointResult> CreateAsync(
        AgentDefinition agent,
        PersistedSession session,
        CheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var checkpointId = Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow;
        var safeReason = AllowedReasons.Contains(request.Reason) ? request.Reason : "invalid";
        try
        {
            ValidateRequest(agent, session, request);
            cancellationToken.ThrowIfCancellationRequested();

            var paths = ResolvePaths(agent, checkpointId);
            var memorySource = await ReadSourceAsync(paths.MemoryPath, paths.MemoryTemplatePath, cancellationToken);
            var handoffSource = await ReadSourceAsync(paths.HandoffPath, paths.HandoffTemplatePath, cancellationToken);
            var memory = ReplaceGeneratedSection(memorySource, BeginMemoryMarker, EndMemoryMarker, request.GeneratedMemory);
            var handoff = ReplaceGeneratedSection(handoffSource, BeginHandoffMarker, EndHandoffMarker, request.GeneratedHandoff);
            var artifact = new CheckpointArtifact(
                ArtifactSchemaVersion,
                checkpointId,
                createdAt,
                agent.Id,
                agent.Realms.FirstOrDefault(),
                request.Reason,
                session.Id,
                session.NativeConversationReference,
                ComputeSha256(memory),
                ComputeSha256(handoff));
            var artifactJson = JsonSerializer.Serialize(artifact, ArtifactJsonOptions);

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(paths.AgentDirectory);
            Directory.CreateDirectory(paths.CheckpointDirectory);
            var memoryTempPath = paths.MemoryPath + $".{checkpointId}.tmp";
            var handoffTempPath = paths.HandoffPath + $".{checkpointId}.tmp";
            var artifactTempPath = paths.ArtifactPath + $".{checkpointId}.tmp";
            try
            {
                await WriteAsync(memoryTempPath, memory, cancellationToken);
                await WriteAsync(handoffTempPath, handoff, cancellationToken);
                await WriteAsync(artifactTempPath, artifactJson, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                File.Move(memoryTempPath, paths.MemoryPath, overwrite: true);
                File.Move(handoffTempPath, paths.HandoffPath, overwrite: true);
                File.Move(artifactTempPath, paths.ArtifactPath, overwrite: true);
            }
            finally
            {
                DeleteIfPresent(memoryTempPath);
                DeleteIfPresent(handoffTempPath);
                DeleteIfPresent(artifactTempPath);
            }

            EmitActivity(agent, safeReason, "success", "written");
            return new CheckpointResult(
                checkpointId,
                createdAt,
                paths.ArtifactPath,
                paths.MemoryPath,
                paths.HandoffPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            EmitActivity(agent, safeReason, "error", "cancelled");
            throw;
        }
        catch (CheckpointException exception)
        {
            EmitActivity(agent, safeReason, "error", exception.Code);
            throw;
        }
        catch (AgentConfigurationException)
        {
            var exception = new CheckpointException("checkpoint_agent_invalid", "The checkpoint agent identity is invalid.");
            EmitActivity(agent, safeReason, "error", exception.Code);
            throw exception;
        }
        catch (IOException)
        {
            var exception = new CheckpointException("checkpoint_write_failed", "The runtime checkpoint could not be written.");
            EmitActivity(agent, safeReason, "error", exception.Code);
            throw exception;
        }
        catch (UnauthorizedAccessException)
        {
            var exception = new CheckpointException("checkpoint_write_failed", "The runtime checkpoint could not be written.");
            EmitActivity(agent, safeReason, "error", exception.Code);
            throw exception;
        }
    }

    private void ValidateRequest(AgentDefinition agent, PersistedSession session, CheckpointRequest request)
    {
        AgentRegistry.ValidateIdentity(agent.Id);
        if (!string.Equals(session.AgentId, agent.Id, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(session.Id))
        {
            throw new CheckpointException("checkpoint_session_invalid", "The checkpoint session does not match the logical agent.");
        }

        if (!AllowedReasons.Contains(request.Reason))
        {
            throw new CheckpointException("checkpoint_reason_invalid", "The checkpoint reason is not supported.");
        }

        ValidateGeneratedSection(request.GeneratedMemory);
        ValidateGeneratedSection(request.GeneratedHandoff);
    }

    private CheckpointPaths ResolvePaths(AgentDefinition agent, string checkpointId)
    {
        var sourceAgentDirectory = Path.Combine(repositoryRoot, "agents", agent.Id);
        var runtimeAgentDirectory = Path.Combine(runtimeDirectory, "agents", agent.Id);
        var trackedAgentDirectory = Path.Combine(repositoryRoot, "agents");
        if (BootstrapResolver.IsWithin(trackedAgentDirectory, runtimeAgentDirectory))
        {
            throw new CheckpointException("checkpoint_runtime_path_invalid", "Runtime checkpoint state cannot be placed in tracked agent source.");
        }

        return new CheckpointPaths(
            runtimeAgentDirectory,
            Path.Combine(runtimeAgentDirectory, "checkpoints"),
            Path.Combine(runtimeAgentDirectory, "MEMORY.md"),
            Path.Combine(runtimeAgentDirectory, "HANDOFF.md"),
            Path.Combine(runtimeAgentDirectory, "checkpoints", $"{checkpointId}.json"),
            Path.Combine(sourceAgentDirectory, "MEMORY.template.md"),
            Path.Combine(sourceAgentDirectory, "HANDOFF.template.md"));
    }

    private static async Task<string> ReadSourceAsync(
        string runtimePath,
        string templatePath,
        CancellationToken cancellationToken)
    {
        var sourcePath = File.Exists(runtimePath) ? runtimePath : templatePath;
        if (!File.Exists(sourcePath))
        {
            throw new CheckpointException("checkpoint_template_missing", "The tracked checkpoint template is missing.");
        }

        return await File.ReadAllTextAsync(sourcePath, Encoding.UTF8, cancellationToken);
    }

    private static string ReplaceGeneratedSection(
        string source,
        string beginMarker,
        string endMarker,
        string generatedContent)
    {
        var beginIndex = source.IndexOf(beginMarker, StringComparison.Ordinal);
        var endIndex = beginIndex < 0
            ? -1
            : source.IndexOf(endMarker, beginIndex + beginMarker.Length, StringComparison.Ordinal);
        if (beginIndex < 0 || endIndex < 0 || source.IndexOf(beginMarker, beginIndex + beginMarker.Length, StringComparison.Ordinal) >= 0)
        {
            throw new CheckpointException("checkpoint_template_invalid", "The checkpoint template markers are invalid.");
        }

        var normalized = NormalizeGeneratedContent(generatedContent);
        var replacement = normalized.Length == 0 ? "\n" : $"\n{normalized}\n";
        return source[..(beginIndex + beginMarker.Length)]
            + replacement
            + source[endIndex..];
    }

    private static string NormalizeGeneratedContent(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Contains('\0')
            || normalized.Contains(BeginMemoryMarker, StringComparison.Ordinal)
            || normalized.Contains(EndMemoryMarker, StringComparison.Ordinal)
            || normalized.Contains(BeginHandoffMarker, StringComparison.Ordinal)
            || normalized.Contains(EndHandoffMarker, StringComparison.Ordinal)
            || Encoding.UTF8.GetByteCount(normalized) > MaxGeneratedSectionBytes)
        {
            throw new CheckpointException("checkpoint_content_invalid", "Generated checkpoint content is outside the supported format.");
        }

        return normalized;
    }

    private static void ValidateGeneratedSection(string content)
    {
        _ = NormalizeGeneratedContent(content);
    }

    private static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough
        });
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private void EmitActivity(AgentDefinition agent, string reason, string status, string outcome)
    {
        activitySink.Append(ActivityEvent.MemoryCheckpoint(
            agent.Id,
            agent.Realms.FirstOrDefault(),
            reason,
            status,
            outcome));
    }

    private static string ComputeSha256(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temporary runtime artifact.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of a temporary runtime artifact.
        }
    }

    private sealed record CheckpointPaths(
        string AgentDirectory,
        string CheckpointDirectory,
        string MemoryPath,
        string HandoffPath,
        string ArtifactPath,
        string MemoryTemplatePath,
        string HandoffTemplatePath);
}

public sealed class CheckpointException(string code, string message) : AgentLifecycleException(code, message);
