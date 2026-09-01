# 03-spec-terminal-dashboard.md

Status: approved for implementation; T2 transport amendment recorded
Depends on: Phase 0B frozen at commit c75bd56
Stack: React/Vite dashboard, C#/.NET ASP.NET Core backend, SQLite, tmux

## Introduction/Overview

Phase 0C turns the Phase 0B lifecycle card into a real terminal workspace for
the one configured personal Claude agent. It hydrates the existing tmux pane
with `capture-pane`, uses a harness-owned pipe as a change signal for
coalesced canonical screen captures, delivers user input through a per-agent
serializer, and adds checkpoint-gated session hygiene plus the first useful
activity feed.

The harness remains the control plane and native Claude remains the runtime.
The browser connects to the harness; it does not become a second terminal
process, a prompt router, or a model API client. Closing the browser disconnects
the observation stream but does not stop or mutate the native agent session.

## Goals

- Provide one reliable `/agents/personal` terminal workspace that shows the
  actual personal tmux session, including existing scrollback on connection.
- Deliver bounded canonical screen updates through a WebSocket. `pipe-pane` is
  used only as a harness-owned change signal; each coalesced update is a fresh
  bounded screen capture, not raw VT/output streaming or timer-based polling.
- Serialize all browser input per logical agent so input arrives in order and
  never becomes a shell-built command.
- Make compact, clear, and hard-rotate actions checkpoint-gated, observable,
  and safe to retry without losing the logical agent.
- Expose immutable activity events as a recent feed and deterministic local-day
  counters, including zero-valued categories for future integrations.

## User Stories

- **As the owner of the personal agent**, I want to see the current native
  Claude terminal in the dashboard so that I can continue work without opening
  a separate terminal window.
- **As the owner of the personal agent**, I want to reconnect after a browser
  refresh and receive the current pane backlog before live output so that the
  terminal is understandable rather than starting blank.
- **As the owner of the personal agent**, I want keyboard input to be delivered
  in order so that fast typing, pasted text, and terminal control sequences do
  not interleave or corrupt one another.
- **As the owner of the personal agent**, I want clear/compact/rotate actions
  to checkpoint first so that short native sessions do not discard unresolved
  work or durable context accidentally.
- **As the owner of the harness**, I want to see activity totals and recent
  events so that I can tell what the harness has observed, delivered, blocked,
  or changed without reading a terminal transcript.

## Demoable Units of Work

### Unit 1: Terminal hydration and canonical screen updates

**Purpose:** Give the owner a live view of the existing personal Claude tmux
session with a deterministic initial screen followed by coalesced canonical
screen replacements.

**Functional Requirements:**

- The system shall add a dedicated personal-agent terminal workspace at
  `/agents/personal`; the existing overview card shall link to it without
  changing Phase 0B start/stop semantics.
- The system shall expose one same-origin WebSocket route,
  `/ws/agents/personal/terminal`, for the configured personal logical agent.
- The system shall reject a WebSocket request when the personal logical agent
  is not configured, the session is missing, or the observed session state is
  not eligible for observation; it shall return a stable error frame or HTTP
  ProblemDetails before accepting the socket.
- On a successful connection, the server shall verify the process-aware Phase
  0B session health, run tmux `capture-pane` with an explicit scrollback bound,
  and send a hydration `screen` frame before any live screen update.
- The capture bound shall use the effective session/browser scrollback setting
  and shall not read native Claude private storage.
- After hydration, the server shall use tmux `pipe-pane` or an equivalent
  harness-owned bridge only as a change signal. A short coalescing window shall
  drain a burst of signals, capture the current bounded pane once, normalize
  its line endings, and send one complete `screen` frame. Raw pipe bytes shall
  never be sent to the browser, and a timer shall not poll the complete pane.
- The stream bridge shall be scoped to the logical personal session, support
  multiple browser observers without creating multiple tmux pipes, and stop
  its pipe when the final observer disconnects. Stopping the pipe shall never
  stop or respawn the native Claude process.
