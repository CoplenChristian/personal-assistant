import { describe, expect, it } from "vitest";

import {
  parseTerminalFrame,
  terminalWebSocketUrl,
} from "../src/api/terminalApi";

describe("terminalApi", () => {
  it("parses the versioned snapshot and output frames", () => {
    expect(parseTerminalFrame(JSON.stringify({
      type: "snapshot",
      sequence: 0,
      data: "hello\r\n",
      scrollbackLines: 5000,
      hydrationBoundary: true,
    }))).toMatchObject({ type: "snapshot", sequence: 0, hydrationBoundary: true });

    expect(parseTerminalFrame({ type: "output", sequence: 1, data: "next\r\n" }))
      .toMatchObject({ type: "output", sequence: 1, data: "next\r\n" });
  });

  it("rejects unsupported or incomplete frames", () => {
    expect(() => parseTerminalFrame({ type: "unknown" })).toThrow("unsupported frame type");
    expect(() => parseTerminalFrame({ type: "snapshot", sequence: 0, data: "" }))
      .toThrow("snapshot is invalid");
    expect(() => parseTerminalFrame("not-json")).toThrow("invalid JSON");
  });

  it("uses the page host and upgrades the browser protocol", () => {
    expect(terminalWebSocketUrl({ protocol: "http:", host: "127.0.0.1:4323" }))
      .toBe("ws://127.0.0.1:4323/ws/agents/personal/terminal");
    expect(terminalWebSocketUrl({ protocol: "https:", host: "assistant.example" }))
      .toBe("wss://assistant.example/ws/agents/personal/terminal");
  });
});
