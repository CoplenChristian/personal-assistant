# T1 Proofs: Terminal hydration and continuous output

Status: complete
Spec: [03-spec-terminal-dashboard.md](../03-spec-terminal-dashboard.md)
Task: [03-tasks-terminal-dashboard.md](../03-tasks-terminal-dashboard.md), parent task 1.0

## Outcome

T1 delivers a personal-agent terminal observation boundary without adding
terminal input, session hygiene, activity aggregation, dynamic agents, or
provider/API model access. The backend captures a bounded tmux snapshot,
maintains one harness-owned stream bridge per logical session, applies bounded
per-observer output buffers, and exposes the same-origin WebSocket contract.
The dashboard adds `/agents/personal`, renders the Phase 0B identity/lifecycle
state, and mounts `@xterm/xterm` only when the server reports a healthy native
session.

## Acceptance evidence

### Protocol and runtime boundary

- `TerminalProtocolTests.cs` covers unknown frames, protocol validation,
  payload limits, monotonic output sequence rules, and valid client frame
  parsing.
- `TmuxTerminalStreamTests.cs` asserts the exact bounded
  `capture-pane -p -t <session>:0.0 -S -N` argument vector, one shared
  `pipe-pane` bridge, final-observer teardown, and safe sink quoting.
- `TerminalOutputHubTests.cs` proves shared monotonic sequences, bounded
  slow-observer failure with `terminal_client_slow`, and independent observer
  disposal.
- `TerminalApiTests.cs` proves the WebSocket route rejects non-WebSocket,
  unhealthy-session, and foreign-origin requests; the providerless healthy
  fixture proves `hello` → snapshot → output ordering and fresh snapshot
  hydration after reconnect.
- The stream startup test proves the tailer is ready before the tmux pipe is
  enabled, and teardown waits for the tailer before disposing its runtime
  resources. Outgoing JSON frames enforce the protocol payload bound.

### Dashboard surface

- `terminalApi.test.ts` covers versioned frame parsing, malformed-frame
  rejection, and HTTP-to-WebSocket URL selection.
- `TerminalSurface.test.tsx` covers xterm mount, hello/snapshot/output order,
  hydration, sequence rejection, reconnect replacement without buffer reuse,
  reconnect after a hydrated disconnect, Strict Mode cleanup, and socket
  cleanup on unmount.
- `PersonalAgentPage.test.tsx` covers a healthy session rendering the terminal
  surface and an unhealthy/missing session rendering the explicit not-ready
  state.
- Hosted browser proof was captured at
  `http://127.0.0.1:4323/agents/personal` from the ASP.NET-served Vite build.
  A temporary providerless tmux fixture named `claude` supplied fixed
  scrollback and heartbeat output outside the repository; it used no provider
  credential and was removed after the browser proof. The in-app browser
  showed `Live stream`, `Hydrated from tmux`, `Read-only observer`, and the
  fixture output, then showed the same hydrated state after a browser refresh.

![T1.5 hosted browser proof](/tmp/personal-assistant-t1-5.png)

### Quality gates

Commands run from the repository root:

```text
npm test
  PersonalAssistant.Harness.Tests: 58 passed
  PersonalAssistant.Server.Tests: 14 passed
  dashboard: 17 passed

npm run build
  .NET build: 0 warnings, 0 errors
  dashboard Vite build: passed

npm run typecheck
  .NET and TypeScript checks: passed

npm run lint
  ESLint: passed

npm run privacy-check
  privacy-check: passed

git diff --check
  passed
```

The Vite build reports its existing non-failing advisory that the bundled
client chunk is larger than 500 kB after minification; it does not fail the
build or change the T1 boundary.

## Manual demo

1. From the repository root, run `npm ci --prefix apps/dashboard` and
   `npm run build`.
2. Start the server with a temporary runtime directory and open
   `http://127.0.0.1:<port>/agents/personal`.
3. With no healthy agent, verify the page says “Start the personal agent to
   open its terminal” and offers a link back to lifecycle controls.
4. If a real authenticated Claude CLI is intentionally available, start the
   personal agent from the overview, open the personal-agent route, refresh,
   and verify the tmux snapshot hydrates before subsequent native output.
5. Close or refresh the browser and verify the observer disconnect does not
   stop the native tmux session. T1 does not enable keyboard input; that is T2.