- Every `screen` frame shall carry a monotonically increasing screen sequence
  and the server shall mark the hydration boundary so the client can replace
  its current screen without treating hydration as new activity.
- The client shall render the normalized screen as a fixed-geometry plain-text
  viewport. It shall preserve line boundaries, allow horizontal scrolling for
  long native lines, and report content dimensions for information only; it
  shall not fit or resize the tmux pane, interpret VT data, render a fabricated
  transcript, or maintain a second conversation history.
- A reconnect shall receive a fresh hydration screen and a new stream boundary.
  The server is not required to replay changes signaled while the browser was
  disconnected.
- A slow WebSocket client shall not cause an unbounded process-memory queue;
  the server shall apply a bounded per-observer buffer and close a client that
  cannot keep up with a stable local close/error reason.

**Proof Artifacts:**

- **C# integration tests:** fake tmux command assertions demonstrate bounded
  `capture-pane` hydration followed by one streaming bridge, not full-pane
  polling.
- **WebSocket test:** a providerless fake session demonstrates hydration before
  canonical screen updates, coalesced sequence values, reconnect hydration,
  and bounded-client failure behavior.
- **React test:** a mocked WebSocket demonstrates that canonical screen frames
  replace the prior screen, keep long lines unwrapped, and reconnect without
  duplicating or retaining old screen content.
- **Hosted browser screenshot:** the ASP.NET-served `/agents/personal` page
  shows a real-looking terminal surface populated by the deterministic fake
  session fixture, with no provider credentials or live personal transcript.

### Unit 2: Serialized input and explicit terminal state

**Purpose:** Let the owner interact with the native terminal while preserving
the per-agent ordering and state boundaries established by the architecture.

**Functional Requirements:**

- The client shall send input through the established WebSocket protocol as
  typed input frames, not as REST prompts and not as a shell command.
- The server shall pass each input item through a per-logical-agent FIFO
  serializer. Only one tmux input operation may be in flight for the personal
  agent at a time.
- The serializer shall use tmux's literal input form with an argument array;
  it shall not use an interpolated `sh -c`, model-generated shell string, or
  an unbounded `send-keys` sequence.
- Input frames shall have a maximum size, a sequence/acknowledgement contract,
  and an explicit behavior when the agent is missing, unhealthy, or the queue
  is full. Rejected input shall not be silently dropped and shall not be
  written into activity metadata.
- The terminal geometry shall be fixed by the harness/runtime boundary. The
  client protocol shall not expose a resize frame, the browser shall not send
  resize events, and no terminal action in this slice shall invoke tmux
  `resize-pane`. Reported screen dimensions are informational content
  dimensions only.
- An `inputAck` shall mean that the harness accepted the frame and its
  serialized tmux input operation completed successfully. It shall not claim
  that Claude received, displayed, interpreted, or acted on the input; the
  harness has no receipt/processing acknowledgement in this slice.
- The server shall expose an explicit terminal activity state separate from the
  Phase 0B session observed state. The supported states are `idle`, `busy`,
  `waiting`, and `error`.
- `idle` shall mean a healthy observed session with no queued/in-flight input
  and no active terminal error. `busy` shall mean input is queued/in-flight or
  recent output is associated with the current input cycle. `error` shall mean
  the stream, input serializer, tmux boundary, or native session has failed.
- `waiting` shall be an explicit deterministic status event from the runtime or
  harness state tracker; the implementation shall not claim semantic Claude
  intent by guessing from arbitrary transcript text. Until a runtime waiting
  signal exists, a healthy session may remain `idle`.
- The dashboard shall show the terminal activity state with text and an
  accessible status announcement; color alone shall not communicate it.
- Closing or refreshing the browser shall close only the WebSocket observer and
  shall not enqueue a stop, clear, rotation, or input action.

**Proof Artifacts:**

