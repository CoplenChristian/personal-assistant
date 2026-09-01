namespace PersonalAssistant.Harness.Agents;

public enum AgentDesiredState
{
    Running,
    Stopped
}

public enum SessionObservedState
{
    Missing,
    Starting,
    Running,
    Exited,
    Error
}

public sealed record AgentDefinition(
    string Id,
    string Name,
    string Runtime,
    string WorkingDirectory,
    IReadOnlyList<string> Realms,
    IReadOnlyList<string> Skills,
    bool AutoStart,
    string? BrowserProfile,
    string? MemoryScope,
    IReadOnlyList<string> ScheduledTaskPermissions,
    string TmuxSessionName,
    string ManifestPath);

public sealed record PersistedSession(
    string Id,
    string AgentId,
    string TmuxSessionName,
    string Runtime,
    string? NativeConversationReference,
    SessionObservedState ObservedState,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? StoppedAt,
    string? LastError);

public sealed record AgentStatus(
    AgentDefinition Definition,
    AgentDesiredState DesiredState,
    PersistedSession Session,
    bool SessionDetected,
    bool RuntimeHealthy)
{
    public string? LastError => Session.LastError;
}

public sealed class AgentConfigurationException(string message) : Exception(message);

public class AgentLifecycleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
