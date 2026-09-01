import { useEffect, useRef, useState } from "react";
import { FitAddon } from "@xterm/addon-fit";
import { Terminal } from "@xterm/xterm";
import "@xterm/xterm/css/xterm.css";

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

interface TerminalSurfaceProps {
  scrollbackLines: number;
}

function connectionLabel(state: TerminalConnectionState): string {
  switch (state) {
    case "connecting":
      return "Connecting";
    case "connected":
      return "Live stream";
    case "reconnecting":
      return "Reconnecting";
    case "error":
      return "Stream error";
    default:
      return "Disconnected";
  }
}

function terminalErrorMessage(frame: TerminalErrorFrame): string {
  return frame.detail ?? `The terminal stream reported ${frame.code}.`;
}

function activityLabel(state: TerminalActivityState): string {
  return state.charAt(0).toUpperCase() + state.slice(1);
}

export function TerminalSurface({ scrollbackLines }: TerminalSurfaceProps) {
  const terminalElement = useRef<HTMLDivElement | null>(null);
  const [connectionState, setConnectionState] = useState<TerminalConnectionState>("connecting");
  const [activityState, setActivityState] = useState<TerminalActivityState>("idle");
  const [hydrated, setHydrated] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [connectionAttempt, setConnectionAttempt] = useState(0);

  useEffect(() => {
    if (!terminalElement.current) {
      return undefined;
    }

    let cancelled = false;
    let lastSequence = 0;
    let nextInputSequence = 1;
    const terminal = new Terminal({
      scrollback: scrollbackLines,
      disableStdin: false,
      cursorBlink: false,
      convertEol: true,
      fontFamily: "SF Mono, JetBrains Mono, Courier New, monospace",
      fontSize: 13,
      theme: {
        background: "#070c0d",
        foreground: "#d9e4dc",
        cursor: "#efb765",
        selectionBackground: "rgba(239, 183, 101, 0.24)",
      },
    });
    const fitAddon = new FitAddon();
    terminal.loadAddon(fitAddon);
    terminal.open(terminalElement.current);
    fitAddon.fit();
    terminal.focus();

    const socket = new WebSocket(terminalWebSocketUrl());
    setConnectionState(connectionAttempt === 0 ? "connecting" : "reconnecting");
    setHydrated(false);
    setError(null);

    const sendFrame = (frame: object): boolean => {
      if (cancelled) {
        return false;
      }

      if (socket.readyState !== WebSocket.OPEN) {
        setConnectionState("error");
        setError("The terminal is not connected; input was not sent.");
        return false;
      }

      try {
        socket.send(JSON.stringify(frame));
        return true;
      } catch {
        setConnectionState("error");
        setError("The terminal input could not be sent.");
        return false;
      }
    };

    const inputSubscription = terminal.onData((data) => {
      if (sendFrame({ type: "input", sequence: nextInputSequence, data })) {
        nextInputSequence += 1;
      }
    });
    const resizeSubscription = terminal.onResize(({ cols, rows }) => {
      sendFrame({ type: "resize", columns: cols, rows });
    });
    const handleWindowResize = () => fitAddon.fit();
    window.addEventListener("resize", handleWindowResize);

    socket.addEventListener("open", () => {
      if (!cancelled) {
        setConnectionState("connected");
        fitAddon.fit();
        if (terminal.cols > 0 && terminal.rows > 0) {
          sendFrame({ type: "resize", columns: terminal.cols, rows: terminal.rows });
        }
      }
    });

    socket.addEventListener("message", (event) => {
      if (cancelled) {
        return;
      }

      if (typeof event.data !== "string") {
        setConnectionState("error");
        setError("The terminal returned a non-text frame.");
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

        if (frame.type === "snapshot") {
          lastSequence = frame.sequence;
          terminal.write(frame.data);
          setHydrated(true);
          return;
        }

        if (frame.type === "output") {
          if (frame.sequence <= lastSequence) {
            throw new Error("The terminal output sequence moved backwards.");
          }
          lastSequence = frame.sequence;
          terminal.write(frame.data);
          return;
        }

        if (frame.type === "state") {
          setActivityState(frame.state);
          return;
        }

        if (frame.type === "inputAck" || frame.type === "pong") {
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
        setError("The terminal stream could not be reached.");
      }
    });

    socket.addEventListener("close", () => {
      if (!cancelled) {
        setConnectionState("disconnected");
      }
    });

    return () => {
      cancelled = true;
      inputSubscription.dispose();
      resizeSubscription.dispose();
      window.removeEventListener("resize", handleWindowResize);
      socket.close(1000, "terminal observer unmounted");
      terminal.dispose();
    };
  }, [connectionAttempt, scrollbackLines]);

  const stateClass = connectionState === "connected" ? "terminal-status--green" : connectionState === "error" ? "terminal-status--red" : "terminal-status--amber";

  return (
    <section className="terminal-workspace" aria-labelledby="terminal-surface-title">
      <header className="terminal-workspace__header">
        <div>
          <p className="eyebrow">NATIVE SESSION / PERSONAL</p>
          <h2 id="terminal-surface-title">Terminal view</h2>
        </div>
        <div className={`terminal-status ${stateClass}`} role="status" aria-live="polite">
          <span className="status-orb" aria-hidden="true" />
          <span>{connectionLabel(connectionState)}</span>
        </div>
      </header>
      <div className="terminal-workspace__meta">
        <span>{hydrated ? "Hydrated from tmux" : "Waiting for hydration"}</span>
        <span className="terminal-workspace__state" role="status" aria-live="polite">State: {activityLabel(activityState)}</span>
        <span>{scrollbackLines.toLocaleString()} lines</span>
      </div>
      <div className="terminal-surface" aria-label="Personal Claude terminal">
        <div className="terminal-surface__viewport" ref={terminalElement} />
        {connectionState === "disconnected" || connectionState === "error" ? (
          <div className="terminal-surface__message">
            <span className="eyebrow">OBSERVER STATUS</span>
            <strong>{error ?? "The native terminal observer is disconnected."}</strong>
            <button className="button button--quiet" type="button" onClick={() => setConnectionAttempt((attempt) => attempt + 1)}>
              Reconnect stream
            </button>
          </div>
        ) : null}
      </div>
      {error && hydrated ? <p className="terminal-workspace__error" role="alert">{error}</p> : null}
    </section>
  );
}