- **Serializer unit tests:** deliberately interleaved input submissions
  demonstrate FIFO ordering, one in-flight operation, size rejection, queue
  overflow reporting, and literal argument arrays.
- **State-transition tests:** fake stream/input events demonstrate all four
  supported UI states and the distinction between a healthy idle session and a
  failed session.
- **API/WebSocket proof:** invalid session and full-queue cases return stable
  errors without mutating the tmux session or activity log with private input.
- **Browser proof:** keyboard focus, fixed screen geometry, input acceptance
  wording, state labels, reconnect, and browser-close behavior are exercised
  on the hosted local dashboard.

### Unit 3: Checkpoint-gated session hygiene and terminal logs

**Purpose:** Make the first destructive or context-changing terminal controls
safe, explicit, and independently observable.

**Functional Requirements:**

- The server shall expose explicit personal-agent actions for `compact`,
  `clear`, and `rotate` under the terminal control boundary. These actions
  shall not be implemented as arbitrary user-provided shell commands.
- Each action shall call a harness-owned checkpoint coordinator before sending
  a native command, closing a native conversation, or replacing a pane.
- A checkpoint shall write only harness-owned runtime state under ignored paths,
  preserve existing human-maintained content outside generated markers, and
  record a safe checkpoint/activity result. It shall not copy private memory,
  handoffs, transcripts, or documents into Git-tracked files.
- If the checkpoint fails, the requested compact/clear/rotate operation shall
  stop before mutating the native session and shall return a stable error with a
  visible dashboard explanation. There is no silent force path in this slice.
- `compact` shall use the native runtime adapter's documented compact behavior
  or a serialized literal native control command. It shall not introduce a
  second conversation abstraction.
- `clear` shall use the native runtime adapter's documented clear behavior and
  shall preserve the logical agent/session row and audit history.
- `rotate` shall checkpoint, record the current opaque native reference when
  available, close/replace the native conversation through the adapter, and
  restore the persisted logical desired state. It shall not parse private
  Claude storage to discover a session reference.
- The dashboard shall disable hygiene controls while an action is in flight,
  show checkpoint/action progress, and make the final result retryable without
  duplicating a successful operation.
- The stream bridge shall write terminal output only to ignored runtime
  artifacts such as `runtime/agents/personal/terminal/active.log`.
- When the effective terminal-log warning size is reached, the harness shall
  emit a warning activity event and dashboard status without copying log
  content into the event.
- At the effective terminal-log rotation size, the harness shall atomically
  rotate the active log and retain only the configured number of rotated files.
  Rotation shall not be confused with native conversation memory and shall not
  delete durable logical-agent state.
- Runtime log paths, filenames, byte counts, and private content shall not be
  returned in public API metadata or activity event metadata.

**Proof Artifacts:**

- **Checkpoint/action tests:** fake checkpoint and runtime adapters demonstrate
  that failed checkpoints block every hygiene action and successful actions run
  in the required order.
- **Failure-injection test:** a failed rotate/clear demonstrates that desired
  agent identity, session records, and immutable audit history remain intact.
- **Log-rotation test:** a temporary ignored runtime directory demonstrates
  warning, atomic rotation, retention, and separation from durable memory.
- **Hosted browser screenshot:** the terminal workspace shows compact, clear,
  rotate, checkpoint, error, and retry states without exposing private runtime
  content.

### Unit 4: Activity feed and local-day counters

**Purpose:** Give the owner a trustworthy summary of harness activity without
pretending that deferred integrations have performed work.

**Functional Requirements:**

- The server shall expose a versioned activity read API, for example
  `GET /api/activity?date=YYYY-MM-DD`, backed by the immutable
  `activity_events` table.
- The API shall return the requested local calendar date, the timezone used for
  bucketing, deterministic counters, and a bounded recent-event feed ordered by
  timestamp. The default date shall be the current local day.
- Counters shall include prompts delivered; scheduled runs; queued and dropped
  scheduled prompts; email reads and modifications; messages sent, replied, and
  blocked; calendar/reminder writes; memory writes/checkpoints; document
  indexing; browser actions; blocked security actions; failures; and agent
  starts, stops, clears, rotations, and roster changes.
