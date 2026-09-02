import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { HygieneApiError } from "../src/api/hygieneApi";
import type { HygieneApi, SessionHygieneResponse } from "../src/api/hygieneApi";
import { SessionHygieneControls } from "../src/features/agents/SessionHygieneControls";

afterEach(() => {
  cleanup();
});

function success(action: "compact" | "clear" | "rotate"): SessionHygieneResponse {
  return {
    contractVersion: "phase-0c-session-hygiene.v1",
    requestId: `request-${action}`,
    action,
    checkpointId: `checkpoint-${action}`,
    desiredState: "running",
    observedState: "running",
    nativeActionPerformed: true,
  };
}

function createApi(overrides: Partial<HygieneApi> = {}): HygieneApi {
  return {
    execute: vi.fn(async (action) => success(action)),
    checkpoint: vi.fn(async () => ({
      contractVersion: "phase-0c-session-hygiene.v1",
      requestId: "checkpoint-request",
      checkpointId: "checkpoint-compact",
    })),
    ...overrides,
  };
}

describe("SessionHygieneControls", () => {
  it("shows checkpoint progress, disables every action, and reports a real success", async () => {
    let resolveAction: ((value: SessionHygieneResponse) => void) | undefined;
    const execute = vi.fn(() => new Promise<SessionHygieneResponse>((resolve) => {
      resolveAction = resolve;
    }));
    const api = createApi({ execute });

    render(<SessionHygieneControls api={api} />);
    fireEvent.click(screen.getByRole("button", { name: "Compact context" }));

    expect(screen.getByRole("button", { name: /checkpointing before compact/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Clear context" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Checkpoint now" })).toBeDisabled();
    expect(screen.getByRole("status")).toHaveTextContent("Checkpoint first");

    resolveAction!(success("compact"));
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("Compact context was accepted by the harness."));
    expect(screen.getByRole("button", { name: "Clear context" })).toBeEnabled();
    expect(execute).toHaveBeenCalledWith("compact", expect.objectContaining({
      checkpoint: expect.objectContaining({ generatedMemory: "", generatedHandoff: "" }),
    }));
  });

  it("keeps blocked checkpoint failures honest and retryable", async () => {
    const execute = vi.fn(async () => {
      throw new HygieneApiError("checkpoint unavailable", { status: 409, code: "checkpoint_write_failed" });
    });
    const api = createApi({ execute });

    render(<SessionHygieneControls api={api} />);
    fireEvent.click(screen.getByRole("button", { name: "Rotate conversation" }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("No native action was performed."));
    expect(screen.getByRole("alert")).toHaveTextContent("checkpoint unavailable");
    expect(screen.queryByText(/accepted by the harness/i)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Rotate conversation" })).toBeEnabled();
  });

  it("can write a checkpoint without claiming that the native session changed", async () => {
    const api = createApi();

    render(<SessionHygieneControls api={api} />);
    fireEvent.click(screen.getByRole("button", { name: "Checkpoint now" }));

    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("The native session was not changed."));
    expect(api.checkpoint).toHaveBeenCalledWith(expect.objectContaining({
      checkpoint: expect.objectContaining({ reason: "compact", generatedMemory: "", generatedHandoff: "" }),
    }));
  });

  it("does not call a skipped native action successful", async () => {
    const api = createApi({
      execute: vi.fn(async () => ({ ...success("rotate"), nativeActionPerformed: false })),
    });

    render(<SessionHygieneControls api={api} />);
    fireEvent.click(screen.getByRole("button", { name: "Rotate conversation" }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("native action was not performed"));
    expect(screen.queryByText(/accepted by the harness/i)).not.toBeInTheDocument();
  });
});
