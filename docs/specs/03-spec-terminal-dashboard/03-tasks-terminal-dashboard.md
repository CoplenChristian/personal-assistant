# 03-tasks-terminal-dashboard.md

Status: T1 complete; T2-T4 pending
Spec: [03-spec-terminal-dashboard.md](03-spec-terminal-dashboard.md)
Planning mode: planning audit passed; implementation may begin through the SDD workflow.

## Planning Context

Phase 0C is decomposed into four end-to-end parent tasks. Each parent has a
reviewable outcome and proof artifacts. Sub-tasks are ordered within each
parent so the developer can keep the repository buildable after every small
step.

Phase 0B at commit c75bd56 remains the lifecycle authority for the configured
personal agent and tmux session. These tasks do not add Codex, dynamic agents,
scheduling, skills, integrations, memory search, or multi-user access.

## Repository Standards Evidence

| Source File | Read | Standards Extracted | Conflicts |
| --- | --- | --- | --- |
| `AGENTS.md`, `../AGENTS.md`, `../../AGENTS.md` | not found | No root or parent AI instruction file was present. | none |
| `README.md` | yes | React/Vite dashboard plus ASP.NET Core/C# backend; native CLIs remain runtimes; privacy check before public pushes. | none |
| `apps/dashboard/README.md` | yes | Dashboard consumes server metadata; terminal/activity surfaces are the next deferred boundary; no provider or credential state. | none |
| `apps/server/README.md` | yes | ASP.NET host owns composition/binding/routes; terminal streaming remains in this slice; local-first operation. | none |
| `shared/AGENTS.shared.md` | yes | Native CLIs are runtimes; external content is untrusted; checkpoints precede clear/rotation; transcripts are not durable memory. | none |
| `.codex/best-practices/README.md` | yes | Repo best-practice docs are authoritative over transient agent memory. | none |
| `package.json` | yes | Root checks use .NET solution commands and dashboard-prefixed npm scripts; privacy-check is a required script. | none |
| `apps/dashboard/package.json` | yes | React/Vite build, typecheck, lint, and Vitest are the frontend quality gates. | none |
| `apps/dashboard/eslint.config.js` | yes | TypeScript ESLint is required; type-only imports are enforced. | none |
| `apps/dashboard/tsconfig.json` | yes | ES2022/browser target, bundler module resolution, React JSX, and no emitted TypeScript. | none |
| `CONTRIBUTING.md` | not found | No additional contribution policy was present. | none |
| `.github/pull_request_template.md` | not found | No pull-request template was present. | none |

## Relevant Files

