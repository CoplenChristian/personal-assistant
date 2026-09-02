# 04-spec-codex-adapter.md

## Introduction/Overview

This feature adds support for the reviewed work agent to run the native Codex
CLI inside the existing harness-managed tmux/session boundary. It generalizes
the current Claude-only lifecycle orchestration so Claude and Codex share the
same logical-agent, persisted-session, health, desired-state, and lifecycle
activity contracts without introducing a model API abstraction.

The feature is intentionally limited to the existing reviewed `personal` and
`work` definitions. It does not create a dynamic agent system or expose Codex
terminal streaming, prompt delivery, or external capabilities.

## Goals

- Launch the reviewed `work` agent as a native `codex` process in its own safe,
  prefixed tmux session.
- Preserve the existing personal Claude lifecycle and terminal behavior while
  moving runtime-specific behavior behind explicit adapters.
- Persist and report Codex lifecycle state through the existing SQLite agent,
  session, and immutable activity boundaries.
- Preserve explicit realm and capability-policy metadata so work never falls
  back to personal state or gains broader authority through its runtime choice.
- Prove the feature with providerless tests and a safe lifecycle API surface.

## User Stories

**As the owner of the harness**, I want to start my reviewed work Codex agent
in its own persistent tmux session so that work and personal runtime sessions
remain separate.

**As the harness**, I want Claude and Codex to use the same lifecycle contract
so that persistence, health reporting, reconciliation, and stop behavior do
not depend on which native CLI is running.

**As a maintainer**, I want Codex-specific command and resume behavior isolated
behind an adapter so that a Codex CLI change does not spread vendor assumptions
through the agent service or server routes.

**As a security reviewer**, I want the work realm and reviewed manifest to
remain authoritative so that adding Codex support does not widen capabilities,
cross account realms, or expose native session contents.

## Demoable Units of Work

### Unit 1: Reviewed work definition and runtime-neutral lifecycle boundary

**Purpose:** Make the existing reviewed work definition loadable and establish
one runtime-neutral lifecycle seam while preserving all existing personal
Claude behavior.

**Functional Requirements:**

- The harness shall load `agents/work/agent.yaml` as a reviewed definition
  with id `work`, runtime `codex`, and realm `work`.
- The registry shall validate reviewed `personal` and `work` definitions using
  the existing safe agent-id, session-name, working-directory, list, and
  metadata rules.
- This slice shall not load arbitrary agent IDs, runtime manifests, or ignored
  dynamic definitions; dynamic lifecycle remains a later Phase 0E feature.
- The lifecycle service shall resolve the runtime adapter from the validated
  definition rather than hard-coding Claude as the only supported runtime.
- Claude shall continue to use its existing native adapter, lifecycle rules,
  opaque conversation-reference validation, and personal endpoint contract.
- Shared lifecycle operations shall cover status, start, stop, reconcile,
  conversation-reference recording, resume attempt, and fresh-conversation
  launch. Runtime-specific operations shall remain adapter-owned.

**Proof Artifacts:**

- Test: registry tests demonstrate valid work loading, `codex` runtime
  preservation, `work` realm preservation, and rejection of unsafe definitions.
- Test: lifecycle tests demonstrate that existing personal Claude scenarios
  remain green after the runtime-neutral refactor.
- Test: adapter-resolution tests demonstrate that `claude` and `codex` select
  different native adapters without changing the shared session contract.

### Unit 2: Codex tmux lifecycle and persisted work session

**Purpose:** Allow the reviewed work agent to be started, adopted, reconciled,
and stopped through the same durable session model as the personal agent.

**Functional Requirements:**

- Starting the work agent shall ensure the safe prefixed tmux session and
  launch the native `codex` executable in the validated working directory.
- Every tmux invocation shall continue to use argument arrays and the existing
  `--` executable boundary; no shell-built command string may be introduced.
- Health inspection shall identify `codex` as the expected runtime for the work
  session and shall preserve the existing pane-provenance safety rules.
- A healthy existing Codex pane shall be adopted without respawning or killing
  it.
- A missing or repair-eligible dead session shall be recreated only when
  durable desired state is `running`. A stopped work agent shall not be
  resurrected during reconciliation.
- Work session rows shall persist runtime `codex`, desired state, observed
  state, timestamps, safe errors, and opaque native conversation references
  through the existing SQLite store.
- Stop shall retain the logical work agent, session record, immutable activity
  history, and runtime memory/handoff state while ending the native session.
