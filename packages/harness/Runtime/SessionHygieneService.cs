using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Memory;
using PersonalAssistant.Harness.Persistence;

namespace PersonalAssistant.Harness.Runtime;

public enum SessionHygieneAction
{
    Compact,
    Clear,
    Rotate
}

public sealed record SessionHygieneRequest(
    string RequestId,
    SessionHygieneAction Action,
    CheckpointRequest Checkpoint);

public sealed record SessionHygieneResult(
    string RequestId,
    SessionHygieneAction Action,
    string CheckpointId,
    AgentDesiredState DesiredState,
    SessionObservedState ObservedState,
    bool NativeActionPerformed);

public sealed record CheckpointReceipt(
    string RequestId,
    string CheckpointId);

public interface ISessionHygieneService
{
    Task<SessionHygieneResult> ExecutePersonalAsync(
        SessionHygieneRequest request,
        CancellationToken cancellationToken = default);

    Task<CheckpointReceipt> CheckpointPersonalAsync(
        string requestId,
        CheckpointRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SessionHygieneService : ISessionHygieneService, IDisposable
{
    private readonly AgentRegistry registry;
    private readonly IAgentSessionStore store;
    private readonly IClaudeRuntimeAdapter claude;
    private readonly ICheckpointCoordinator checkpoints;
    private readonly IActivityEventSink activitySink;
    private readonly SemaphoreSlim actionLock = new(1, 1);
    private readonly Dictionary<string, CompletedHygieneRequest> completed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CompletedCheckpointRequest> completedCheckpoints = new(StringComparer.Ordinal);
    private bool disposed;

    public SessionHygieneService(
        AgentRegistry registry,
        IAgentSessionStore store,
        IClaudeRuntimeAdapter claude,
        ICheckpointCoordinator checkpoints,
        IActivityEventSink activitySink)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.claude = claude ?? throw new ArgumentNullException(nameof(claude));
        this.checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
        this.activitySink = activitySink ?? throw new ArgumentNullException(nameof(activitySink));
    }

    public async Task<SessionHygieneResult> ExecutePersonalAsync(
        SessionHygieneRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Checkpoint);
        ThrowIfDisposed();
        ValidateRequest(request);

        if (TryGetCompleted(request, out var completedResult))
        {
            return completedResult;
        }

        await EnterActionAsync(cancellationToken);
        try
        {
            if (TryGetCompleted(request, out completedResult))
            {
                return completedResult;
            }

            var definition = LoadLaunchablePersonal();
            var status = store.EnsureAgent(definition);
            var health = claude.GetStatus(definition, status.Session);
            status = status with
            {
                SessionDetected = health.SessionDetected,
                RuntimeHealthy = health.RuntimeHealthy
            };

            CheckpointResult checkpoint;
            try
            {
                checkpoint = await checkpoints.CreateAsync(definition, status.Session, request.Checkpoint, cancellationToken);
            }
            catch (CheckpointException exception)
            {
                EmitHygieneActivity(definition, request.Action, "blocked", "checkpoint_failed", exception.Code);
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                EmitHygieneActivity(definition, request.Action, "blocked", "cancelled", "checkpoint_cancelled");
                throw;
            }

            if (request.Action == SessionHygieneAction.Rotate
                && status.DesiredState == AgentDesiredState.Stopped)
            {
                var stoppedResult = new SessionHygieneResult(
                    request.RequestId,
                    request.Action,
                    checkpoint.CheckpointId,
                    status.DesiredState,
                    status.Session.ObservedState,
                    NativeActionPerformed: false);
                EmitHygieneActivity(definition, request.Action, "blocked", "agent_stopped");
                CacheCompleted(request, stoppedResult);
                return stoppedResult;
            }

            if (!status.RuntimeHealthy)
            {
                var exception = new SessionHygieneException(
                    "agent_runtime_unhealthy",
                    "The native Claude session is not healthy enough for this action.");
                EmitHygieneActivity(definition, request.Action, "blocked", "runtime_unhealthy", exception.Code);
                throw exception;
            }

            try
            {
                PerformNativeAction(definition, status.Session, request.Action);
                var observed = claude.GetStatus(definition, status.Session);
                if (!observed.RuntimeHealthy)
                {
                    throw new SessionHygieneException(
                        "hygiene_runtime_unhealthy",
                        "The native Claude session did not return to a healthy state.");
                }

                var successActivity = ActivityEvent.AgentHygiene(
                    definition.Id,
                    definition.Realms.FirstOrDefault(),
                    ToWireValue(request.Action),
                    "success",
                    "native_action_completed");
                var persisted = store.RecordObservation(
                    definition,
                    SessionObservedState.Running,
                    null,
                    successActivity,
                    desiredState: status.DesiredState,
                    clearNativeConversationReference: request.Action == SessionHygieneAction.Rotate);
                var result = new SessionHygieneResult(
                    request.RequestId,
                    request.Action,
                    checkpoint.CheckpointId,
                    persisted.DesiredState,
                    persisted.Session.ObservedState,
                    NativeActionPerformed: true);
                CacheCompleted(request, result);
                return result;
            }
            catch (Exception exception) when (IsRuntimeFailure(exception))
            {
                var failure = ToHygieneException(exception);
                var failureActivity = ActivityEvent.AgentHygiene(
                    definition.Id,
                    definition.Realms.FirstOrDefault(),
                    ToWireValue(request.Action),
                    "failure",
                    "native_action_failed",
                    failure.Code);
                PersistFailure(definition, status, failure, failureActivity);
                throw failure;
            }
        }
        finally
        {
            actionLock.Release();
        }
    }

