import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { PersonalAgentPage } from "../src/features/agents/PersonalAgentPage";
import type { AgentStatus } from "../src/api/agentsApi";

vi.mock("../src/features/agents/StandardizedTerminalSurface", () => ({
  StandardizedTerminalSurface: ({ scrollbackLines }: { scrollbackLines: number }) => (
    <div role="region" aria-label="Standardized terminal">Standardized terminal · {scrollbackLines} lines</div>
  ),
}));

vi.mock("../src/features/agents/ActivityPanel", () => ({
  ActivityPanel: () => <div role="region" aria-label="Harness activity">Harness activity panel</div>,
}));

const baseStatus: AgentStatus = {
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

const settings = {
  contractVersion: "phase-0a-settings.v1",
  settings: [{ key: "appearance.browserScrollbackLines", value: 7500 }],
  safety: [],
  integrations: [],
};

function response(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

describe("PersonalAgentPage", () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it("renders the terminal surface for a healthy personal session", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) =>
      String(input).includes("/api/settings")
        ? response(settings)
        : response({ ...baseStatus, desiredState: "running", observedState: "running", sessionDetected: true, runtimeHealthy: true })));

    render(<PersonalAgentPage />);

    expect(await screen.findByRole("region", { name: "Standardized terminal" })).toHaveTextContent("7500 lines");
    expect(screen.getByRole("heading", { name: /the native session/i })).toBeInTheDocument();
  });

  it("keeps the terminal unavailable when the native session is not healthy", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) =>
      String(input).includes("/api/settings") ? response(settings) : response(baseStatus)));

    render(<PersonalAgentPage />);

    expect(await screen.findByRole("heading", { name: "Start the personal agent to open its terminal." })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Harness activity" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open lifecycle controls/i })).toHaveAttribute("href", "/");
  });
});