- Codex resume behavior shall use the currently documented/native CLI command
  shape when supported. If the installed CLI cannot resume the stored opaque
  reference, the adapter shall return an explicit unavailable result and use a
  safe fresh-conversation fallback; it shall never inspect or parse Codex
  private storage.

**Proof Artifacts:**

- Test: fake tmux executor tests demonstrate exact Codex launch and health
  command arguments, expected executable checks, safe working-directory
  handling, and no `sh -c` command construction.
- Test: providerless work-agent lifecycle tests demonstrate session creation,
  healthy adoption, missing/dead reconciliation, stop retention, and desired
  state preservation.
- Test: Codex adapter tests demonstrate new-session launch, supported resume,
  unavailable-resume fallback, opaque-reference validation, and safe error
  mapping without live Codex credentials.

### Unit 3: Observable work lifecycle API and security-preserving activity

**Purpose:** Make the reviewed work lifecycle verifiable through the existing
server contract without expanding the personal terminal dashboard or the
capability system.

**Functional Requirements:**

- The server shall expose work-agent status, start, and stop operations through
  the existing agent API pattern, using validated known-agent routing such as
  `/api/agents/work`, `/api/agents/work/start`, and `/api/agents/work/stop`.
- Work responses shall preserve the existing agent status contract and report
  id `work`, runtime `codex`, the work realm through persisted activity, and
  the work tmux session name without exposing native output or credentials.
- Work lifecycle failures shall use stable ProblemDetails/error-code behavior
  consistent with the existing personal agent endpoints.
- Lifecycle activity shall remain immutable, privacy-safe, and associated with
  the validated work agent and `work` realm.
- The implementation shall not infer authorization from an agent-id prefix,
  silently fall back between realms, or alter capability-policy decisions.
- Existing `/agents/personal` terminal, hygiene, and activity behavior shall
  remain unchanged; a second Codex terminal WebSocket is not part of this
  slice.

**Proof Artifacts:**

- Test: server API tests demonstrate work status/start/stop responses, stable
  errors, and no personal-realm fallback.
- Test: activity/persistence assertions demonstrate correct work identity and
  realm with safe lifecycle metadata and no native terminal content.
- Screenshot or CLI/API trace: a providerless hosted or test-host proof shows
  the work lifecycle contract without recording credentials, paths, transcripts,
  or raw CLI output.

## Non-Goals (Out of Scope)

1. **Dynamic agents and roster management:** no arbitrary agent creation,
   ignored runtime definitions, roster snapshots, `agents.changed` events, or
   Phase 0E dashboard.
2. **A second terminal surface:** no work-agent WebSocket, terminal input,
   capture-pane stream, activity-state tracker, or browser terminal UI.
3. **Codex-specific hygiene:** no Codex compact, clear, rotate, checkpoint, or
   transcript-management commands unless a later spec defines and proves them.
4. **Model APIs and cloud integrations:** no direct OpenAI/Anthropic API calls,
   Codex SDK, Codex cloud, IDE integration, or provider abstraction.
5. **Skills, memory, scheduling, collaboration, or external integrations:**
   these remain later roadmap phases and must not be enabled by this work.
6. **Authorization redesign:** no multi-user authentication, IAM, new realm
   model, capability broker, credential store, or cross-realm access path.
7. **Private native-state inspection:** no parsing of Codex history, transcripts,
   local credentials, browser profiles, or native session databases.

## Design Considerations

No new dashboard design is required for this slice. The existing personal
terminal workspace remains the only terminal UI. The work lifecycle should be
observable through the existing agent status API and providerless tests without
making the dashboard pretend that Codex terminal streaming or prompt delivery
already exists.

If a future UI displays work status, it should reuse the existing status labels,
loading/error behavior, accessible controls, and local control-room visual
language. It must not display native Codex output, credentials, private paths,
conversation transcripts, or internal session identifiers as user-facing
authorization evidence.

## Repository Standards

- Keep reusable lifecycle and runtime code under `packages/harness`; keep
  minimal HTTP composition under `apps/server`.
- Follow the existing nullable C#, implicit-using, warnings-as-errors, records,
  exception-code, and constructor-injection conventions.
- Use the existing `AgentDefinition`, `PersistedSession`, `IAgentSessionStore`,
  `SqliteHarnessDatabase`, activity event, and `TmuxSessionManager` boundaries.
