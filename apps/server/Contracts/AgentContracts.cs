using PersonalAssistant.Harness.Agents;

namespace PersonalAssistant.Server.Contracts;

public sealed record AgentStatusResponse(
    string ContractVersion,
    string Id,
    string Name,
    string Runtime,
    string DesiredState,
    string ObservedState,
    string TmuxSessionName,
    bool SessionDetected,
    bool RuntimeHealthy,
    string? LastSeenAt,
    string? StoppedAt,
    string? LastError)
{
    public static AgentStatusResponse From(AgentStatus status) => new(
        "phase-0b-agents.v1",
        status.Definition.Id,
        status.Definition.Name,
        status.Definition.Runtime,
        status.DesiredState.ToString().ToLowerInvariant(),
        status.Session.ObservedState.ToString().ToLowerInvariant(),
        status.Session.TmuxSessionName,
        status.SessionDetected,
        status.RuntimeHealthy,
        status.Session.LastSeenAt?.ToString("O"),
        status.Session.StoppedAt?.ToString("O"),
        status.LastError);
}
