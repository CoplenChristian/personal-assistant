import { useEffect, useRef, useState } from "react";

import {
  TERMINAL_PROTOCOL,
  parseTerminalFrame,
  terminalWebSocketUrl,
} from "../../api/terminalApi";
import type {
  TerminalActivityState,
  TerminalConnectionState,
  TerminalErrorFrame,
} from "../../api/terminalApi";

interface StandardizedTerminalSurfaceProps {
  scrollbackLines: number;
}

function connectionLabel(state: TerminalConnectionState): string {
  switch (state) {
    case "connecting":
      return "Connecting";
    case "connected":
      return "Live screen";
    case "reconnecting":
      return "Reconnecting";
    case "error":
      return "Screen error";
    default:
      return "Disconnected";
  }
}

function activityLabel(state: TerminalActivityState): string {
  return state.charAt(0).toUpperCase() + state.slice(1);
}

function terminalErrorMessage(frame: TerminalErrorFrame): string {
  return frame.detail ?? `The terminal reported ${frame.code}.`;
}

export function StandardizedTerminalSurface({ scrollbackLines }: StandardizedTerminalSurfaceProps) {
  const screenElement = useRef<HTMLPreElement | null>(null);
  const sendInput = useRef<((data: string) => boolean) | null>(null);
  const deliveryTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [connectionState, setConnectionState] = useState<TerminalConnectionState>("connecting");
  const [activityState, setActivityState] = useState<TerminalActivityState>("idle");
  const [screenData, setScreenData] = useState("");
  const [screenDimensions, setScreenDimensions] = useState({ columns: 0, rows: 0 });
  const [hydrated, setHydrated] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [delivery, setDelivery] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [connectionAttempt, setConnectionAttempt] = useState(0);

  useEffect(() => {
    if (!screenElement.current) {
      return undefined;
    }

    let cancelled = false;
    let lastScreenSequence = -1;
    let nextInputSequence = 1;
    const socket = new WebSocket(terminalWebSocketUrl());

    setConnectionState(connectionAttempt === 0 ? "connecting" : "reconnecting");
    setActivityState("idle");
    setHydrated(false);
    setScreenData("");
    setScreenDimensions({ columns: 0, rows: 0 });
    setError(null);
    setDelivery(null);
    if (deliveryTimer.current) {
      clearTimeout(deliveryTimer.current);
      deliveryTimer.current = null;
    }

    const showDeliveryStatus = (message: string) => {
      if (deliveryTimer.current) {
        clearTimeout(deliveryTimer.current);
      }

      setDelivery(message);
      deliveryTimer.current = setTimeout(() => {
        setDelivery(null);
        deliveryTimer.current = null;
      }, 5000);
    };

    sendInput.current = (data: string) => {
      if (cancelled || socket.readyState !== WebSocket.OPEN) {
        setConnectionState("error");
        setError("The terminal is not connected; input was not sent.");
        return false;
      }

      try {
        socket.send(JSON.stringify({ type: "input", sequence: nextInputSequence, data }));
        showDeliveryStatus("Input queued.");
        nextInputSequence += 1;
        return true;
      } catch {
        setConnectionState("error");
        setError("The terminal input could not be sent.");
        return false;
      }
    };

    socket.addEventListener("open", () => {
      if (!cancelled) {
        setConnectionState("connected");
      }
    });

    socket.addEventListener("message", (event) => {
      if (cancelled || typeof event.data !== "string") {
        return;
      }

      try {
        const frame = parseTerminalFrame(event.data);
        if (frame.type === "hello") {
          if (frame.protocol !== TERMINAL_PROTOCOL || frame.agentId !== "personal") {
            throw new Error("The terminal handshake did not match this agent.");
          }
          return;
        }

        if (frame.type === "screen") {
          if (frame.sequence <= lastScreenSequence) {
            throw new Error("The standardized screen sequence moved backwards.");
          }
          lastScreenSequence = frame.sequence;
          setScreenData(frame.data);
          setScreenDimensions({ columns: frame.columns, rows: frame.rows });
          setHydrated((current) => current || frame.hydrationBoundary);
          return;
        }

        if (frame.type === "state") {
          setActivityState(frame.state);
          return;
        }

        if (frame.type === "inputAck") {
          showDeliveryStatus("Input accepted by harness.");
          return;
        }

        if (frame.type === "error") {
          setError(terminalErrorMessage(frame));
        }
      } catch (frameError) {
        setConnectionState("error");
        setError(frameError instanceof Error ? frameError.message : "The terminal returned an invalid frame.");
      }
    });

    socket.addEventListener("error", () => {
      if (!cancelled) {
        setConnectionState("error");
        setError("The standardized terminal screen could not be reached.");
      }
    });

    socket.addEventListener("close", () => {
      if (!cancelled) {
        setConnectionState("disconnected");
      }
    });

    return () => {
      cancelled = true;
      sendInput.current = null;
      if (deliveryTimer.current) {
        clearTimeout(deliveryTimer.current);
        deliveryTimer.current = null;
      }
      socket.close(1000, "standardized terminal observer unmounted");
    };
  }, [connectionAttempt]);

  useEffect(() => {
    if (screenElement.current) {
      screenElement.current.scrollTop = screenElement.current.scrollHeight;
    }
  }, [screenData]);

  function submitInput() {
    if (draft.length === 0) {
      return;
    }

    if (sendInput.current?.(`${draft}\r`)) {
      setDraft("");
    }
  }

  const stateClass = connectionState === "connected" ? "terminal-status--green" : connectionState === "error" ? "terminal-status--red" : "terminal-status--amber";

  return (
    <section className="terminal-workspace standardized-terminal" aria-labelledby="standardized-terminal-title">
      <header className="terminal-workspace__header">
        <div>
          <p className="eyebrow">CANONICAL SCREEN / PERSONAL</p>
          <h2 id="standardized-terminal-title">Standardized terminal</h2>
        </div>
        <div className={`terminal-status ${stateClass}`} role="status" aria-live="polite">
          <span className="status-orb" aria-hidden="true" />
          <span>{connectionLabel(connectionState)}</span>
        </div>
      </header>
      <div className="terminal-workspace__meta">
        <span>{hydrated ? "Hydrated screen" : "Waiting for screen"}</span>
        <span className="terminal-workspace__state" role="status" aria-live="polite">State: {activityLabel(activityState)}</span>
        <span>{screenDimensions.columns > 0 ? `Fixed viewport · ${screenDimensions.columns} × ${screenDimensions.rows}` : `Fixed viewport · ${scrollbackLines.toLocaleString()} lines`}</span>
      </div>
      <pre className="standardized-terminal__screen" ref={screenElement} aria-label="Personal Claude terminal screen" role="log" aria-live="polite">
        {screenData || "Waiting for the canonical screen snapshot…"}
      </pre>
      <form className="standardized-terminal__controls" onSubmit={(event) => { event.preventDefault(); submitInput(); }}>
        <label className="standardized-terminal__input-label" htmlFor="standardized-terminal-input">Send native input</label>
        <textarea
          id="standardized-terminal-input"
          aria-label="Standardized terminal input"
          rows={2}
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              submitInput();
            }
          }}
          placeholder="Type a line for the native session…"
        />
        <button className="button button--primary" type="submit" disabled={connectionState !== "connected" || draft.length === 0}>Send input</button>
        <span className="standardized-terminal__delivery" role="status" aria-live="polite">{delivery ?? "Input is held here until you send it."}</span>
      </form>
      {error ? <p className="terminal-workspace__error" role="alert">{error}</p> : null}
      {connectionState === "disconnected" || connectionState === "error" ? (
        <div className="standardized-terminal__reconnect">
          <span>{error ?? "The standardized observer is disconnected."}</span>
          <button className="button button--quiet" type="button" onClick={() => setConnectionAttempt((attempt) => attempt + 1)}>Reconnect screen</button>
        </div>
      ) : null}
    </section>
  );
}