    public async Task<CheckpointReceipt> CheckpointPersonalAsync(
        string requestId,
        CheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateRequestId(requestId);
        ArgumentNullException.ThrowIfNull(request);

        if (TryGetCompletedCheckpoint(requestId, request, out var completedReceipt))
        {
            return completedReceipt;
        }

        await EnterActionAsync(cancellationToken);
        try
        {
            if (TryGetCompletedCheckpoint(requestId, request, out completedReceipt))
            {
                return completedReceipt;
            }

            var definition = LoadLaunchablePersonal();
            var status = store.EnsureAgent(definition);
            try
            {
                var checkpoint = await checkpoints.CreateAsync(definition, status.Session, request, cancellationToken);
                var receipt = new CheckpointReceipt(requestId, checkpoint.CheckpointId);
                lock (completedCheckpoints)
                {
                    completedCheckpoints[requestId] = new CompletedCheckpointRequest(request, receipt);
                }

                return receipt;
            }
            catch (CheckpointException exception)
            {
                EmitHygieneActivity(definition, null, "blocked", "checkpoint_failed", exception.Code);
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                EmitHygieneActivity(definition, null, "blocked", "cancelled", "checkpoint_cancelled");
                throw;
            }
        }
        finally
        {
            actionLock.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        actionLock.Dispose();
    }

    private async Task EnterActionAsync(CancellationToken cancellationToken)
    {
        if (!await actionLock.WaitAsync(0, cancellationToken))
        {
            throw new SessionHygieneException(
                "hygiene_in_progress",
                "Another personal-agent hygiene action is already in progress.");
        }
    }

    private AgentDefinition LoadLaunchablePersonal()
    {
        var definition = registry.LoadPersonal();
        if (!string.Equals(definition.Runtime, "claude", StringComparison.Ordinal))
        {
            throw new SessionHygieneException("agent_runtime_unavailable", "Only the Claude runtime supports hygiene actions in this slice.");
        }

        return definition;
    }

    private void PerformNativeAction(
        AgentDefinition definition,
        PersistedSession session,
        SessionHygieneAction action)
    {
        switch (action)
        {
            case SessionHygieneAction.Compact:
                claude.Compact(definition, session);
                break;
            case SessionHygieneAction.Clear:
                claude.Clear(definition, session);
                break;
            case SessionHygieneAction.Rotate:
                claude.Rotate(definition, session);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "The hygiene action is not supported.");
        }
    }