| File | Why It Is Relevant |
| --- | --- |
| `packages/harness/Runtime/TmuxRuntime.cs` | Existing safe tmux argument and process-health boundary to extend with capture, pipe, resize, and literal input operations. |
| `packages/harness/Runtime/ClaudeRuntimeAdapter.cs` | Existing native Claude lifecycle adapter to extend with compact, clear, and rotate operations. |
| `packages/harness/Agents/AgentSessionService.cs` | Existing personal-agent lifecycle and desired/observed state authority. |
| `packages/harness/Persistence/SqliteHarnessDatabase.cs` | Shared SQLite connection, transaction, migration, and activity boundary. |
| `packages/harness/Activity/ActivityModels.cs` | Immutable activity event model and lifecycle event construction. |
| `packages/harness/Runtime/TerminalProtocolModels.cs` | Planned typed server/client frame and state contracts. |
| `packages/harness/Runtime/TmuxTerminalStream.cs` | Planned capture-pane/pipe-pane stream ownership and bounded observer fan-out. |
| `packages/harness/Runtime/TerminalInputSerializer.cs` | Planned per-agent FIFO input queue, acknowledgements, limits, and backpressure. |
| `packages/harness/Runtime/SessionHygieneService.cs` | Planned checkpoint-before-compact/clear/rotate orchestration. |
| `packages/harness/Activity/ActivityQueryService.cs` | Planned timezone-aware activity aggregation, category totals, and redaction. |
| `apps/server/Program.cs` | ASP.NET middleware ordering, WebSocket enablement, dashboard fallback, and dependency composition. |
| `apps/server/Endpoints/TerminalEndpoints.cs` | Planned personal terminal WebSocket and hygiene route mapping. |
| `apps/server/Endpoints/ActivityEndpoints.cs` | Planned versioned activity feed/counter route mapping. |
| `apps/server/Contracts/TerminalContracts.cs` | Planned terminal frame, state, error, and hygiene response contracts. |
| `apps/server/Contracts/ActivityContracts.cs` | Planned activity feed, counter, date, timezone, and event response contracts. |
| `tests/PersonalAssistant.Harness.Tests/Runtime/TmuxTerminalStreamTests.cs` | Planned providerless tmux capture/pipe/resize/input command and stream tests. |
| `tests/PersonalAssistant.Harness.Tests/Runtime/TerminalInputSerializerTests.cs` | Planned FIFO, queue, acknowledgement, size, cancellation, and failure tests. |
| `tests/PersonalAssistant.Harness.Tests/Runtime/SessionHygieneServiceTests.cs` | Planned checkpoint ordering, failure blocking, retry, and adapter action tests. |
| `tests/PersonalAssistant.Harness.Tests/Activity/ActivityQueryServiceTests.cs` | Planned local-day aggregation and privacy/redaction tests. |
| `tests/PersonalAssistant.Server.Tests/TerminalApiTests.cs` | Planned WebSocket/API hydration, stream, input, reconnect, and hygiene tests. |
| `tests/PersonalAssistant.Server.Tests/ActivityApiTests.cs` | Planned versioned activity API and no-fake-event tests. |
| `apps/dashboard/package.json` | Frontend dependency/scripts source; add only the pinned xterm packages required by the spec. |
| `apps/dashboard/package-lock.json` | Reproducible frontend dependency graph updated through `npm ci`/lockfile workflow. |
| `apps/dashboard/src/app/App.tsx` | Existing navigation/overview link to the personal terminal workspace. |
| `apps/dashboard/src/api/agentsApi.ts` | Existing agent API client to extend or pair with terminal/activity clients. |
| `apps/dashboard/src/api/terminalApi.ts` | Planned WebSocket URL/protocol and hygiene request client. |
| `apps/dashboard/src/api/activityApi.ts` | Planned activity feed/counter client. |
| `apps/dashboard/src/features/agents/PersonalAgentPage.tsx` | Planned `/agents/personal` workspace composition. |
| `apps/dashboard/src/features/agents/TerminalSurface.tsx` | Planned xterm.js lifecycle, hydration, output, input, resize, and reconnect behavior. |
| `apps/dashboard/src/features/agents/ActivityPanel.tsx` | Planned local-day counters, feed, zero states, and failure/blocked display. |
| `apps/dashboard/src/features/agents/terminalProtocol.ts` | Planned client frame validation, sequence tracking, and state mapping. |
| `apps/dashboard/tests/PersonalAgentPage.test.tsx` | Planned hosted-surface composition and action-state tests. |
| `apps/dashboard/tests/TerminalSurface.test.tsx` | Planned mocked WebSocket/xterm hydration, stream, input, reconnect, and cleanup tests. |
| `apps/dashboard/tests/ActivityPanel.test.tsx` | Planned counters/feed/zero-state/privacy presentation tests. |
| `apps/dashboard/src/styles.css` | Existing visual system and responsive layout extended for terminal/activity surfaces. |
| `scripts/privacy-check.sh` | Required deterministic staged/runtime privacy gate. |
| `docs/specs/03-spec-terminal-dashboard/03-spec-terminal-dashboard.md` | Normative Phase 0C requirements, protocol, security, technical, and proof source. |
| `docs/specs/03-spec-terminal-dashboard/03-audit-terminal-dashboard.md` | Required planning audit artifact created after this task list is complete. |

### Notes

- New files are planned where the current repository has no equivalent; exact
  implementation names may change only if the same boundary and proof remain.
- C# tests use xUnit and the existing fake-executor seams. React tests use
  Vitest and Testing Library. Hosted proof uses the ASP.NET-served Vite bundle.
- Clean frontend proofs use `npm ci --prefix apps/dashboard`; `node_modules`,
  `dist`, runtime state, terminal logs, and screenshots remain untracked.

## Tasks

### [x] 1.0 Terminal hydration and continuous output

#### 1.0 Proof Artifact(s)

- Test: C# tmux-boundary tests capture the exact bounded `capture-pane`
  argument vector and demonstrate that ongoing output uses one pipe/stream
  bridge rather than repeated full-pane polling.
- Test: providerless WebSocket integration tests demonstrate `hello` →
  snapshot → output ordering, monotonic sequences, reconnect hydration, and
  bounded slow-client behavior.
- Test: React/Vitest tests demonstrate xterm receives snapshot data before live
  output and replaces a disconnected stream on reconnect without duplicating
  terminal content.
- Screenshot: hosted ASP.NET dashboard at `/agents/personal` shows the
  deterministic terminal workspace, hydration boundary, connection state, and
  no private transcript or provider credential.
- Check: `dotnet test PersonalAssistant.sln`, dashboard build/typecheck/lint/
  tests, and `./scripts/privacy-check.sh` pass with no tracked runtime output.

#### 1.0 Tasks

