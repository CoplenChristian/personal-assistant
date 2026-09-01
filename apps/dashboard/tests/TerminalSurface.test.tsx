import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import { StrictMode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { TerminalSurface } from "../src/features/agents/TerminalSurface";

const { FakeWebSocket, FakeTerminal, FakeFitAddon } = vi.hoisted(() => {
  class HoistedFakeWebSocket {
    static instances: HoistedFakeWebSocket[] = [];
    readonly url: string;
    readonly close = vi.fn();
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
      for (const listener of this.listeners.get(type) ?? []) {
        listener(event);
      }
    }
  }

  class HoistedFakeTerminal {
    static instances: HoistedFakeTerminal[] = [];
    readonly write = vi.fn();
    readonly open = vi.fn();
    readonly loadAddon = vi.fn();
    readonly dispose = vi.fn();

    constructor() {
      HoistedFakeTerminal.instances.push(this);
    }
  }

  class HoistedFakeFitAddon {
    readonly fit = vi.fn();
  }

  return {
    FakeWebSocket: HoistedFakeWebSocket,
    FakeTerminal: HoistedFakeTerminal,
    FakeFitAddon: HoistedFakeFitAddon,
  };
});

vi.mock("@xterm/xterm", () => ({ Terminal: FakeTerminal }));
vi.mock("@xterm/addon-fit", () => ({ FitAddon: FakeFitAddon }));

function send(socket: InstanceType<typeof FakeWebSocket>, frame: unknown): void {
  act(() => socket.emit("message", { data: JSON.stringify(frame) }));
}

describe("TerminalSurface", () => {
  beforeEach(() => {
    FakeWebSocket.instances = [];
    FakeTerminal.instances = [];
    vi.stubGlobal("WebSocket", FakeWebSocket);
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it("opens the observer, hydrates xterm, and applies monotonic output", async () => {
    render(<TerminalSurface scrollbackLines={5000} />);
    const socket = FakeWebSocket.instances[0]!;
    const terminal = FakeTerminal.instances[0]!;

    expect(socket.url).toBe("ws://localhost:3000/ws/agents/personal/terminal");
    expect(screen.getByRole("status")).toHaveTextContent("Connecting");

    act(() => socket.emit("open"));
    expect(screen.getByRole("status")).toHaveTextContent("Live stream");

    send(socket, { type: "hello", protocol: "phase-0c-terminal.v1", agentId: "personal" });
    send(socket, {
      type: "snapshot",
      sequence: 0,
      data: "ready\r\n",
      scrollbackLines: 5000,
      hydrationBoundary: true,
    });
    send(socket, { type: "output", sequence: 1, data: "streamed\r\n" });

    await waitFor(() => expect(screen.getByText("Hydrated from tmux")).toBeInTheDocument());
    expect(terminal.write).toHaveBeenNthCalledWith(1, "ready\r\n");
    expect(terminal.write).toHaveBeenNthCalledWith(2, "streamed\r\n");

    send(socket, { type: "output", sequence: 1, data: "duplicate\r\n" });
    expect(await screen.findByRole("alert")).toHaveTextContent("sequence moved backwards");
  });

  it("reconnects from the observer surface and closes the socket on unmount", async () => {
    const { unmount } = render(<TerminalSurface scrollbackLines={100} />);
    const firstSocket = FakeWebSocket.instances[0]!;

    act(() => firstSocket.emit("close"));
    expect(screen.getByRole("status")).toHaveTextContent("Disconnected");
    act(() => screen.getByRole("button", { name: "Reconnect stream" }).click());
    await waitFor(() => expect(FakeWebSocket.instances).toHaveLength(2));

    unmount();
    expect(firstSocket.close).toHaveBeenCalledWith(1000, "terminal observer unmounted");
    expect(FakeWebSocket.instances[1]!.close).toHaveBeenCalledWith(1000, "terminal observer unmounted");
  });

  it("replaces a hydrated stream after disconnect without reusing its terminal buffer", async () => {
    const { unmount } = render(<TerminalSurface scrollbackLines={100} />);
    const firstSocket = FakeWebSocket.instances[0]!;

    act(() => firstSocket.emit("open"));
    send(firstSocket, { type: "hello", protocol: "phase-0c-terminal.v1", agentId: "personal" });
    send(firstSocket, {
      type: "snapshot",
      sequence: 0,
      data: "first\r\n",
      scrollbackLines: 100,
      hydrationBoundary: true,
    });
    send(firstSocket, { type: "output", sequence: 1, data: "live\r\n" });
    await waitFor(() => expect(screen.getByText("Hydrated from tmux")).toBeInTheDocument());

    act(() => firstSocket.emit("close"));
    expect(await screen.findByRole("button", { name: "Reconnect stream" })).toBeInTheDocument();
    act(() => screen.getByRole("button", { name: "Reconnect stream" }).click());
    await waitFor(() => expect(FakeWebSocket.instances).toHaveLength(2));

    const secondSocket = FakeWebSocket.instances[1]!;
    act(() => secondSocket.emit("open"));
    send(secondSocket, { type: "hello", protocol: "phase-0c-terminal.v1", agentId: "personal" });
    send(secondSocket, {
      type: "snapshot",
      sequence: 0,
      data: "fresh\r\n",
      scrollbackLines: 100,
      hydrationBoundary: true,
    });
    await waitFor(() => expect(FakeTerminal.instances).toHaveLength(2));
    expect(FakeTerminal.instances[1]!.write).toHaveBeenCalledWith("fresh\r\n");
    expect(FakeTerminal.instances[1]!.write).not.toHaveBeenCalledWith("first\r\n");

    unmount();
    expect(firstSocket.close).toHaveBeenCalledWith(1000, "terminal observer unmounted");
    expect(secondSocket.close).toHaveBeenCalledWith(1000, "terminal observer unmounted");
  });

  it("cleans up both observer lifecycles under Strict Mode", async () => {
    const { unmount } = render(
      <StrictMode>
        <TerminalSurface scrollbackLines={100} />
      </StrictMode>,
    );

    await waitFor(() => expect(FakeWebSocket.instances).toHaveLength(2));
    unmount();

    expect(FakeWebSocket.instances[0]!.close).toHaveBeenCalledWith(1000, "terminal observer unmounted");
    expect(FakeWebSocket.instances[1]!.close).toHaveBeenCalledWith(1000, "terminal observer unmounted");
    expect(FakeTerminal.instances[0]!.dispose).toHaveBeenCalledOnce();
    expect(FakeTerminal.instances[1]!.dispose).toHaveBeenCalledOnce();
  });
});
