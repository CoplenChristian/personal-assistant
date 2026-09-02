import { useMemo, useState } from "react";

import {
  createHygieneApi,
  HygieneApiError,
} from "../../api/hygieneApi";
import type {
  HygieneApi,
  SessionHygieneAction,
} from "../../api/hygieneApi";

interface SessionHygieneControlsProps {
  api?: HygieneApi;
}

type Feedback = {
  kind: "success" | "blocked" | "error";
  message: string;
};

const ACTIONS: Array<{ action: SessionHygieneAction; label: string; description: string }> = [
  {
    action: "compact",
    label: "Compact context",
    description: "Checkpoint, then ask the native session to compact its context.",
  },
  {
    action: "clear",
    label: "Clear context",
    description: "Checkpoint, then clear the native conversation context.",
  },
  {
    action: "rotate",
    label: "Rotate conversation",
    description: "Checkpoint, then replace the current native conversation.",
  },
];

function requestId(prefix: string): string {
  const randomUuid = globalThis.crypto?.randomUUID?.();
  return randomUuid ? `${prefix}-${randomUuid}` : `${prefix}-${Date.now()}`;
}

function checkpointFor(reason: string) {
  return {
    reason,
    generatedMemory: "",
    generatedHandoff: "",
  };
}

function errorFeedback(error: unknown, actionLabel: string): Feedback {
  if (!(error instanceof HygieneApiError)) {
    return { kind: "error", message: `The harness could not confirm ${actionLabel.toLowerCase()}. Retry is available.` };
  }

  const blocked = error.code.startsWith("checkpoint")
    || error.code === "hygiene_in_progress"
    || error.code === "agent_runtime_unhealthy";
  return {
    kind: blocked ? "blocked" : "error",
    message: blocked
      ? `No native action was performed. ${error.message}`
      : `The harness could not confirm ${actionLabel.toLowerCase()}. ${error.message} Retry is available.`,
  };
}

export function SessionHygieneControls({ api: providedApi }: SessionHygieneControlsProps) {
  const api = useMemo(() => providedApi ?? createHygieneApi(), [providedApi]);
  const [runningAction, setRunningAction] = useState<SessionHygieneAction | "checkpoint" | null>(null);
  const [feedback, setFeedback] = useState<Feedback | null>(null);

  async function runAction(action: SessionHygieneAction) {
    const definition = ACTIONS.find((item) => item.action === action)!;
    setRunningAction(action);
    setFeedback(null);
    try {
      const result = await api.execute(action, {
        requestId: requestId(`hygiene-${action}`),
        checkpoint: checkpointFor(action),
      });
      setFeedback(result.nativeActionPerformed
        ? { kind: "success", message: `Checkpoint complete. ${definition.label} was accepted by the harness.` }
        : { kind: "blocked", message: "Checkpoint complete. The native action was not performed because the agent is stopped." });
    } catch (error) {
      setFeedback(errorFeedback(error, definition.label));
    } finally {
      setRunningAction(null);
    }
  }

  async function runCheckpoint() {
    setRunningAction("checkpoint");
    setFeedback(null);
    try {
      await api.checkpoint({
        requestId: requestId("checkpoint"),
        checkpoint: checkpointFor("compact"),
      });
      setFeedback({ kind: "success", message: "Checkpoint written. The native session was not changed." });
    } catch (error) {
      setFeedback(errorFeedback(error, "checkpoint"));
    } finally {
      setRunningAction(null);
    }
  }

  const inFlight = runningAction !== null;

  return (
    <section className="session-hygiene" aria-labelledby="session-hygiene-title">
      <header className="session-hygiene__header">
        <div>
          <p className="eyebrow">SESSION HYGIENE / CHECKPOINT GATE</p>
          <h2 id="session-hygiene-title">Change context deliberately.</h2>
          <p>Every context action checkpoints first. Runtime paths and terminal text stay local to the harness.</p>
        </div>
        <span className="session-hygiene__stamp" aria-hidden="true">0C<br />SAFE</span>
      </header>

      <div className="session-hygiene__actions" aria-label="Session hygiene actions">
        {ACTIONS.map((item) => (
          <button
            className="session-hygiene__action"
            key={item.action}
            type="button"
            disabled={inFlight}
            aria-label={runningAction === item.action ? `Checkpointing before ${item.action}…` : item.label}
            aria-describedby={`hygiene-${item.action}-description`}
            onClick={() => void runAction(item.action)}
          >
            <span className="session-hygiene__action-label">
              {runningAction === item.action ? `Checkpointing before ${item.action}…` : item.label}
            </span>
            <span className="session-hygiene__action-description" id={`hygiene-${item.action}-description`}>{item.description}</span>
          </button>
        ))}
        <button
          className="session-hygiene__action session-hygiene__action--quiet"
          type="button"
          disabled={inFlight}
          aria-label={runningAction === "checkpoint" ? "Writing checkpoint…" : "Checkpoint now"}
          onClick={() => void runCheckpoint()}
        >
          <span className="session-hygiene__action-label">{runningAction === "checkpoint" ? "Writing checkpoint…" : "Checkpoint now"}</span>
          <span className="session-hygiene__action-description">Save the current handoff boundary without changing the native session.</span>
        </button>
      </div>

      <div className="session-hygiene__status" role={feedback?.kind === "success" ? "status" : feedback ? "alert" : "status"} aria-live="polite">
        {runningAction ? `Checkpoint first · ${runningAction === "checkpoint" ? "saving local context" : `${runningAction} in progress`}` : feedback?.message ?? "Ready. A checkpoint is required before context changes."}
      </div>
      <p className="session-hygiene__footer">The native session remains under harness control · successful requests are safe to retry.</p>
    </section>
  );
}
