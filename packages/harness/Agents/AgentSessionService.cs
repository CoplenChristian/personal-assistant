using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Persistence;
using PersonalAssistant.Harness.Runtime;

namespace PersonalAssistant.Harness.Agents;

public interface IAgentSessionService
{
    AgentStatus GetPersonal();

    AgentStatus ReconcilePersonal();

    AgentStatus StartPersonal();

    AgentStatus StopPersonal();

    void RecordPersonalConversationReference(string reference);

    AgentStatus GetWork();

    AgentStatus ReconcileWork();

    AgentStatus StartWork();

    AgentStatus StopWork();

    void RecordWorkConversationReference(string reference);
}

public sealed class AgentSessionService : IAgentSessionService
{
    private readonly AgentRegistry registry;
    private readonly IAgentSessionStore store;
    private readonly TmuxSessionManager tmux;
    private readonly IRuntimeAdapterResolver runtimeAdapters;
    private readonly object lifecycleLock = new();

    public AgentSessionService(
        AgentRegistry registry,
        IAgentSessionStore store,
        TmuxSessionManager tmux,
        IRuntimeAdapterResolver runtimeAdapters)
    {
        this.registry = registry;
        this.store = store;
        this.tmux = tmux;
        this.runtimeAdapters = runtimeAdapters;
    }

    public AgentStatus GetPersonal()
    {
        lock (lifecycleLock)
        {
            return GetCore(LoadLaunchablePersonal());
        }
    }

    public AgentStatus ReconcilePersonal()
    {
        lock (lifecycleLock)
        {
            return ReconcileCore(LoadLaunchablePersonal());
        }
    }

    public AgentStatus StartPersonal()
    {
        lock (lifecycleLock)
        {
            return StartCore(LoadLaunchablePersonal());
        }
    }

    public AgentStatus StopPersonal()
    {
        lock (lifecycleLock)
        {
            return StopCore(LoadLaunchablePersonal());
        }
    }

    public void RecordPersonalConversationReference(string reference)
    {
        lock (lifecycleLock)
        {
            RecordConversationReferenceCore(LoadLaunchablePersonal(), reference);
        }
    }

    public AgentStatus GetWork()
    {
        lock (lifecycleLock)
        {
            return GetCore(LoadLaunchableWork());
        }
    }

    public AgentStatus ReconcileWork()
    {
        lock (lifecycleLock)
        {
            return ReconcileCore(LoadLaunchableWork());
        }
    }

    public AgentStatus StartWork()
    {
        lock (lifecycleLock)
        {
            return StartCore(LoadLaunchableWork());
        }
    }

    public AgentStatus StopWork()
    {
        lock (lifecycleLock)
        {
            return StopCore(LoadLaunchableWork());
        }
    }

    public void RecordWorkConversationReference(string reference)
    {
        lock (lifecycleLock)
        {
            RecordConversationReferenceCore(LoadLaunchableWork(), reference);
        }
    }

    private void RecordConversationReferenceCore(AgentDefinition definition, string reference)
    {
        var adapter = runtimeAdapters.Resolve(definition.Runtime);
        var status = store.EnsureAgent(definition);
        var normalized = adapter.RecordConversationReference(definition, status.Session, reference);
        store.RecordConversationReference(definition.Id, normalized);
    }

    private AgentStatus GetCore(AgentDefinition definition)
    {
        var adapter = runtimeAdapters.Resolve(definition.Runtime);
        var status = store.EnsureAgent(definition);
        var health = adapter.GetStatus(definition, status.Session);
        return PersistObservation(definition, health, null);
    }