- Categories with no events shall be returned as zero. Future integrations
  shall not create placeholder success events merely because their Settings
  cards exist.
- The API shall preserve immutable event identity, timestamp, logical agent,
  realm, category, operation, target, status, duration, and structured metadata
  while redacting credentials, tokens, private paths, terminal data, input
  text, transcript content, and document content.
- The dashboard shall render a recent activity feed and counter grid with clear
  zero/empty states, a local-day label, and visible failure/blocked states.
- Activity refresh shall not poll the full terminal pane. Terminal WebSocket
  events and activity events shall remain separate contracts, with the UI
  allowed to refresh or subscribe to each independently.
- A browser refresh or activity refresh shall never mutate an activity event.

**Proof Artifacts:**

- **Activity aggregation tests:** seeded immutable events demonstrate exact
  local-day buckets, zero-valued categories, stable ordering, and timezone
  boundaries.
- **Privacy tests:** event serialization demonstrates that input, terminal
  output, credentials, tokens, and private paths cannot enter the feed.
- **API test:** the activity endpoint demonstrates versioned JSON and no fake
  integration events.
- **Hosted browser screenshot:** the dashboard shows the feed, counters, local
  date, zero states, blocked/failure styling, and the terminal workspace in one
  coherent control-room surface.

## Non-Goals (Out of Scope)

1. **Codex runtime support:** the shared lifecycle contract remains ready for
   Phase 0D, but this slice controls only the configured personal Claude agent.
2. **Dynamic agents and roster management:** arbitrary creation, deletion,
   roster snapshots, and `agents.changed` notifications remain Phase 0E.
3. **Scheduler and agent-to-agent messaging:** no scheduled prompt queue,
   collaboration channel, or roster notification is added here.
4. **Skills activation and routing:** no deterministic skill matcher, native
   skill projection, or second routing model is introduced.
5. **External integrations:** email, EventKit, BlueBubbles, browser providers,
   Keychain flows, document indexing, and provider OAuth remain deferred.
6. **Memory search and generated-memory authoring:** the checkpoint boundary is
   explicit, but SQLite FTS5 search, generated MEMORY.md materialization, and
   model-authored durable memory remain later work.
7. **Multi-user security:** no signup, tenant, RBAC, public OAuth, or cloud
   hosting model is added. The existing single-user local/Tailscale trust model
   remains in force.
8. **Direct model APIs:** the harness never calls Anthropic or OpenAI APIs.
9. **Arbitrary shell access:** the terminal is scoped to the configured
   personal tmux session; the API does not accept arbitrary session names,
   executables, shell commands, or output destinations.
10. **Terminal transcript as memory:** terminal logs are rotated runtime
    artifacts, not durable assistant memory.

## Design Considerations

### Workspace layout

The primary route is `/agents/personal`, linked from the existing overview.
It contains:

- a compact identity/state header using the Phase 0B status contract;
- a large canonical screen surface with a visible hydration boundary;
- connection, reconnect, and stream-error status;
- fixed terminal geometry with input submission owned by the WebSocket
  controller;
- compact, clear, rotate, and checkpoint feedback controls; and
- an activity summary/feed panel that can be collapsed on narrow screens.

The visual language remains the existing local control room: dark ink/slate,
warm amber action emphasis, teal healthy/safety signals, serif display type,
monospace operational labels, restrained motion, and atmospheric texture. The
terminal itself must prioritize legibility over decoration. Healthy, waiting,
busy, and error states require text labels and accessible live announcements,
not color alone.

The route shall remain usable at narrow responsive widths. The terminal may
scroll horizontally when a fixed-width native line cannot fit, while controls
stack into an explicit action region. Keyboard focus must be visible, the
terminal container must have an accessible name, and destructive/context
changing controls must have specific labels and status text.

### WebSocket protocol

