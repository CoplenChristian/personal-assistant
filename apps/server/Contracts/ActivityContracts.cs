using PersonalAssistant.Harness.Activity;

namespace PersonalAssistant.Server.Contracts;

public sealed record ActivityResponse(
    string ContractVersion,
    string Date,
    string Timezone,
    IReadOnlyDictionary<string, int> Counters,
    IReadOnlyList<ActivityEventResponse> RecentEvents,
    int FeedLimit)
{
    public static ActivityResponse From(ActivityQueryResult result) => new(
        result.ContractVersion,
        result.Date,
        result.Timezone,
        result.Counters,
        result.RecentEvents.Select(ActivityEventResponse.From).ToArray(),
        result.FeedLimit);
}

public sealed record ActivityEventResponse(
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
    public static ActivityEventResponse From(ActivityPublicEvent activityEvent) => new(
        activityEvent.Id,
        activityEvent.Timestamp,
        activityEvent.AgentId,
        activityEvent.Realm,
        activityEvent.Category,
        activityEvent.Operation,
        activityEvent.Target,
        activityEvent.Status,
        activityEvent.DurationMs,
        activityEvent.MetadataJson);
}
