import { describe, expect, it } from "vitest";

import {
  parseTerminalFrame,
  terminalWebSocketUrl,
} from "../src/api/terminalApi";

describe("terminalApi", () => {
  it("parses the versioned canonical screen frames", () => {
    expect(parseTerminalFrame(JSON.stringify({
      type: "screen",
      sequence: 0,
      data: "hello",
      columns: 5,
      rows: 1,
      hydrationBoundary: true,
    }))).toMatchObject({ type: "screen", sequence: 0, hydrationBoundary: true });

    expect(parseTerminalFrame({ type: "screen", sequence: 1, data: "next", columns: 4, rows: 1, hydrationBoundary: false }))
      .toMatchObject({ type: "screen", sequence: 1, data: "next" });
  });

  it("rejects unsupported or incomplete frames", () => {
    expect(() => parseTerminalFrame({ type: "unknown" })).toThrow("unsupported frame type");
    expect(() => parseTerminalFrame({ type: "screen", sequence: 0, data: "" }))
      .toThrow("screen frame is invalid");
    expect(() => parseTerminalFrame("not-json")).toThrow("invalid JSON");
    expect(() => parseTerminalFrame({ type: "state", state: "working" })).toThrow("state is invalid");
    expect(() => parseTerminalFrame({ type: "inputAck", sequence: -1 })).toThrow("valid sequence");
  });

  it("uses the page host and upgrades the browser protocol", () => {
    expect(terminalWebSocketUrl({ protocol: "http:", host: "127.0.0.1:4323" }))
      .toBe("ws://127.0.0.1:4323/ws/agents/personal/terminal");
    expect(terminalWebSocketUrl({ protocol: "https:", host: "assistant.example" }))
      .toBe("wss://assistant.example/ws/agents/personal/terminal");
  });
});
