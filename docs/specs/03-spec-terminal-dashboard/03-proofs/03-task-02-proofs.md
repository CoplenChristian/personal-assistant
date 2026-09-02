# T2 Proofs: Serialized input, canonical screen, and explicit terminal state

Status: complete; transport amendment and UX follow-up verified
Spec: [03-spec-terminal-dashboard.md](../03-spec-terminal-dashboard.md)
Task: [03-tasks-terminal-dashboard.md](../03-tasks-terminal-dashboard.md), parent task 2.0

## Task summary

T2 makes the Phase 0C personal terminal interactive through its existing
WebSocket boundary. The standardized surface renders coalesced canonical
screen frames in a fixed-geometry plain-text viewport. Input is accepted only
as typed frames, serialized FIFO per logical agent, submitted with literal tmux
arguments, and acknowledged by sequence at the harness/tmux boundary. The
acknowledgement makes no claim that Claude received, displayed, interpreted, or
processed the input. The server publishes explicit `idle`, `busy`, `waiting`,
and `error` state frames; the dashboard renders the state accessibly.

T3 and T4 behavior remains unimplemented: no hygiene controls, terminal-log
rotation, activity aggregation, scheduler, integrations, or prompt composer
were added.

## What this proves

- The serializer enforces one in-flight operation, bounded queue/frame limits,
  FIFO order, cancellation, stable failures, and input acknowledgements.
- The tmux boundary uses `send-keys -l -- <data>` argument arrays; newline and
  terminal control sequences remain data rather than becoming shell syntax.
  There is no tmux resize operation in the terminal contract.
- The personal WebSocket rejects unhealthy sessions, validates input frames,
  rejects unsupported resize frames, emits state transitions, returns stable
  errors, and never echoes private input into protocol responses.
- The plain-text screen owns the rendered canonical view; reconnect and Strict
  Mode cleanup do not stop the native session.
- Visible input status is unnumbered and temporary; protocol sequence numbers
  remain internal correlation data.

## Evidence summary

- 73 harness tests, 17 server tests, and 16 dashboard tests pass, including
  serializer, tmux, state-tracker, WebSocket input, fixed-geometry, temporary
  input-status, unhealthy-session, and origin tests.
- The ASP.NET-served hosted route was exercised in the in-app browser against
  the rebuilt bundle. It showed the canonical screen, fixed viewport metadata,
  and no resize control; the live personal screen was not copied into the
  repository or proof image.

## Artifact: FIFO serializer and tmux boundary tests

**What it proves:** Interleaved input remains ordered and bounded before it
reaches the native session, and the native boundary receives literal data. The
terminal has no resize command or client resize frame.

**Why it matters:** This is the safety boundary that prevents concurrent
browser input from interleaving or becoming shell-built commands.

**Files:**

- `packages/harness/Runtime/TerminalInputSerializer.cs`
- `packages/harness/Runtime/TmuxRuntime.cs`
- `tests/PersonalAssistant.Harness.Tests/Runtime/TerminalInputSerializerTests.cs`
- `tests/PersonalAssistant.Harness.Tests/Runtime/TmuxTerminalStreamTests.cs`
- `tests/PersonalAssistant.Harness.Tests/Runtime/TerminalProtocolTests.cs`

**Result:** The harness test run below covers FIFO delivery, one in-flight
operation, queue overflow, cancellation, privacy-safe failure messages, and
literal control-sequence arguments. Protocol tests also prove resize frames are
rejected because geometry is fixed.

## Artifact: WebSocket input and state integration

**What it proves:** A healthy personal session receives typed input, returns a
boundary-level `inputAck`, emits `busy` and `idle`, and does not echo input
text. Unsupported resize frames are rejected without a `resize-pane` command.
Unhealthy sessions and foreign origins are rejected before mutation.

**File:** `tests/PersonalAssistant.Server.Tests/TerminalApiTests.cs`

**Result:** The providerless TestHost fixtures assert `hello` → hydration
screen → idle ordering, canonical screen replacement after a change signal,
input acknowledgement/state transitions, fixed-geometry rejection,
reconnect hydration, oversized screen bounding, unhealthy-session rejection,
and same-origin enforcement.

## Artifact: Dashboard interaction tests

**What it proves:** The canonical screen is visible as plain text, input
acknowledgements are presented as harness acceptance only, fixed geometry is
visible, and socket/listener cleanup is safe during reconnect and React Strict
Mode lifecycles.

**Files:**

- `apps/dashboard/src/api/terminalApi.ts`
- `apps/dashboard/src/features/agents/StandardizedTerminalSurface.tsx`
- `apps/dashboard/tests/terminalApi.test.ts`
- `apps/dashboard/tests/StandardizedTerminalSurface.test.tsx`

**Result:** No separate prompt input surface was introduced; the terminal
workspace remains the only interactive input boundary. The screen normalizer
converts tmux line endings, trims capture padding, and preserves long lines in
the fixed plain-text viewport instead of asking a browser terminal emulator to
reflow them. Input status copy stays unnumbered and clears after five seconds.

## Artifact: Hosted browser proof

**What it proves:** The real ASP.NET-served Vite bundle exposes the keyboard-
ready fixed-geometry canonical screen, connection state, hydration state,
explicit activity state, and standardized input acceptance wording in the
hosted route.

**URL:** `http://127.0.0.1:4323/agents/personal`

**Artifact path:** `/tmp/personal-assistant-standardized-fixed-terminal.png`

**Result:** The in-app browser showed the standardized screen, `Hydrated screen`,
`State: Idle`, and the `Fixed viewport` label. This sanitized crop records the
terminal header/metadata only; the live personal screen content was excluded.
The browser does not claim that Claude received or processed submitted input.

![T2 hosted fixed-geometry canonical screen proof](/tmp/personal-assistant-standardized-fixed-terminal.png)

The fixture was providerless and temporary, with no repository writes. It was
removed after the proof.

## Artifact: Quality gates

Commands run from the repository root after the T2 transport amendment:

```text
npm test
  PersonalAssistant.Harness.Tests: 73 passed
  PersonalAssistant.Server.Tests: 17 passed
  dashboard: 16 passed

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

## Reviewer conclusion

T2 is implemented as a bounded, serialized, literal-input interaction surface
with explicit state semantics and a fixed canonical screen transport. The
browser can submit input to the harness/tmux boundary without claiming Claude
receipt, while observer disconnects remain separate from agent lifecycle. T3
is the next unstarted parent task after this amendment is verified.