The standardized protocol version is `phase-0c-terminal.standardized.v1`. The
server owns the connection and the native session; the client owns only its
rendered canonical screen and local connection state.

Server-to-client frames are typed JSON envelopes except for terminal payloads,
which may use a documented text/binary frame variant:

~~~json
{ "type": "hello", "protocol": "phase-0c-terminal.standardized.v1", "agentId": "personal" }
{ "type": "screen", "sequence": 0, "data": "...", "columns": 80, "rows": 24, "hydrationBoundary": true }
{ "type": "screen", "sequence": 1, "data": "...", "columns": 80, "rows": 24, "hydrationBoundary": false }
{ "type": "state", "state": "idle" }
{ "type": "inputAck", "sequence": 12 }
{ "type": "error", "code": "terminal_stream_unavailable" }
~~~

Client-to-server frames are:

~~~json
{ "type": "input", "sequence": 12, "data": "..." }
{ "type": "ping", "sequence": 13 }
~~~

The implementation shall validate protocol version, frame type, sequence,
payload size, and agent binding. It shall reject a `resize` frame as an
unsupported operation because terminal geometry is fixed. Screen `columns` and
`rows` describe the normalized content in that frame; they are informational
and are not a pane-resize contract. An `inputAck` confirms harness/tmux-boundary
acceptance only; it is not evidence that Claude received or processed the
input. The server shall not echo input text into activity metadata. A
reconnect gets a new `hello` and hydration `screen` rather than assuming the
client retained a correct buffer.

ASP.NET Core shall enable WebSockets before the endpoint that accepts the
connection, keep the request pipeline alive until the socket loop completes,
and cancel/close the socket when the request is aborted. The default browser
origin is same-origin; any explicit additional origin must be a documented
launch-time local configuration, not a public wildcard.

### Backend boundaries

Add focused C# boundaries rather than expanding the Phase 0B classes into a
single god object:

~~~text
packages/harness/Runtime/
  TmuxTerminalStream.cs       capture/pipe/literal-input boundary
  TerminalInputSerializer.cs  per-agent FIFO and backpressure
  SessionHygieneService.cs    checkpoint/compact/clear/rotate orchestration

packages/harness/Activity/
  ActivityQueryService.cs     local-day aggregation and redaction

apps/server/
  Endpoints/TerminalEndpoints.cs
  Endpoints/ActivityEndpoints.cs
  Contracts/TerminalContracts.cs
  Contracts/ActivityContracts.cs

apps/dashboard/src/features/agents/
  PersonalAgentPage.tsx
  StandardizedTerminalSurface.tsx
  ActivityPanel.tsx
  terminalProtocol.ts
~~~

The existing `TmuxSessionManager` remains the only process-argument boundary.
Every tmux invocation uses `ProcessStartInfo.ArgumentList`. `capture-pane`,
`pipe-pane`, literal input, and stream teardown receive typed arguments. The
standardized screen path uses `pipe-pane` only as a change signal and performs
one coalesced `capture-pane` after a burst; it does not forward raw pipe bytes.
If tmux's `pipe-pane` requires a shell-command sink, that sink must be a fixed
harness-owned helper and the command must be constructed by a dedicated
platform-safe utility; no model, browser, transcript, or external content may
contribute command text. Tests must assert the exact safe command shape.

The WebSocket observer owns no tmux session. Phase 0B desired/observed state
and provenance-based health remain authoritative. A missing/dead/known-wrong
session may be repaired only through the existing lifecycle service rules; a
live unknown pane is never repaired merely because a browser requested a
terminal.

### Current standards research

Research was completed on 2026-09-01 against living primary/official
documentation:

- **ASP.NET Core WebSockets:** [Microsoft Learn — WebSockets support in ASP.NET
  Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets?view=aspnetcore-10.0).
  The spec follows the documented `UseWebSockets`/`AcceptWebSocketAsync`
  pipeline, keeps the request alive for the socket loop, uses asynchronous
  receive/send operations, and configures bounded keep-alive behavior rather
  than returning before background writes finish.
