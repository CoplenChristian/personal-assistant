import { useCallback, useEffect, useMemo, useRef, useState } from "react";

import {
  ActivityApiError,
  COUNTER_KEYS,
  COUNTER_LABELS,
  createActivityApi,
  type ActivityApi,
  type ActivityEventItem,
  type ActivitySnapshot,
} from "../../api/activityApi";

interface ActivityPanelProps {
  api?: ActivityApi;
}

function errorMessage(error: unknown): string {
  if (error instanceof ActivityApiError) {
    return error.message;
  }

  return "The activity feed could not load.";
}

function eventLabel(event: ActivityEventItem): string {
  return `${event.category} · ${event.operation}`;
}

function statusDisplay(status: string): { className: string; label: string } {
  if (status === "blocked") {
    return {
      className: "activity-feed__status activity-feed__status--blocked",
      label: "Blocked",
    };
  }

  if (status === "failure" || status === "error") {
    return {
      className: "activity-feed__status activity-feed__status--failure",
      label: "Failed",
    };
  }

  if (status === "warning") {
    return {
      className: "activity-feed__status activity-feed__status--warning",
      label: status,
    };
  }

  return {
    className: "activity-feed__status",
    label: status,
  };
}

export function ActivityPanel({ api: providedApi }: ActivityPanelProps) {
  const api = useMemo(() => providedApi ?? createActivityApi(), [providedApi]);
  const [snapshot, setSnapshot] = useState<ActivitySnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const loadControllerRef = useRef<AbortController | null>(null);

  const loadActivity = useCallback(async () => {
    loadControllerRef.current?.abort();
    const controller = new AbortController();
    loadControllerRef.current = controller;
    const signal = controller.signal;

    setLoading(true);
    setError(null);
    try {
      const result = await api.getActivity({ signal });
      if (signal.aborted) {
        return;
      }

      setSnapshot(result);
    } catch (loadError) {
      if (signal.aborted) {
        return;
      }

      setSnapshot(null);
      setError(errorMessage(loadError));
    } finally {
      if (!signal.aborted) {
        setLoading(false);
      }
    }
  }, [api]);

  useEffect(() => {
    void loadActivity();
    return () => loadControllerRef.current?.abort();
  }, [loadActivity]);

  const localDayLabel = snapshot
    ? `Local day ${snapshot.date} (${snapshot.timezone})`
    : "Local day activity";

  return (
    <section className="activity-panel" aria-labelledby="activity-panel-title">
      <header className="activity-panel__header">
        <div>
          <span className="eyebrow">IMMUTABLE ACTIVITY</span>
          <h2 id="activity-panel-title">Harness activity</h2>
          <p className="activity-panel__day" aria-live="polite">{localDayLabel}</p>
        </div>
        <button
          className="button button--quiet activity-panel__refresh"
          type="button"
          onClick={() => void loadActivity()}
          disabled={loading}
        >
          Refresh activity
        </button>
      </header>

      {loading && (
        <div className="activity-panel__loading" aria-label="Loading activity feed">
          <span /><span /><span />
        </div>
      )}

      {!loading && error && (
        <div className="activity-panel__error" role="alert">
          <p>{error}</p>
          <button className="button button--primary" type="button" onClick={() => void loadActivity()}>
            Retry activity
          </button>
        </div>
      )}

      {!loading && !error && snapshot && (
        <>
          {snapshot.auditDegraded && (
            <div className="activity-panel__audit-warning" role="status">
              Activity recording is degraded. Recent actions may be missing from this feed.
            </div>
          )}

          <div className="activity-counters" aria-label="Local-day activity counters">
            {COUNTER_KEYS.map((key) => (
              <div className="activity-counter" key={key}>
                <span className="activity-counter__label">{COUNTER_LABELS[key]}</span>
                <strong className="activity-counter__value">{snapshot.counters[key]}</strong>
              </div>
            ))}
          </div>

          <section className="activity-feed" aria-labelledby="activity-feed-title">
            <div className="activity-feed__header">
              <h3 id="activity-feed-title">Recent events</h3>
              <span className="activity-feed__limit">Showing up to {snapshot.feedLimit}</span>
            </div>

            {snapshot.recentEvents.length === 0 ? (
              <p className="activity-feed__empty">No activity recorded for this local day yet.</p>
            ) : (
              <ol className="activity-feed__list">
                {snapshot.recentEvents.map((event) => {
                  const status = statusDisplay(event.status);
                  return (
                    <li className="activity-feed__item" key={event.id}>
                      <div className="activity-feed__summary">
                        <span className="activity-feed__time">
                          {new Date(event.timestamp).toLocaleTimeString("en-US", {
                            hour: "2-digit",
                            minute: "2-digit",
                            second: "2-digit",
                            timeZone: snapshot.timezone,
                            hour12: true,
                          })}
                        </span>
                        <span className={status.className} role="status">
                          {status.label}
                        </span>
                      </div>
                      <strong className="activity-feed__label">{eventLabel(event)}</strong>
                      {event.agentId && (
                        <span className="activity-feed__agent">Agent {event.agentId}</span>
                      )}
                    </li>
                  );
                })}
              </ol>
            )}
          </section>
        </>
      )}
    </section>
  );
}
