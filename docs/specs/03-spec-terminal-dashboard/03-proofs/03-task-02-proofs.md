# T2 Proofs: Serialized input and explicit terminal state

Status: complete
Spec: [03-spec-terminal-dashboard.md](../03-spec-terminal-dashboard.md)
Task: [03-tasks-terminal-dashboard.md](../03-tasks-terminal-dashboard.md), parent task 2.0

## Task summary

T2 makes the Phase 0C personal terminal interactive through its existing
WebSocket boundary. Input is accepted only as typed frames, serialized FIFO
per logical agent, delivered with literal tmux arguments, and acknowledged by
sequence. Resize is independently validated and typed. The server publishes
explicit `idle`, `busy`, `waiting`, and `error` state frames; the dashboard
renders the state accessibly and keeps all keyboard input inside xterm.

T3 and T4 behavior remains unimplemented: no hygiene controls, terminal-log
rotation, activity aggregation, scheduler, integrations, or prompt composer
were added.

## What this proves

- The serializer enforces one in-flight operation, bounded queue/frame limits,
  FIFO order, cancellation, stable failures, and input acknowledgements.
- The tmux boundary uses `send-keys -l -- <data>` and bounded `resize-pane`
  argument arrays; newline and terminal control sequences remain data rather
  than becoming shell syntax.
- The personal WebSocket rejects unhealthy sessions, validates input/resize
  frames, emits state transitions, returns stable errors, and never echoes
  private input into protocol responses.
- xterm owns keyboard focus and emits input/resize frames; reconnect and
  Strict Mode cleanup do not stop the native session.

## Evidence summary

- 71 harness tests and 18 server tests pass, including serializer, tmux,
  state-tracker, WebSocket input/resize, unhealthy-session, and origin tests.
- 17 dashboard tests pass, including xterm input, resize, acknowledgements,
  state labels, reconnect, unmount cleanup, and Strict Mode behavior.
- The ASP.NET-served hosted route was exercised in the in-app browser with a
  temporary providerless tmux fixture. Refresh rehydrated the terminal, typed
  input appeared in the native pane, and closing a second observer left the
  tmux session alive.

## Artifact: FIFO serializer and tmux boundary tests

**What it proves:** Interleaved input remains ordered and bounded before it
reaches the native session, and the native boundary receives literal data and
validated dimensions.

**Why it matters:** This is the safety boundary that prevents concurrent
browser keystrokes from interleaving or becoming shell-built commands.

**Files:**

- `packages/harness/Runtime/TerminalInputSerializer.cs`
- `packages/harness/Runtime/TmuxRuntime.cs`
- `tests/PersonalAssistant.Harness.Tests/Runtime/TerminalInputSerializerTests.cs`
- `tests/PersonalAssistant.Harness.Tests/Runtime/TmuxTerminalStreamTests.cs`

**Result:** 71 harness tests passed. The tests cover FIFO delivery, one
in-flight operation, queue overflow, cancellation, privacy-safe failure
messages, literal control-sequence arguments, bounded resize arguments, and
rejection of invalid dimensions.

## Artifact: WebSocket input and state integration

**What it proves:** A healthy personal session receives typed input and resize
frames, returns `inputAck`, emits `busy` and `idle`, and does not echo input
text. Unhealthy sessions and foreign origins are rejected before mutation.

**File:** `tests/PersonalAssistant.Server.Tests/TerminalApiTests.cs`

**Result:** 16 server tests passed. Providerless TestHost fixtures assert
`hello` → snapshot → idle ordering, input acknowledgement/state transitions,
literal resize command delivery, reconnect hydration, oversized snapshot
bounding, unhealthy-session rejection, and same-origin enforcement.

## Artifact: Dashboard interaction tests

**What it proves:** xterm receives terminal input and emits resize frames,
state frames are visible as text, input acknowledgements are accepted, and
socket/terminal/listener cleanup is safe during reconnect and React Strict
Mode lifecycles.

**Files:**

- `apps/dashboard/src/api/terminalApi.ts`
- `apps/dashboard/src/features/agents/TerminalSurface.tsx`
- `apps/dashboard/tests/terminalApi.test.ts`
- `apps/dashboard/tests/TerminalSurface.test.tsx`

**Result:** 17 dashboard tests passed. No separate prompt input surface was
introduced; the terminal remains the only interactive input boundary.
The terminal renderer also enables xterm line-ending conversion so tmux's
line-oriented capture/stream data starts each row at the correct column; the
renderer regression assertion covers this setting.

## Artifact: Hosted browser proof

**What it proves:** The real ASP.NET-served Vite bundle exposes the keyboard-
ready terminal, connection state, hydration state, explicit activity state,
and native output in the hosted route.

**URL:** `http://127.0.0.1:4323/agents/personal`

**Artifact path:** `/tmp/personal-assistant-t2.png`

**Result:** The in-app browser showed `Live stream`, `Hydrated from tmux`,
`State: Idle`, an active `Terminal input` textbox, and deterministic fixture
output. A typed `hi` was rendered in the native pane. Refresh created a fresh
observer and hydrated again. A second observer tab was closed and the exact
temporary tmux session remained alive afterward.

![T2 hosted keyboard-ready terminal proof](/tmp/personal-assistant-t2.png)

The fixture was an executable named `claude` under `/tmp`, with no provider
credentials and no repository writes. It was removed after the proof.

The corrective spec-review findings were addressed before commit:
resize-unavailable errors are stable, in-flight tmux operations are
cancellation-aware, state publication is serialized, and healthy reconnects
reset transient state when no input remains.

## Artifact: Quality gates

Commands run from the repository root after the T2 implementation:

```text
npm test
  PersonalAssistant.Harness.Tests: 71 passed
  PersonalAssistant.Server.Tests: 18 passed
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

The Vite build emits its non-failing advisory that the client bundle exceeds
500 kB after minification. No quality gate failed.

## Reviewer conclusion

T2 is implemented and evidenced as a bounded, serialized, literal-input
terminal interaction surface with explicit state semantics. The browser can
operate the native terminal through xterm, while observer disconnects remain
separate from agent lifecycle. T3 is the next unstarted parent task.
