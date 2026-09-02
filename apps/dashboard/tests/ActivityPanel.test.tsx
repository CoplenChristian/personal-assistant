import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { ActivityPanel } from "../src/features/agents/ActivityPanel";
import type { ActivityApi, ActivitySnapshot } from "../src/api/activityApi";

const zeroCounters = {
  promptsDelivered: 0,
  scheduledRuns: 0,
  scheduledPromptsQueued: 0,
  scheduledPromptsDropped: 0,
  emailReads: 0,
  emailModifications: 0,
  messagesSent: 0,
  messagesReplied: 0,
  messagesBlocked: 0,
  calendarWrites: 0,
  reminderWrites: 0,
  memoryWrites: 0,
  memoryCheckpoints: 0,
  documentIndexing: 0,
  browserActions: 0,
  securityBlocked: 0,
  failures: 0,
  agentStarts: 0,
  agentStops: 0,
  agentClears: 0,
  agentRotations: 0,
  rosterChanges: 0,
};

const snapshot: ActivitySnapshot = {
  contractVersion: "phase-0c-activity.v1",
  date: "2026-09-01",
  timezone: "UTC",
  counters: { ...zeroCounters, agentStarts: 2, securityBlocked: 1, failures: 1 },
  recentEvents: [
    {
      id: "blocked-event",
      timestamp: "2026-09-01T18:00:00.000Z",
      agentId: "personal",
      realm: "personal",
      category: "agents",
      operation: "clear",
      target: "runtime-session",
      status: "blocked",
      durationMs: null,
      metadataJson: "{}",
    },
    {
      id: "start-event",
      timestamp: "2026-09-01T12:00:00.000Z",
      agentId: "personal",
      realm: "personal",
      category: "agents",
      operation: "start",
      target: "runtime-session",
      status: "success",
      durationMs: null,
      metadataJson: "{}",
    },
  ],
  feedLimit: 50,
};

function createMockApi(getActivity: ActivityApi["getActivity"]): ActivityApi {
  return { getActivity };
}

describe("ActivityPanel", () => {
  afterEach(() => {
    cleanup();
  });

  it("renders local-day counters, feed items, and blocked/failure labels", async () => {
    const getActivity = vi.fn(async () => snapshot);
    render(<ActivityPanel api={createMockApi(getActivity)} />);

    expect(await screen.findByText("Local day 2026-09-01 (UTC)")).toBeInTheDocument();
    expect(screen.getByText("Agent starts")).toBeInTheDocument();
    expect(screen.getByLabelText("Local-day activity counters")).toHaveTextContent("2");
    expect(screen.getByText("agents · clear")).toBeInTheDocument();
    expect(screen.getByText("Blocked")).toBeInTheDocument();
    expect(screen.getByText("agents · start")).toBeInTheDocument();
    expect(getActivity).toHaveBeenCalledTimes(1);
  });

  it("shows an empty-state message when no events exist for the local day", async () => {
    render(<ActivityPanel api={createMockApi(async () => ({
      ...snapshot,
      counters: zeroCounters,
      recentEvents: [],
    }))} />);

    expect(await screen.findByText("No activity recorded for this local day yet.")).toBeInTheDocument();
  });

  it("refreshes activity independently without relying on terminal transport", async () => {
    const getActivity = vi.fn(async () => snapshot);
    render(<ActivityPanel api={createMockApi(getActivity)} />);
    await screen.findByText("Local day 2026-09-01 (UTC)");

    fireEvent.click(screen.getByRole("button", { name: "Refresh activity" }));

    await waitFor(() => expect(getActivity).toHaveBeenCalledTimes(2));
  });

  it("formats event times using the activity timezone", async () => {
    const baseEvent = snapshot.recentEvents[0]!;
    const getActivity = vi.fn(async (): Promise<ActivitySnapshot> => ({
      ...snapshot,
      timezone: "America/New_York",
      recentEvents: [{
        ...baseEvent,
        timestamp: "2026-09-01T18:00:00.000Z",
      }],
    }));
    render(<ActivityPanel api={createMockApi(getActivity)} />);

    expect(await screen.findByText("Local day 2026-09-01 (America/New_York)")).toBeInTheDocument();
    expect(screen.getByText("02:00:00 PM")).toBeInTheDocument();
  });

  it("surfaces load errors with a retry action", async () => {
    const getActivity = vi.fn(async () => {
      throw new Error("offline");
    });
    render(<ActivityPanel api={createMockApi(getActivity)} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("The activity feed could not load.");
    expect(screen.getByRole("button", { name: "Retry activity" })).toBeInTheDocument();
  });
});
