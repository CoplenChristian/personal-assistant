using System.Text.Json;

namespace PersonalAssistant.Harness.Activity;

public sealed record ActivityEvent(
    string Id,
    DateTimeOffset Timestamp,
    string? AgentId,
    string? Realm,
    string Category,
    string Operation,
    string? Target,
    string Status,
    long? DurationMs,
    string MetadataJson)
{
    public static ActivityEvent SettingsUpdated(IReadOnlyCollection<string> keys, bool requiresRestart, string operation) =>
        new(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            null,
            null,
            "settings",
            operation,
            null,
            "success",
            null,
            JsonSerializer.Serialize(new
            {
                eventType = "settings.updated",
                keys,
                scope = "global",
                requiresRestart
            }));

    public static ActivityEvent AgentLifecycle(
        string agentId,
        string? realm,
        string operation,
        string target,
        string status,
        string desiredState,
        string observedState,
        bool adopted = false,
        bool resumeAttempted = false,
        bool resumeFallback = false,
        string? errorCode = null,
        string? eventType = null) =>
        new(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            agentId,
            realm,
            "agents",
            operation,
            target,
            status,
            null,
            JsonSerializer.Serialize(new
            {
                eventType = eventType ?? $"agent.{operation}",
                agentId,
                desiredState,
                observedState,
                adopted,
                resumeAttempted,
                resumeFallback,
                errorCode
            }));

    public static ActivityEvent MemoryCheckpoint(
        string agentId,
        string? realm,
        string reason,
        string status,
        string outcome) =>
        new(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            agentId,
            realm,
            "memory",
            "checkpoint",
            "runtime-memory",
            status,
            null,
            JsonSerializer.Serialize(new
            {
                eventType = "memory.checkpoint",
                reason,
                outcome
            }));

    public static ActivityEvent AgentHygiene(
        string agentId,
        string? realm,
        string operation,
        string status,
        string outcome,
        string? errorCode = null) =>
        new(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            agentId,
            realm,
            "agents",
            operation,
            "runtime-session",
            status,
            null,
            JsonSerializer.Serialize(new
            {
                eventType = $"agent.{operation}",
                outcome,
                errorCode
            }));

    public static ActivityEvent TerminalLogWarning(string agentId, string? realm) =>
        new(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            agentId,
            realm,
            "sessions",
            "terminal_log_warning",
            "runtime-terminal-log",
            "warning",
            null,
            JsonSerializer.Serialize(new
            {
                eventType = "terminal.log.warning",
                outcome = "threshold_reached"
            }));

    public static ActivityEvent TerminalLogRotated(string agentId, string? realm) =>
        new(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            agentId,
            realm,
            "sessions",
            "terminal_log_rotation",
            "runtime-terminal-log",
            "success",
            null,
            JsonSerializer.Serialize(new
            {
                eventType = "terminal.log.rotation",
                outcome = "rotated"
            }));

    public static ActivityEvent TerminalSession(
        string agentId,
        string? realm,
        string operation,
        string status,
        string? outcome = null,
        string? state = null,
        string? errorCode = null) =>
        new(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            agentId,
            realm,
            "sessions",
            operation,
            "runtime-terminal",
            status,
            null,
            JsonSerializer.Serialize(new
            {
                eventType = $"terminal.{operation}",
                outcome,
                state,
                errorCode
            }));
}
