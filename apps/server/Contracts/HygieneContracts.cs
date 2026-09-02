using PersonalAssistant.Harness.Runtime;

namespace PersonalAssistant.Server.Contracts;

public sealed record SessionHygieneRequestContract(
    string? RequestId,
    CheckpointRequestContract? Checkpoint);

public sealed record CheckpointRequestContract(
    string? Reason,
    string? GeneratedMemory,
    string? GeneratedHandoff);

public sealed record SessionHygieneResponse(
    string ContractVersion,
    string RequestId,
    string Action,
    string CheckpointId,
    string DesiredState,
    string ObservedState,
    bool NativeActionPerformed)
{
    public static SessionHygieneResponse From(SessionHygieneResult result) => new(
        "phase-0c-session-hygiene.v1",
        result.RequestId,
        result.Action.ToString().ToLowerInvariant(),
        result.CheckpointId,
        result.DesiredState.ToString().ToLowerInvariant(),
        result.ObservedState.ToString().ToLowerInvariant(),
        result.NativeActionPerformed);
}

public sealed record CheckpointResponse(
    string ContractVersion,
    string RequestId,
    string CheckpointId)
{
    public static CheckpointResponse From(CheckpointReceipt receipt) => new(
        "phase-0c-session-hygiene.v1",
        receipt.RequestId,
        receipt.CheckpointId);
}
