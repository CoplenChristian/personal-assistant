import { useCallback, useEffect, useMemo, useState } from "react";

import { AgentApiError, createAgentApi } from "../../api/agentsApi";
import { createSettingsApi } from "../../api/settingsApi";
import type { AgentStatus } from "../../api/agentsApi";
import { SessionHygieneControls } from "./SessionHygieneControls";
import { StandardizedTerminalSurface } from "./StandardizedTerminalSurface";
import { ActivityPanel } from "./ActivityPanel";

function errorMessage(error: unknown): string {
  if (error instanceof AgentApiError) {
    return error.message;
  }
  return "The local agent workspace could not load.";
}

export function PersonalAgentPage() {
  const agentApi = useMemo(() => createAgentApi(), []);
  const settingsApi = useMemo(() => createSettingsApi(), []);
  const [status, setStatus] = useState<AgentStatus | null>(null);
  const [scrollbackLines, setScrollbackLines] = useState(5000);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadWorkspace = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [agent, settings] = await Promise.all([agentApi.getPersonal(), settingsApi.getSettings()]);
      setStatus(agent);
      const scrollback = settings.settings.find((setting) => setting.key === "appearance.browserScrollbackLines")?.value;
      if (typeof scrollback === "number" && Number.isInteger(scrollback)) {
        setScrollbackLines(scrollback);
      }
    } catch (loadError) {
      setError(errorMessage(loadError));
    } finally {
      setLoading(false);
    }
  }, [agentApi, settingsApi]);

  useEffect(() => {
    void loadWorkspace();
  }, [loadWorkspace]);

  if (loading) {
    return (
      <section className="agent-page agent-page--loading" aria-labelledby="agent-page-loading-title">
        <span className="eyebrow">NATIVE RUNTIME / PHASE 0C</span>
        <h1 id="agent-page-loading-title">Opening the personal session.</h1>
        <div className="loading-stack" aria-label="Loading agent workspace"><span /><span /><span /></div>
      </section>
    );
  }

  if (error || !status) {
    return (
      <section className="agent-page agent-page--error" aria-labelledby="agent-page-error-title">
        <span className="eyebrow">NATIVE RUNTIME / PHASE 0C</span>
        <h1 id="agent-page-error-title">The workspace is temporarily out of reach.</h1>
        <p>{error ?? "The personal agent status was not available."}</p>
        <button className="button button--primary" type="button" onClick={() => void loadWorkspace()}>Retry workspace</button>
      </section>
    );
  }

  return (
    <section className="agent-page" aria-labelledby="agent-page-title">
      <header className="agent-page__hero">
        <div>
          <span className="eyebrow">NATIVE RUNTIME / PHASE 0C</span>
          <h1 id="agent-page-title">The native session,<br /><em>in view.</em></h1>
          <p>Observe the personal Claude process where it already lives: inside the durable tmux session.</p>
        </div>
        <a className="button button--quiet" href="/">Back to overview <span aria-hidden="true">↗</span></a>
      </header>

      <div className="agent-page__identity">
        <div>
          <span className="eyebrow">LOGICAL AGENT</span>
          <strong>{status.name}</strong>
          <span>Claude Code · {status.tmuxSessionName}</span>
        </div>
        <div className="agent-page__intent">
          <span className="eyebrow">DESIRED STATE</span>
          <strong>{status.desiredState === "running" ? "Running" : "Stopped"}</strong>
        </div>
      </div>

      <div className="agent-page__workspace">
        {status.runtimeHealthy ? (
          <div className="agent-page__terminal-column">
            <StandardizedTerminalSurface scrollbackLines={scrollbackLines} />
            <SessionHygieneControls />
          </div>
        ) : (
          <section className="terminal-not-ready" aria-labelledby="terminal-not-ready-title">
            <span className="eyebrow">OBSERVER STATUS</span>
            <h2 id="terminal-not-ready-title">Start the personal agent to open its terminal.</h2>
            <p>The workspace will hydrate from tmux once a healthy Claude process is present.</p>
            <a className="button button--primary" href="/">Open lifecycle controls <span aria-hidden="true">↗</span></a>
          </section>
        )}
        <ActivityPanel />
      </div>
    </section>
  );
}