- **tmux:** [tmux Advanced Use — piping and capturing pane
  output](https://github.com/tmux/tmux/wiki/Advanced-Use) and the [tmux
  `pipe-pane` implementation](https://github.com/tmux/tmux/blob/master/cmd-pipe-pane.c).
  The spec uses `capture-pane` for existing content and `pipe-pane` or a
  fixed equivalent for changes; it treats dead panes explicitly and keeps the
  stream helper harness-owned.
- **Canonical screen rendering:** the standardized surface intentionally uses
  a fixed plain-text viewport rather than xterm.js. This keeps tmux's already
  materialized screen geometry and line boundaries stable across browsers; the
  browser does not reinterpret VT sequences or send pane resize events.
- **React:** [React `useEffect` reference](https://react.dev/reference/react/useEffect)
  and [Synchronizing with Effects](https://react.dev/learn/synchronizing-with-effects).
  The terminal/WebSocket effect must return cleanup that closes the observer,
  include its reactive dependencies, and tolerate the extra setup/cleanup
  cycle used by Strict Mode.
- **Vite:** [Vite production build guidance](https://vite.dev/guide/build).
  The dashboard continues to use the committed lockfile and `npm ci` for clean
  proofs, then serves the generated static bundle from ASP.NET Core.
- **WebSocket browser behavior:** [MDN WebSocket `close` event](https://developer.mozilla.org/en-US/docs/Web/API/WebSocket/close_event).
  The client treats close as a connection-state transition, reports why the
  observer ended, and reconnects through a fresh canonical screen rather than assuming
  that a missed stream can be reconstructed locally.

### Testing and proof strategy

All tests must run without Anthropic/OpenAI credentials, personal documents,
Keychain access, or a live personal transcript. The harness shall provide fake
tmux/runtime seams that can produce deterministic pane snapshots, change
signals, canonical screen updates, input acknowledgements, state changes,
checkpoint outcomes, and failures.

Required checks:

- `npm ci --prefix apps/dashboard`;
- `dotnet build PersonalAssistant.sln`;
- `dotnet test PersonalAssistant.sln`;
- dashboard build, typecheck, lint, and Vitest suites;
- `./scripts/privacy-check.sh` before staging; and
- hosted browser proof against the ASP.NET-served React bundle, including a
  screenshot at the terminal workspace, reconnect/error states, responsive
  layout, accessible labels, and activity zero/empty states.

The opt-in smoke proof may use real tmux and an authenticated Claude CLI, but
it is not required for providerless CI/local verification. It must verify that
browser close leaves the tmux session running and must not record a private
transcript or screenshot in the repository.

## Repository Standards

- Follow the existing C# nullable, implicit-using, warnings-as-errors project
  settings and keep reusable behavior under `packages/harness`.
- Keep ASP.NET endpoint composition under `apps/server` and keep the React app
  consuming versioned server contracts rather than duplicating policy.
- Use the existing `ActivityEvent`/SQLite boundary; activity rows are
  append-only and read APIs redact sensitive metadata.
- Keep the committed dashboard `package-lock.json`; clean frontend proofs use
  `npm ci`, and `node_modules`, `dist`, .NET build output, runtime databases,
  logs, transcripts, screenshots, and personal state remain ignored.
- Existing tests use xUnit for C# and Vitest/Testing Library for React. New
  WebSocket and browser behavior needs focused providerless seams plus hosted
  browser proof rather than only unit tests.
- There is no root `AGENTS.md`. The tracked shared guidance in
  `shared/AGENTS.shared.md`, the privacy contract, frozen architecture, and
  Phase 0B spec are authoritative.
- Do not rewrite frozen architecture decisions while implementing this slice.
  Surface a concrete implementation blocker as a follow-up decision instead
  of widening 0C silently.

## Technical Considerations

- Use ASP.NET Core's built-in WebSocket support; do not add a Node server or a
  model/provider WebSocket proxy.
- Keep the WebSocket protocol versioned and same-origin by default. Use
  cancellation tokens, asynchronous socket I/O, keep-alive, bounded buffers,
  and deterministic close/error codes.
- Render normalized screen data as plain text with preserved line boundaries;
  do not add a terminal emulator, fit addon, or browser-driven pane resize to
  this slice. The input textarea and explicit WebSocket input frames remain
  separate from screen rendering.
- Hydration and canonical screen data must be treated as terminal content, not
  parsed as model instructions. External content rendered in the terminal
  remains untrusted.
- The stream bridge must be observable and disposable per logical session. It
  must not leak file descriptors, pipes, WebSockets, or background tasks after
  the final observer disconnects.
- Input serialization must define ordering, maximum frame size, bounded queue
  behavior, cancellation, acknowledgement, and error recovery before enabling
  paste or control sequences.
- Session hygiene must use an explicit adapter/coordinator boundary. The
  browser cannot request an arbitrary executable or native session reference.
- Activity aggregation must use timezone-aware date boundaries and stable
  category keys. It must query immutable rows, never infer successes from UI
  state, and never expose raw terminal or input content.
- The dashboard production bundle remains a Vite artifact served by ASP.NET
  Core. A clean proof builds it with the committed dependency lockfile and
  does not commit generated `dist` output.

## Security Considerations

- Preserve the single-user local/Tailscale trust model; this feature does not
  invent per-turn IAM, public authentication, tenants, or cloud access.
- Limit all terminal routes to the configured personal logical agent. Do not
  accept arbitrary tmux names, shell commands, executable paths, pipe sinks,
  working directories, or native conversation references from the browser.
- Pass every tmux operation through typed argument arrays. Literal input is
  still user-controlled terminal data, not a shell command; it must never be
  routed through `sh -c`.
- A live pane with unknown provenance must not be killed or respawned by a
  WebSocket connection or reconciliation triggered by the dashboard.
- The harness does not claim to sandbox a separate Claude/Codex process
  running as the same macOS user. This feature only constrains operations
  through the harness boundary.
- Credentials, tokens, Keychain references, private document contents,
  transcript data, terminal input, and raw terminal output must stay out of
  Git, prompts, activity metadata, API error details, screenshots, and logs.
- Runtime checkpoints, terminal logs, stream sinks, and generated handoffs
  belong under ignored runtime paths. The privacy check must reject staged
  instances and credential-shaped content before a public push.
- WebSocket origin handling must not default to a public wildcard. The
  browser client must use the same configured host/origin as the dashboard.
- Activity events for blocked input, failed checkpoints, stream failures,
  clear/rotate outcomes, and lifecycle changes must be append-only and must
  contain safe classifications rather than sensitive payloads.

## Success Metrics

1. **Hydration and screen updates:** 100% of providerless WebSocket integration
   tests observe hydration before a canonical screen update, and no production
   path uses timer-based whole-pane polling or forwards raw pipe bytes.
2. **Input correctness:** 100% of accepted input frames arrive at the fake tmux
   boundary in sequence, with explicit boundary acknowledgements, no Claude
   receipt claim, and zero shell-built command strings in command-capture tests.
3. **Hygiene safety:** every successful compact/clear/rotate proof records a
   successful checkpoint first; every injected checkpoint failure proves zero
   native-session mutation afterward.
4. **Activity trustworthiness:** activity counters match seeded immutable rows
   across local-day/timezone boundaries, include every required zero category,
   and never include raw input or terminal content.
5. **User-facing proof:** the hosted React route renders the terminal,
   reconnect/error states, controls, activity summary, and responsive/accessibility
   behavior with no provider credentials and no private transcript.

## Open Questions

No open questions at this time. The first implementation may refine the exact
fixed stream-helper process and the visual placement of the activity panel as
long as it preserves the protocol, privacy, lifecycle, checkpoint, and proof
requirements above.