- Pass tmux arguments as arrays and preserve typed validation at the process
  boundary. Never route native runtime arguments through `sh -c`.
- Use xUnit fake-executor/providerless tests for backend behavior and preserve
  the existing server contract tests.
- Keep generated runtime state, logs, transcripts, memory, handoffs, databases,
  screenshots, and credentials outside tracked files.
- Update the relevant roadmap/status and proof artifacts only after tests and
  validation pass. Do not rewrite the completed terminal-dashboard spec to
  insert this slice.
- Use descriptive commits and run the repository privacy check before staging
  or pushing.

## Technical Considerations

- The current implementation has a Claude-specific
  `IClaudeRuntimeAdapter`, a personal-only `AgentSessionService`, a personal
  registry loader, personal-only agent routes, and personal-only terminal
  composition. The implementation should generalize only the shared lifecycle
  boundary required by this spec.
- `agents/work/agent.yaml` already supplies the reviewed Codex definition, and
  the existing SQLite schema accepts `codex` in agent and session runtime
  columns. Do not add a migration unless current schema inspection proves one
  is necessary.
- `TmuxSessionManager` already accepts a runtime executable and argument array
  for launch and an expected executable for health inspection. Reuse that seam
  for Codex rather than adding a second process runner.
- The native Codex CLI is an external, versioned executable. Current official
  Codex documentation describes launching `codex` in a local repository and
  provides a `codex resume` workflow. The adapter shall isolate the exact
  command shape, verify the installed CLI's help/version when performing an
  optional smoke check, and keep providerless tests independent of the local
  installation. See the living [Codex CLI documentation](https://developers.openai.com/codex/cli/).
- ASP.NET Core's current dependency-injection guidance favors explicit
  constructor-injected services and avoids service-locator access. Runtime
  adapter resolution should follow that pattern while fitting the repository's
  existing composition in `Program.cs`. See [ASP.NET Core dependency injection
  guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0).
- Codex and Claude may have different resume or native-control syntax. Do not
  copy Claude's `--resume` or slash-command assumptions into the Codex adapter.
  Unsupported runtime-specific operations must be explicit and fail safely.

## Security Considerations

- Only the reviewed `agents/work/agent.yaml` definition is in scope. Its
  `work` realm, skills, browser profile, memory scope, and scheduled permissions
  remain persisted metadata; this feature does not grant those capabilities.
- The work runtime must never fall back to personal realm state, account data,
  credentials, prompts, transcripts, or private document paths.
- Native Codex and tmux commands must use validated executable/argument arrays,
  fixed session-prefix rules, validated working directories, and the existing
  pane-provenance checks. Unknown live panes must not be killed or respawned.
- Conversation references are opaque identifiers only. They must be validated
  for bounded safe storage and passed only through the runtime adapter's typed
  argument boundary.
- Standard tests and proof must run without provider credentials, Keychain
  access, private documents, live transcripts, or a live native Codex session.
- Activity metadata and API errors must contain only safe classifications,
  stable error codes, lifecycle state, and approved logical identifiers. Raw
  Codex output, prompts, terminal data, credentials, and private paths must not
  be persisted or returned.
- No claim of OS-level sandboxing is added. Native CLIs remain local macOS
  processes running with the user's existing privileges.

## Success Metrics

1. **Codex launch:** a providerless fake-tmux proof verifies that the reviewed
   work definition launches native `codex` in the correct prefixed session with
   the correct working directory and no shell-built command.
2. **Shared lifecycle:** work status/start/stop/reconcile and persistence tests
   pass while the complete existing personal Claude lifecycle suite remains
   green.
3. **Safe recovery:** Codex health, resume-unavailable fallback, missing/dead
   session, stopped-intent, and unverified-pane tests demonstrate no unsafe
   resurrection or destructive repair.
4. **Realm and privacy:** all work lifecycle activity carries the work identity
   and realm, no cross-realm fallback occurs, and privacy checks find no raw
   credentials, prompts, terminal content, transcripts, or private paths.
5. **Quality gate:** .NET build/tests, dashboard tests/build/typecheck/lint,
   privacy check, and staged-file inspection pass without requiring a live
   Codex or Claude provider session.

## Open Questions

No open questions at this time. The exact Codex resume argument shape is a
version-sensitive adapter detail: implementation must use the currently
documented/installed command shape when available and must expose a safe,
explicit fresh-session fallback when it is not.
