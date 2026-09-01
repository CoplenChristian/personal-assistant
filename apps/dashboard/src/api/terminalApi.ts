export const TERMINAL_PROTOCOL = "phase-0c-terminal.v1";
export const TERMINAL_SOCKET_PATH = "/ws/agents/personal/terminal";

export type TerminalConnectionState = "connecting" | "connected" | "reconnecting" | "disconnected" | "error";
export type TerminalActivityState = "idle" | "busy" | "waiting" | "error";

export interface TerminalHelloFrame {
  type: "hello";
  protocol: string;
  agentId: string;
}

export interface TerminalSnapshotFrame {
  type: "snapshot";
  sequence: number;
  data: string;
  scrollbackLines: number;
  hydrationBoundary: true;
}

export interface TerminalOutputFrame {
  type: "output";
  sequence: number;
  data: string;
}

export interface TerminalStateFrame {
  type: "state";
  state: TerminalActivityState;
}

export interface TerminalInputAcknowledgementFrame {
  type: "inputAck";
  sequence: number;
}

export interface TerminalPongFrame {
  type: "pong";
  sequence: number;
}

export interface TerminalErrorFrame {
  type: "error";
  code: string;
  detail?: string;
}

export type TerminalServerFrame =
  | TerminalHelloFrame
  | TerminalSnapshotFrame
  | TerminalOutputFrame
  | TerminalStateFrame
  | TerminalInputAcknowledgementFrame
  | TerminalPongFrame
  | TerminalErrorFrame;

export class TerminalProtocolError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "TerminalProtocolError";
  }
}

export function terminalWebSocketUrl(location: Pick<Location, "protocol" | "host"> = window.location): string {
  const protocol = location.protocol === "https:" ? "wss:" : "ws:";
  return `${protocol}//${location.host}${TERMINAL_SOCKET_PATH}`;
}

export function parseTerminalFrame(payload: unknown): TerminalServerFrame {
  let value: unknown = payload;
  if (typeof payload === "string") {
    try {
      value = JSON.parse(payload);
    } catch {
      throw new TerminalProtocolError("The terminal sent invalid JSON.");
    }
  }

  if (!isRecord(value) || typeof value.type !== "string") {
    throw new TerminalProtocolError("The terminal sent an invalid frame.");
  }

  switch (value.type) {
    case "hello":
      return requireStrings(value, ["protocol", "agentId"]) as TerminalHelloFrame;
    case "snapshot":
      return requireSnapshot(value);
    case "output":
      return requireOutput(value);
    case "state":
      return requireState(value);
    case "inputAck":
      return requireSequence(value);
    case "pong":
      return requireSequence(value);
    case "error":
      return requireStrings(value, ["code"]) as TerminalErrorFrame;
    default:
      throw new TerminalProtocolError("The terminal sent an unsupported frame type.");
  }
}

function requireSnapshot(value: Record<string, unknown>): TerminalSnapshotFrame {
  if (!isNonNegativeInteger(value.sequence)
    || typeof value.data !== "string"
    || !isNonNegativeInteger(value.scrollbackLines)
    || value.hydrationBoundary !== true) {
    throw new TerminalProtocolError("The terminal snapshot is invalid.");
  }
  return value as unknown as TerminalSnapshotFrame;
}

function requireOutput(value: Record<string, unknown>): TerminalOutputFrame {
  if (!isNonNegativeInteger(value.sequence) || typeof value.data !== "string") {
    throw new TerminalProtocolError("The terminal output frame is invalid.");
  }
  return value as unknown as TerminalOutputFrame;
}

function requireStrings(value: Record<string, unknown>, properties: string[]): TerminalServerFrame {
  if (properties.some(property => typeof value[property] !== "string")) {
    throw new TerminalProtocolError("The terminal frame is missing required text.");
  }
  return value as unknown as TerminalServerFrame;
}

function requireState(value: Record<string, unknown>): TerminalStateFrame {
  if (value.state !== "idle" && value.state !== "busy" && value.state !== "waiting" && value.state !== "error") {
    throw new TerminalProtocolError("The terminal state is invalid.");
  }
  return value as unknown as TerminalStateFrame;
}

function requireSequence(value: Record<string, unknown>): TerminalInputAcknowledgementFrame | TerminalPongFrame {
  if (!isNonNegativeInteger(value.sequence)) {
    throw new TerminalProtocolError("The terminal frame is missing a valid sequence.");
  }
  return value as unknown as TerminalInputAcknowledgementFrame | TerminalPongFrame;
}

function isNonNegativeInteger(value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && value >= 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