    private void PersistFailure(
        AgentDefinition definition,
        AgentStatus status,
        SessionHygieneException failure,
        ActivityEvent activity)
    {
        try
        {
            store.RecordObservation(
                definition,
                SessionObservedState.Error,
                failure.Code,
                activity,
                desiredState: status.DesiredState);
        }
        catch (Exception)
        {
            try
            {
                activitySink.Append(activity);
            }
            catch (Exception)
            {
                // Preserve the native action error even if the audit store is unavailable.
            }
        }
    }

    private void EmitHygieneActivity(
        AgentDefinition definition,
        SessionHygieneAction? action,
        string status,
        string outcome,
        string? errorCode = null)
    {
        var operation = action is null ? "checkpoint" : ToWireValue(action.Value);
        activitySink.Append(ActivityEvent.AgentHygiene(
            definition.Id,
            definition.Realms.FirstOrDefault(),
            operation,
            status,
            outcome,
            errorCode));
    }

    private bool TryGetCompleted(SessionHygieneRequest request, out SessionHygieneResult result)
    {
        lock (completed)
        {
            if (!completed.TryGetValue(request.RequestId, out var prior))
            {
                result = null!;
                return false;
            }

            if (prior.Action != request.Action || prior.Checkpoint != request.Checkpoint)
            {
                throw new SessionHygieneException(
                    "hygiene_request_conflict",
                    "The request id was already used for a different hygiene request.");
            }

            result = prior.Result;
            return true;
        }
    }

    private static string ToWireValue(SessionHygieneAction action) => action switch
    {
        SessionHygieneAction.Compact => "compact",
        SessionHygieneAction.Clear => "clear",
        SessionHygieneAction.Rotate => "rotate",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "The hygiene action is not supported.")
    };

    private bool TryGetCompletedCheckpoint(
        string requestId,
        CheckpointRequest request,
        out CheckpointReceipt receipt)
    {
        lock (completedCheckpoints)
        {
            if (!completedCheckpoints.TryGetValue(requestId, out var prior))
            {
                receipt = null!;
                return false;
            }

            if (prior.Request != request)
            {
                throw new SessionHygieneException(
                    "hygiene_request_conflict",
                    "The request id was already used for a different checkpoint request.");
            }

            receipt = prior.Receipt;
            return true;
        }
    }

    private void CacheCompleted(SessionHygieneRequest request, SessionHygieneResult result)
    {
        lock (completed)
        {
            completed[request.RequestId] = new CompletedHygieneRequest(request.Action, request.Checkpoint, result);
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(SessionHygieneService));
        }
    }

    private static void ValidateRequest(SessionHygieneRequest request)
    {
        ValidateRequestId(request.RequestId);
        var expectedReason = request.Action switch
        {
            SessionHygieneAction.Compact => "compact",
            SessionHygieneAction.Clear => "clear",
            SessionHygieneAction.Rotate => "rotate",
            _ => throw new SessionHygieneException("hygiene_action_invalid", "The hygiene action is not supported.")
        };
        if (!string.Equals(request.Checkpoint.Reason, expectedReason, StringComparison.Ordinal))
        {
            throw new SessionHygieneException("hygiene_checkpoint_mismatch", "The checkpoint reason must match the hygiene action.");
        }
    }

    private static void ValidateRequestId(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId)
            || requestId.Length > 128
            || requestId.Any(char.IsControl))
        {
            throw new SessionHygieneException("hygiene_request_invalid", "The hygiene request id is invalid.");
        }
    }

    private static bool IsRuntimeFailure(Exception exception) =>
        exception is TmuxUnavailableException
            or TmuxOperationException
            or AgentLifecycleException;

    private static SessionHygieneException ToHygieneException(Exception exception) =>
        exception switch
        {
            SessionHygieneException hygiene => hygiene,
            AgentLifecycleException lifecycle => new SessionHygieneException(lifecycle.Code, "The native session action could not be completed."),
            _ => new SessionHygieneException("hygiene_native_action_failed", "The native session action could not be completed.")
        };

    private sealed record CompletedHygieneRequest(
        SessionHygieneAction Action,
        CheckpointRequest Checkpoint,
        SessionHygieneResult Result);

    private sealed record CompletedCheckpointRequest(
        CheckpointRequest Request,
        CheckpointReceipt Receipt);
}

public sealed class SessionHygieneException(string code, string message) : AgentLifecycleException(code, message);