    private AgentStatus ReconcileCore(AgentDefinition definition)
    {
        var adapter = runtimeAdapters.Resolve(definition.Runtime);
        var status = store.EnsureAgent(definition);
        var health = adapter.GetStatus(definition, status.Session);
        var startResult = (RuntimeStartResult?)null;
        var adopted = health.RuntimeHealthy;

        if (!health.RuntimeHealthy
            && status.DesiredState == AgentDesiredState.Running
            && (health.RepairEligible || !health.SessionDetected))
        {
            try
            {
                tmux.EnsureSession(definition.TmuxSessionName, definition.WorkingDirectory);
                status = store.RecordObservation(definition, SessionObservedState.Starting, null, null);
                startResult = adapter.Start(definition, status.Session);
                health = adapter.GetStatus(definition, status.Session);
                if (!health.RuntimeHealthy && startResult.ResumeAttempted && !startResult.StartedNewConversation)
                {
                    adapter.StartNewConversation(definition, status.Session);
                    startResult = startResult with { StartedNewConversation = true };
                    health = adapter.GetStatus(definition, status.Session);
                }
                if (!health.RuntimeHealthy)
                {
                    health = new TmuxHealth(true, false, SessionObservedState.Error, "The native runtime did not become healthy.");
                }
            }
            catch (Exception exception) when (IsRuntimeFailure(exception))
            {
                health = RuntimeFailureHealth(exception);
            }
        }

        var eventStatus = health.RuntimeHealthy
            || (status.DesiredState == AgentDesiredState.Stopped
                && health.ObservedState is SessionObservedState.Missing or SessionObservedState.Exited)
            ? "success"
            : "error";
        var activity = ActivityEvent.AgentLifecycle(
            definition.Id,
            definition.Realms.FirstOrDefault(),
            "reconcile",
            definition.TmuxSessionName,
            eventStatus,
            ToDatabaseValue(status.DesiredState),
            ToDatabaseValue(health.ObservedState),
            adopted,
            startResult?.ResumeAttempted ?? false,
            startResult is { ResumeAttempted: true, StartedNewConversation: true },
            SafeErrorCode(health.Error),
            "session.reconciled");
        return PersistObservation(definition, health, activity, status.DesiredState);
    }

    private AgentStatus StartCore(AgentDefinition definition)
    {
        var adapter = runtimeAdapters.Resolve(definition.Runtime);
        var status = store.EnsureAgent(definition);
        store.SetDesiredState(definition.Id, AgentDesiredState.Running);
        status = store.ReadStatus(definition);
        var health = adapter.GetStatus(definition, status.Session);

        if (health.RuntimeHealthy)
        {
            var adoptedActivity = ActivityEvent.AgentLifecycle(
                definition.Id,
                definition.Realms.FirstOrDefault(),
                "start",
                definition.TmuxSessionName,
                "success",
                "running",
                "running",
                adopted: true);
            return PersistObservation(definition, health, adoptedActivity, AgentDesiredState.Running);
        }

        try
        {
            if (health.SessionDetected && !health.RepairEligible)
            {
                throw new AgentLifecycleException("agent_session_unverified", "The live tmux pane owner could not be verified safely.");
            }

            tmux.EnsureSession(definition.TmuxSessionName, definition.WorkingDirectory);
            status = store.RecordObservation(definition, SessionObservedState.Starting, null, null);
            var startResult = adapter.Start(definition, status.Session);
            health = adapter.GetStatus(definition, status.Session);
            if (!health.RuntimeHealthy && startResult.ResumeAttempted && !startResult.StartedNewConversation)
            {
                adapter.StartNewConversation(definition, status.Session);
                startResult = startResult with { StartedNewConversation = true };
                health = adapter.GetStatus(definition, status.Session);
            }
            if (!health.RuntimeHealthy)
            {
                throw new AgentLifecycleException("agent_start_failed", "The native runtime did not become healthy after launch.");
            }

            var activity = ActivityEvent.AgentLifecycle(
                definition.Id,
                definition.Realms.FirstOrDefault(),
                "start",
                definition.TmuxSessionName,
                "success",
                "running",
                "running",
                resumeAttempted: startResult.ResumeAttempted,
                resumeFallback: startResult is { ResumeAttempted: true, StartedNewConversation: true });
            return PersistObservation(definition, health, activity, AgentDesiredState.Running);
        }
        catch (Exception exception) when (IsRuntimeFailure(exception))
        {
            var failure = RuntimeFailureHealth(exception);
            var activity = ActivityEvent.AgentLifecycle(
                definition.Id,
                definition.Realms.FirstOrDefault(),
                "error",
                definition.TmuxSessionName,
                "error",
                "running",
                "error",
                errorCode: SafeErrorCode(failure.Error));
            PersistObservation(definition, failure, activity, AgentDesiredState.Running);
            throw ToLifecycleException(exception);
        }
    }

