import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { StandardizedTerminalSurface } from "../src/features/agents/StandardizedTerminalSurface";

const { FakeWebSocket } = vi.hoisted(() => {
  class HoistedFakeWebSocket {
    static instances: HoistedFakeWebSocket[] = [];
    static OPEN = 1;
    static CLOSED = 3;
    readonly url: string;
    readyState = 0;
    readonly send = vi.fn();
    readonly close = vi.fn(() => {
      this.readyState = HoistedFakeWebSocket.CLOSED;
    });
    private readonly listeners = new Map<string, Array<(event: { data?: unknown }) => void>>();

    constructor(url: string) {
      this.url = url;
      HoistedFakeWebSocket.instances.push(this);
    }

    addEventListener(type: string, listener: (event: { data?: unknown }) => void): void {
      const listeners = this.listeners.get(type) ?? [];
      listeners.push(listener);
      this.listeners.set(type, listeners);
    }

    emit(type: string, event: { data?: unknown } = {}): void {
      if (type === "open") {
        this.readyState = HoistedFakeWebSocket.OPEN;
      } else if (type === "close") {
        this.readyState = HoistedFakeWebSocket.CLOSED;
      }
      for (const listener of this.listeners.get(type) ?? []) {
        listener(event);
      }
    }
  }

  return { FakeWebSocket: HoistedFakeWebSocket };
});

function send(socket: InstanceType<typeof FakeWebSocket>, frame: unknown): void {
  act(() => socket.emit("message", { data: JSON.stringify(frame) }));
}

describe("StandardizedTerminalSurface", () => {
  beforeEach(() => {
    FakeWebSocket.instances = [];
    vi.stubGlobal("WebSocket", FakeWebSocket);
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it("renders canonical screen frames and reports harness input acceptance", async () => {
    render(<StandardizedTerminalSurface scrollbackLines={5000} />);
    const socket = FakeWebSocket.instances[0]!;
    actOpen(socket);
    send(socket, { type: "hello", protocol: "phase-0c-terminal.standardized.v1", agentId: "personal" });
    send(socket, { type: "screen", sequence: 0, data: "canonical screen", columns: 80, rows: 24, hydrationBoundary: true });
    send(socket, { type: "state", state: "idle" });

    expect(await screen.findByText("canonical screen")).toBeInTheDocument();
    expect(screen.getByText("State: Idle")).toBeInTheDocument();
    expect(screen.getByText("Fixed viewport · 80 × 24")).toBeInTheDocument();

    const input = screen.getByLabelText("Standardized terminal input");
    fireEvent.change(input, { target: { value: "list files" } });
    fireEvent.click(screen.getByRole("button", { name: "Send input" }));
    expect(socket.send).toHaveBeenCalledWith(JSON.stringify({ type: "input", sequence: 1, data: "list files\r" }));
    send(socket, { type: "inputAck", sequence: 1 });
    expect(await screen.findByText("Input 1 accepted by harness.")).toBeInTheDocument();
  });

  it("replaces the canonical screen after reconnect without retaining the prior screen", async () => {
    render(<StandardizedTerminalSurface scrollbackLines={5000} />);
    const firstSocket = FakeWebSocket.instances[0]!;
    actOpen(firstSocket);
    send(firstSocket, { type: "hello", protocol: "phase-0c-terminal.standardized.v1", agentId: "personal" });
    send(firstSocket, { type: "screen", sequence: 0, data: "first screen", columns: 80, rows: 24, hydrationBoundary: true });
    expect(await screen.findByText("first screen")).toBeInTheDocument();

    firstSocket.emit("close");
    fireEvent.click(await screen.findByRole("button", { name: "Reconnect screen" }));
    await waitFor(() => expect(FakeWebSocket.instances).toHaveLength(2));
    const secondSocket = FakeWebSocket.instances[1]!;
    actOpen(secondSocket);
    send(secondSocket, { type: "hello", protocol: "phase-0c-terminal.standardized.v1", agentId: "personal" });
    send(secondSocket, { type: "screen", sequence: 0, data: "fresh screen", columns: 100, rows: 30, hydrationBoundary: true });

    expect(await screen.findByText("fresh screen")).toBeInTheDocument();
    expect(screen.queryByText("first screen")).not.toBeInTheDocument();
  });
});

function actOpen(socket: InstanceType<typeof FakeWebSocket>): void {
  act(() => socket.emit("open"));
}