- [x] 1.1 Define the versioned terminal protocol models for `hello`, `snapshot`,
  `output`, `state`, `inputAck`, and `error`, including sequence rules,
  payload-size limits, hydration boundary metadata, and stable error codes.
  Test artifact: C# contract tests reject unknown frame types, invalid versions,
  oversized payloads, and non-monotonic output sequences.
- [x] 1.2 Extend the tmux boundary with typed capture-pane and stream operations:
  bounded `capture-pane -p` hydration, one harness-owned pipe/stream bridge per
  logical session, observer reference counting, and deterministic stream
  teardown that never kills the Claude session. Test artifact: fake executor
  assertions prove exact argument vectors, no whole-pane polling, one shared
  pipe, and cleanup after the final observer.
- [x] 1.3 Implement bounded per-observer output buffers and monotonic stream
  sequence assignment, including slow-client close/error behavior and
  cancellation when a request disconnects. Test artifact: providerless stream
  tests prove backpressure behavior, no unbounded queue, and no leaked worker or
  pipe after cancellation.
- [x] 1.4 Add ASP.NET WebSocket middleware/endpoint composition with same-origin
  validation, personal-agent/session health checks, async socket lifetime,
  hello/snapshot ordering, output forwarding, and reconnect-specific fresh
  hydration. Test artifact: server WebSocket tests cover healthy, missing,
  unhealthy, rejected-origin, disconnect, and reconnect cases.
- [x] 1.5 Add the `/agents/personal` React route and link it from the overview;
  install/pin `@xterm/xterm` and the smallest required fit/accessibility addons,
  then implement terminal mount, snapshot write, output write, hydration
  marker, connection status, and cleanup-safe reconnect behavior. Test artifact:
  Vitest tests with mocked WebSocket/xterm prove snapshot-before-output and
  Strict Mode cleanup; hosted browser screenshot proves the visible workspace.

### [ ] 2.0 Serialized input and explicit terminal state

#### 2.0 Proof Artifact(s)

- Test: serializer tests submit interleaved input and demonstrate FIFO order,
  one in-flight tmux operation, maximum frame size, bounded queue overflow,
  acknowledgement, cancellation, and literal argument arrays.
- Test: WebSocket/API tests demonstrate invalid-session, unhealthy-session, and
  full-queue errors without mutating the native session or logging input text.
- Test: terminal state tests demonstrate `idle`, `busy`, `waiting`, and
  `error`, including the explicit waiting-signal rule and healthy-idle default.
- Test: React/Vitest tests demonstrate keyboard input, resize validation,
  acknowledgements, visible state labels, reconnect cleanup, and Strict Mode
  socket cleanup.
- Screenshot: hosted browser proof shows keyboard-ready terminal controls,
  state labels, resize behavior, and no prompt composer outside the terminal
  boundary.
- Check: the full repository quality command set and privacy check pass.

#### 2.0 Tasks

- [ ] 2.1 Implement `TerminalInputSerializer` as a per-logical-agent FIFO with
  one in-flight operation, bounded queue/frame limits, cancellation, stable
  rejection codes, and input acknowledgements. Test artifact: interleaving,
  overflow, cancellation, and failure-injection tests prove ordering and no
  silent drops.
- [ ] 2.2 Add typed literal-input and resize operations to the tmux boundary;
  use argument arrays and validated positive column/row bounds, and keep input
  separate from model/API prompt abstractions. Test artifact: fake executor
  tests prove `send-keys -l`/resize argument shape, control-sequence handling,
  and absence of `sh -c` or model-generated shell text.
- [ ] 2.3 Add server frame validation and deterministic terminal state tracking:
  healthy-idle default, busy during queued/in-flight/recent input activity,
  explicit waiting event, error on stream/input/runtime failure, and state
  frames to observers. Test artifact: state-transition and WebSocket tests
  cover all four states and invalid frame/session cases.
- [ ] 2.4 Connect xterm `onData`, resize/fitting, acknowledgement, reconnect,
  close cleanup, and accessible state announcements to the protocol. Test
  artifact: React tests prove FIFO-facing client behavior, visible labels,
  responsive controls, no stop on unmount, and Strict Mode setup/cleanup.
- [ ] 2.5 Exercise keyboard, resize, reconnect, error, and browser-close paths
  against the hosted dashboard using a deterministic fake session fixture.
  Proof artifact: browser trace/screenshot and server log show only observer
  disconnect on browser close, with no lifecycle stop request.

### [ ] 3.0 Checkpoint-gated session hygiene and terminal logs

#### 3.0 Proof Artifact(s)

- Test: fake checkpoint/runtime adapter tests demonstrate checkpoint-before-
  compact/clear/rotate ordering and prove a failed checkpoint causes zero
  native-session mutation.