    private AgentStatus StopCore(AgentDefinition definition)
    {
        var adapter = runtimeAdapters.Resolve(definition.Runtime);
        var status = store.EnsureAgent(definition);
        var health = adapter.GetStatus(definition, status.Session);

        try
        {
            if (health.SessionDetected)
            {
                adapter.Stop(definition, status.Session);
            }
        }
        catch (Exception exception) when (IsRuntimeFailure(exception))
        {
            var failure = RuntimeFailureHealth(exception);
            var activity = ActivityEvent.AgentLifecycle(
                definition.Id,
                definition.Realms.FirstOrDefault(),
                "error",
                definition.TmuxSessionName,
                "error",
                "stopped",
                "error",
                errorCode: SafeErrorCode(failure.Error));
            PersistObservation(definition, failure, activity, AgentDesiredState.Stopped);
            throw ToLifecycleException(exception);
        }

        var observed = health.Error is not null && !health.SessionDetected
            ? SessionObservedState.Error
            : SessionObservedState.Exited;
        var finalHealth = observed == SessionObservedState.Error
            ? health
            : new TmuxHealth(false, false, observed, null);
        var stopActivity = ActivityEvent.AgentLifecycle(
            definition.Id,
            definition.Realms.FirstOrDefault(),
            "stop",
            definition.TmuxSessionName,
            finalHealth.ObservedState == SessionObservedState.Error ? "error" : "success",
            "stopped",
            ToDatabaseValue(finalHealth.ObservedState),
            errorCode: SafeErrorCode(finalHealth.Error));
        var result = PersistObservation(definition, finalHealth, stopActivity, AgentDesiredState.Stopped);
        if (finalHealth.ObservedState == SessionObservedState.Error)
        {
            throw new AgentLifecycleException("agent_runtime_unavailable", "The native runtime could not be inspected.");
        }

        return result;
    }

    private AgentDefinition LoadLaunchablePersonal()
    {
        var definition = registry.LoadPersonal();
        if (!string.Equals(definition.Runtime, "claude", StringComparison.Ordinal))
        {
            throw new AgentLifecycleException("agent_runtime_unavailable", "Only the Claude runtime is available for the personal agent.");
        }

        return definition;
    }

    private AgentDefinition LoadLaunchableWork()
    {
        var definition = registry.LoadWork();
        if (!string.Equals(definition.Runtime, "codex", StringComparison.Ordinal))
        {
            throw new AgentLifecycleException("agent_runtime_unavailable", "Only the Codex runtime is available for the work agent.");
        }

        return definition;
    }

    private AgentStatus PersistObservation(
        AgentDefinition definition,
        TmuxHealth health,
        ActivityEvent? activity,
        AgentDesiredState? desiredState = null)
    {
        var observedState = health.ObservedState;
        var safeError = SafeErrorCode(health.Error);
        var persisted = store.RecordObservation(definition, observedState, safeError, activity, desiredState: desiredState);
        return persisted with
        {
            SessionDetected = health.SessionDetected,
            RuntimeHealthy = health.RuntimeHealthy
        };
    }

    private static bool IsRuntimeFailure(Exception exception) =>
        exception is TmuxUnavailableException
            or TmuxOperationException
            or AgentLifecycleException;

    private static TmuxHealth RuntimeFailureHealth(Exception exception) =>
        new(false, false, SessionObservedState.Error, SafeErrorCode(exception.Message));

    private static AgentLifecycleException ToLifecycleException(Exception exception) =>
        exception as AgentLifecycleException
        ?? new AgentLifecycleException("agent_runtime_unavailable", "The native runtime is unavailable.");

    private static string? SafeErrorCode(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        if (error.Contains("tmux", StringComparison.OrdinalIgnoreCase))
        {
            return "tmux_unavailable";
        }

        if (error.Contains("Claude", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Codex", StringComparison.OrdinalIgnoreCase)
            || error.Contains("native", StringComparison.OrdinalIgnoreCase))
        {
            return "native_runtime_unhealthy";
        }

        return "agent_runtime_error";
    }

    private static string ToDatabaseValue(AgentDesiredState state) => state switch
    {
        AgentDesiredState.Running => "running",
        AgentDesiredState.Stopped => "stopped",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static string ToDatabaseValue(SessionObservedState state) => state switch
    {
        SessionObservedState.Missing => "missing",
        SessionObservedState.Starting => "starting",
        SessionObservedState.Running => "running",
        SessionObservedState.Exited => "exited",
        SessionObservedState.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
