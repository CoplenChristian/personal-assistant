import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { SettingsPage } from "../src/features/settings/SettingsPage";
import type { SettingsSnapshot } from "../src/api/settingsApi";

const snapshot: SettingsSnapshot = {
  contractVersion: "phase-0a-settings.v1",
  settings: [
    {
      key: "appearance.theme",
      category: "General",
      label: "Theme",
      description: "Color scheme used by the local dashboard.",
      valueType: "enum",
      options: ["system", "light", "dark"],
      value: "system",
      defaultValue: "system",
      hasOverride: false,
      source: "repo-default",
      scope: { type: "global", id: null },
      editable: true,
      resettable: true,
      requiresRestart: false,
      bootstrap: false,
      sensitive: false,
      constraints: { options: ["system", "light", "dark"] },
    },
    {
      key: "agents.defaults.autoStart",
      category: "Agents",
      label: "Auto-start new agents",
      description: "Start newly created agents automatically.",
      valueType: "boolean",
      value: false,
      defaultValue: false,
      hasOverride: false,
      source: "repo-default",
      scope: { type: "global", id: null },
      editable: true,
      resettable: true,
      requiresRestart: false,
      bootstrap: false,
      sensitive: false,
      constraints: {},
    },
    {
      key: "sessions.nativeSessionWarningBytes",
      category: "Sessions",
      label: "Native session warning",
      description: "Warn before a native session reaches this size.",
      valueType: "integer",
      value: 25 * 1024 * 1024,
      defaultValue: 25 * 1024 * 1024,
      hasOverride: false,
      source: "repo-default",
      scope: { type: "global", id: null },
      editable: true,
      resettable: true,
      requiresRestart: true,
      bootstrap: false,
      sensitive: false,
      constraints: { minimum: 1, maximum: 1024 * 1024 * 1024, unit: "bytes" },
    },
    {
      key: "sessions.nativeSessionRotateBytes",
      category: "Sessions",
      label: "Native session hard rotate",
      description: "Rotate a native session at this size.",
      valueType: "integer",
      value: 50 * 1024 * 1024,
      defaultValue: 50 * 1024 * 1024,
      hasOverride: false,
      source: "repo-default",
      scope: { type: "global", id: null },
      editable: true,
      resettable: true,
      requiresRestart: true,
      bootstrap: false,
      sensitive: false,
      constraints: { minimum: 1, maximum: 4 * 1024 * 1024 * 1024, unit: "bytes" },
    },
    {
      key: "system.serverHost",
      category: "System",
      label: "Server host",
      description: "Startup bind host for the local server.",
      valueType: "string",
      value: "127.0.0.1",
      defaultValue: "127.0.0.1",
      hasOverride: false,
      source: "environment",
      scope: { type: "global", id: null },
      editable: false,
      resettable: false,
      requiresRestart: true,
      bootstrap: true,
      sensitive: false,
      constraints: {},
    },
  ],
  safety: [
    {
      key: "safety.emailSending",
      label: "Email sending",
      state: "Disabled",
      source: "capability-policy",
      locked: true,
      reason: "No mail-send capability exists.",
    },
  ],
  integrations: [
    { id: "email", label: "Email", status: "not-configured", phase: "Phase 4" },
    { id: "calendar", label: "Calendar & Reminders", status: "not-configured", phase: "Phase 3" },
    { id: "bluebubbles", label: "BlueBubbles", status: "not-configured", phase: "Phase 5" },
    { id: "browser", label: "Browser", status: "not-configured", phase: "Phase 7" },
  ],
};

function response(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("SettingsPage", () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    document.documentElement.removeAttribute("data-theme");
  });

  it("loads metadata, tracks a dirty edit, and saves only the changed value", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === "PATCH") {
        const body = JSON.parse(String(init.body));
        expect(body.changes).toEqual([{ key: "appearance.theme", value: "dark" }]);
        return response({
          ...snapshot,
          settings: snapshot.settings.map((setting) =>
            setting.key === "appearance.theme"
              ? { ...setting, value: "dark", hasOverride: true, source: "override" }
              : setting,
          ),
        });
      }
      return response(snapshot);
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<SettingsPage />);

    expect(await screen.findByRole("heading", { name: /shape of the assistant/i })).toBeInTheDocument();
    const theme = screen.getByLabelText("Theme");
    fireEvent.change(theme, { target: { value: "dark" } });

    expect(screen.getByText("1 unsaved change")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(screen.getByText("Settings saved.")).toBeInTheDocument());
    expect(fetchMock).toHaveBeenCalledWith("/api/settings", expect.objectContaining({ method: "PATCH" }));
    expect(screen.getByDisplayValue("dark")).toBeInTheDocument();
  });

  it("resets an override through the API", async () => {
    const overridden = {
      ...snapshot,
      settings: snapshot.settings.map((setting) =>
        setting.key === "appearance.theme"
          ? { ...setting, value: "dark" as const, hasOverride: true, source: "override" }
          : setting,
      ),
    };
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === "DELETE") {
        return response(snapshot);
      }
      return response(overridden);
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<SettingsPage />);

    expect(await screen.findByDisplayValue("dark")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reset Theme to default" }));

    await waitFor(() => expect(screen.getByText("Theme restored to its default.")).toBeInTheDocument());
    expect(fetchMock).toHaveBeenCalledWith("/api/settings/appearance.theme", expect.objectContaining({ method: "DELETE" }));
    expect(screen.getByLabelText("Theme")).toHaveValue("system");
  });

  it("shows a retry surface when the API cannot be reached", async () => {
    const fetchMock = vi.fn(async () => response({ detail: "The server is offline.", code: "settings_unavailable" }, 503));
    vi.stubGlobal("fetch", fetchMock);

    render(<SettingsPage />);

    expect(await screen.findByRole("heading", { name: "Settings are temporarily out of reach." })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry connection" })).toBeInTheDocument();
    expect(screen.getByText("The server is offline.")).toBeInTheDocument();
  });

  it("keeps the draft dirty when the server rejects a save", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === "PATCH") {
        return response({
          title: "Settings request rejected",
          detail: "Use a value above the warning threshold.",
          code: "cross_setting_invalid",
        }, 400);
      }
      return response(snapshot);
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<SettingsPage />);
    expect(await screen.findByDisplayValue("system")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Theme"), { target: { value: "dark" } });
    fireEvent.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(screen.getByText("Use a value above the warning threshold.")).toBeInTheDocument());
    expect(screen.getByText("1 unsaved change")).toBeInTheDocument();
  });
});
