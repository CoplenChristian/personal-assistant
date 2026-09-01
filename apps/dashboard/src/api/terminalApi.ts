export const TERMINAL_PROTOCOL = "phase-0c-terminal.standardized.v1";
export const TERMINAL_SOCKET_PATH = "/ws/agents/personal/terminal";

export type TerminalConnectionState = "connecting" | "connected" | "reconnecting" | "disconnected" | "error";
export type TerminalActivityState = "idle" | "busy" | "waiting" | "error";

export interface TerminalHelloFrame {
  type: "hello";
  protocol: string;
  agentId: string;
}

export interface TerminalScreenFrame {
  type: "screen";
  sequence: number;
  data: string;
  columns: number;
  rows: number;
  hydrationBoundary: boolean;
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
  | TerminalScreenFrame
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
    case "screen":
      return requireScreen(value);
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

function requireScreen(value: Record<string, unknown>): TerminalScreenFrame {
  if (!isNonNegativeInteger(value.sequence)
    || typeof value.data !== "string"
    || !isNonNegativeInteger(value.columns)
    || !isNonNegativeInteger(value.rows)
    || typeof value.hydrationBoundary !== "boolean") {
    throw new TerminalProtocolError("The terminal screen frame is invalid.");
  }
  return value as unknown as TerminalScreenFrame;
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