- Test: failure-injection tests demonstrate logical-agent/session/audit state
  survives failed clear or rotation and successful actions are retry-safe.
- Test: temporary ignored-runtime tests demonstrate terminal-log warning,
  atomic rotation, configured retention, and separation from durable memory.
- Screenshot: hosted browser proof shows compact, clear, rotate, checkpoint,
  progress, failure, and retry states without exposing runtime paths or
  terminal contents.
- Check: C# and dashboard suites, build/typecheck/lint, privacy-check, and
  staged-file inspection pass.

#### 3.0 Tasks

- [ ] 3.1 Define the checkpoint coordinator contract and runtime-only checkpoint
  artifact format, preserving human-maintained content outside generated
  markers and emitting privacy-safe activity. Test artifact: fake checkpoint
  tests prove providerless success, failure, cancellation, ignored-path
  placement, and no tracked memory/handoff writes.
- [ ] 3.2 Extend the Claude adapter/session hygiene service with typed compact,
  clear, and rotate operations, opaque reference recording, desired-state
  preservation, retry/idempotency handling, and no arbitrary executable input.
  Test artifact: fake runtime tests prove checkpoint precedes each native
  mutation and failed checkpoint produces zero runtime action.
- [ ] 3.3 Add explicit personal-agent hygiene routes and ProblemDetails/error
  contracts; serialize one hygiene action per logical agent and append immutable
  success/blocked/failure activity without payloads. Test artifact: API tests
  cover concurrent/repeated action rejection, checkpoint failure, runtime
  failure, and retained agent/session/audit state.
- [ ] 3.4 Implement the harness-owned terminal-log writer with active-log
  location, warning threshold, atomic rotation, configured retention, bounded
  writes, and shutdown cleanup. Test artifact: temporary runtime tests prove
  warning/rotation/retention and no log contents in activity/API metadata.
- [ ] 3.5 Add dashboard compact/clear/rotate/checkpoint controls with explicit
  labels, progress, disabled-in-flight behavior, retry-safe feedback, and
  error/blocked states. Test artifact: React tests and hosted screenshot prove
  the controls never expose private paths or fabricate successful actions.

### [ ] 4.0 Activity feed and local-day counters

#### 4.0 Proof Artifact(s)

- Test: activity aggregation tests seed immutable events across local-day and
  timezone boundaries and demonstrate exact counters, stable ordering, and
  zero-valued future categories.
- Test: activity serialization/privacy tests prove input, terminal output,
  credentials, tokens, private paths, and document content are excluded.
- Test: API tests demonstrate versioned JSON, bounded recent events, blocked/
  failure statuses, and no fake integration success events.
- Test: React/Vitest tests demonstrate feed refresh, zero/empty states, local
  date labeling, failure/blocked presentation, and separation from terminal
  WebSocket updates.
- Screenshot: hosted browser proof shows the terminal workspace alongside the
  activity feed/counters with local-day and zero-state behavior.
- Check: the full repository quality command set and privacy check pass.

#### 4.0 Tasks

- [ ] 4.1 Define canonical activity category keys, redaction helpers, bounded
  feed size, local timezone/date boundary behavior, and deterministic counter
  aggregation over immutable SQLite events. Test artifact: seeded C# tests
  cover every category, zeros, midnight/timezone boundaries, stable ordering,
  and malformed metadata.
- [ ] 4.2 Add activity events for terminal hydration/stream/input/state and
  hygiene outcomes at the existing SQLite transaction boundary without storing
  raw terminal or input data. Test artifact: privacy/transaction tests prove
  safe metadata and no event on rejected/no-op refresh operations.
- [ ] 4.3 Add versioned `GET /api/activity` response contracts with date/timezone
  parameters, bounded recent events, counters, ProblemDetails, and explicit
  zero states. Test artifact: API tests cover defaults, valid/invalid dates,
  timezone conversion, event limits, redaction, blocked/failure statuses, and
  no fake integration successes.
- [ ] 4.4 Implement `ActivityPanel` and its client with local-day label,
  counters, recent feed, loading/error/empty states, independent refresh, and
  accessible failure/blocked status. Test artifact: React tests cover seeded
  data, zero categories, date labels, errors, refresh, and separation from the
  terminal WebSocket.
- [ ] 4.5 Integrate terminal, hygiene, and activity surfaces into the hosted
  `/agents/personal` route and verify the complete browser experience at
  desktop and narrow widths. Proof artifact: sanitized screenshots plus the
  full repository command output demonstrate the final acceptance gate.

## Planning Audit Handoff

The detailed sub-tasks and relevant-files table are complete. The mandatory
planning audit must now evaluate requirement-to-test traceability, proof
verifiability, repository standards, open questions, regression blind spots,
and non-goal leakage before implementation begins.
