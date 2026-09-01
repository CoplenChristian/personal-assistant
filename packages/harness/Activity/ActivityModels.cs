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
}
