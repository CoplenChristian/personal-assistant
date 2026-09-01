import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { AgentControlCard } from "../src/features/agents/AgentControlCard";
import type { AgentStatus } from "../src/api/agentsApi";

const stoppedStatus: AgentStatus = {
  contractVersion: "phase-0b-agents.v1",
  id: "personal",
  name: "Personal",
  runtime: "claude",
  desiredState: "stopped",
  observedState: "missing",
  tmuxSessionName: "pa-personal",
  sessionDetected: false,
  runtimeHealthy: false,
  lastSeenAt: null,
  stoppedAt: null,
  lastError: null,
};

const runningStatus: AgentStatus = {
  ...stoppedStatus,
  desiredState: "running",
  observedState: "running",
  sessionDetected: true,
  runtimeHealthy: true,
};

function response(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("AgentControlCard", () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it("shows desired and observed state without implying tmux is Claude", async () => {
    const fetchMock = vi.fn(async () => response(stoppedStatus));
    vi.stubGlobal("fetch", fetchMock);

    render(<AgentControlCard />);

    expect(await screen.findByRole("heading", { name: "Personal agent" })).toBeInTheDocument();
    expect(screen.getAllByText("Not present")).not.toHaveLength(0);
    expect(screen.getByRole("button", { name: "Start agent" })).toBeInTheDocument();
    expect(screen.getByText("pa-personal")).toBeInTheDocument();
  });

  it("stops a healthy agent through the lifecycle endpoint", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) =>
      init?.method === "POST" ? response(stoppedStatus) : response(runningStatus));
    vi.stubGlobal("fetch", fetchMock);

    render(<AgentControlCard />);
    expect(await screen.findByRole("button", { name: "Stop agent" })).toBeInTheDocument();
    screen.getByRole("button", { name: "Stop agent" }).click();

    await waitFor(() => expect(screen.getByText("Stop requested.")).toBeInTheDocument());
    expect(fetchMock).toHaveBeenCalledWith("/api/agents/personal/stop", expect.objectContaining({ method: "POST" }));
  });

  it("provides a retry surface when agent status is unreachable", async () => {
    const fetchMock = vi.fn(async () => { throw new Error("offline"); });
    vi.stubGlobal("fetch", fetchMock);

    render(<AgentControlCard />);

    expect(await screen.findByRole("alert")).toHaveTextContent("local agent service is unreachable");
    expect(screen.getByRole("button", { name: "Retry status" })).toBeInTheDocument();
  });

  it("refreshes desired and observed state when a start is rejected", async () => {
    let statusRequests = 0;
    const failedStart = new Response(JSON.stringify({ detail: "Claude is unavailable.", code: "agent_runtime_unavailable" }), {
      status: 503,
      headers: { "Content-Type": "application/problem+json" },
    });
    const runningIntentError: AgentStatus = {
      ...stoppedStatus,
      desiredState: "running",
      observedState: "error",
      lastError: "tmux_unavailable",
    };
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === "POST") {
        return failedStart;
      }
      statusRequests += 1;
      return response(statusRequests === 1 ? stoppedStatus : runningIntentError);
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<AgentControlCard />);
    expect(await screen.findByRole("button", { name: "Start agent" })).toBeInTheDocument();
    screen.getByRole("button", { name: "Start agent" }).click();

    await waitFor(() => expect(screen.getByText("Claude is unavailable.")).toBeInTheDocument());
    expect(screen.getByText("Running")).toBeInTheDocument();
    expect(screen.getAllByText("Unavailable")).not.toHaveLength(0);
  });
});
