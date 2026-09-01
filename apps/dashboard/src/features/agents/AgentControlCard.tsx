import { useCallback, useEffect, useMemo, useState } from "react";

import { AgentApiError, createAgentApi } from "../../api/agentsApi";
import type { AgentStatus } from "../../api/agentsApi";

type Action = "start" | "stop" | null;

function errorMessage(error: unknown): string {
  if (error instanceof AgentApiError) {
    return error.message;
  }
  return "The local agent service could not complete that request.";
}

function observedLabel(status: AgentStatus): string {
  if (status.runtimeHealthy) {
    return "Running";
  }

  switch (status.observedState) {
    case "starting":
      return "Starting";
    case "missing":
      return "Not present";
    case "exited":
      return "Exited";
    case "error":
      return "Unavailable";
    default:
      return "Not running";
  }
}

function statusClass(status: AgentStatus): string {
  if (status.runtimeHealthy) {
    return "agent-card__state--green";
  }
  if (status.observedState === "error") {
    return "agent-card__state--red";
  }
  return "agent-card__state--amber";
}

export function AgentControlCard() {
  const api = useMemo(() => createAgentApi(), []);
  const [status, setStatus] = useState<AgentStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [action, setAction] = useState<Action>(null);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const loadStatus = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setStatus(await api.getPersonal());
    } catch (loadError) {
      setError(errorMessage(loadError));
    } finally {
      setLoading(false);
    }
  }, [api]);

  useEffect(() => {
    void loadStatus();
  }, [loadStatus]);

  async function runAction(nextAction: Exclude<Action, null>) {
    setAction(nextAction);
    setError(null);
    setMessage(null);
    try {
      const next = nextAction === "start" ? await api.startPersonal() : await api.stopPersonal();
      setStatus(next);
      setMessage(nextAction === "start" ? "Start requested." : "Stop requested.");
    } catch (actionError) {
      setError(errorMessage(actionError));
      try {
        setStatus(await api.getPersonal());
      } catch {
        // Preserve the action error if the follow-up status request is also unavailable.
      }
    } finally {
      setAction(null);
    }
  }

  return (
    <section className="agent-card" aria-labelledby="personal-agent-title">
      <div className="agent-card__topline">
        <div>
          <p className="eyebrow">NATIVE RUNTIME / PHASE 0B</p>
          <h2 id="personal-agent-title">Personal agent</h2>
        </div>
        <span className="agent-card__sigil" aria-hidden="true">01</span>
      </div>

      {loading ? (
        <div className="agent-card__loading" aria-label="Loading personal agent status">
          <span />
          <span />
        </div>
      ) : error && !status ? (
        <div className="agent-card__empty" role="alert">
          <p>{error}</p>
          <button className="text-button" type="button" onClick={() => void loadStatus()}>Retry status</button>
        </div>
      ) : status ? (
        <>
          <div className="agent-card__identity">
            <div>
              <strong>{status.name}</strong>
              <span>{status.runtime === "claude" ? "Claude Code" : status.runtime}</span>
            </div>
            <span className={`agent-card__state ${statusClass(status)}`}>
              <span className="status-orb" aria-hidden="true" />{observedLabel(status)}
            </span>
          </div>

          <dl className="agent-card__details">
            <div>
              <dt>Desired</dt>
              <dd>{status.desiredState === "running" ? "Running" : "Stopped"}</dd>
            </div>
            <div>
              <dt>Observed</dt>
              <dd>{observedLabel(status)}</dd>
            </div>
            <div>
              <dt>tmux session</dt>
              <dd>{status.tmuxSessionName}</dd>
            </div>
          </dl>

          <p className="agent-card__note">
            {status.runtimeHealthy
              ? "Claude is present in the managed session. Open the terminal workspace to observe it."
              : status.lastError ?? "The session is not running a healthy Claude process."}
          </p>

          <div className="agent-card__actions">
            {status.runtimeHealthy ? (
              <button className="button button--quiet" type="button" onClick={() => void runAction("stop")} disabled={action !== null}>
                {action === "stop" ? "Stopping…" : "Stop agent"}
              </button>
            ) : (
              <button className="button button--primary" type="button" onClick={() => void runAction("start")} disabled={action !== null}>
                {action === "start" ? "Starting…" : "Start agent"}
              </button>
            )}
            <a className="text-button agent-card__open" href="/agents/personal">Open terminal <span aria-hidden="true">↗</span></a>
            <span className="agent-card__boundary">No prompt input in 0C</span>
          </div>
          {message ? <p className="agent-card__feedback" role="status">{message}</p> : null}
          {error ? <p className="agent-card__feedback agent-card__feedback--error" role="alert">{error}</p> : null}
        </>
      ) : null}
    </section>
  );
}
