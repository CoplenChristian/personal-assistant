# Vertical-slice roadmap

The build remains staged so each phase produces a usable, testable boundary.
Configuration/settings is the first slice so later runtime code does not
scatter environment reads, defaults, validation, privacy rules, or activity
handling across modules.

## Phase 0A — Settings and configuration

Outcome: a local Settings route and backend configuration service can load
effective values, persist safe user overrides, reset them, and explain locked
or bootstrap-only values.

Scope:

- typed settings registry shared by backend validation and UI metadata;
- basic capability-policy/configuration contracts needed by later integrations;
- repository defaults loaded from policies/defaults/runtime.yaml;
- bootstrap resolution for PA_RUNTIME_DIR, PA_SERVER_HOST, PA_SERVER_PORT, and
  the fixed tmux prefix;
- SQLite override storage with future global, realm, agent, and integration
  scopes;
- localhost GET/PATCH/DELETE Settings API;
- real Settings route with loading, error, dirty, save, reset, and restart
  states;
- read-only safety posture and honest not-configured integration cards; and
- privacy-safe tracked templates, ignored runtime paths, and the planned
  privacy check.

Acceptance gate:

- [ ] Typed registry is the single metadata source for backend validation and UI rendering.
- [ ] Repository defaults remain in Git-tracked YAML and are never rewritten by Settings.
- [ ] PA_RUNTIME_DIR, PA_SERVER_HOST, and PA_SERVER_PORT are bootstrap/read-only.
- [ ] tmux prefix cannot be changed in a way that orphans active sessions.
- [ ] SQLite persists overrides only, with future global/realm/agent/integration scopes.
- [ ] GET returns effective value, default, override state, source, editability, and restart metadata.
- [ ] PATCH rejects unknown, invalid, immutable, bootstrap, and sensitive settings.
- [ ] DELETE removes an override and returns to the effective default.
- [ ] Cross-field session validation prevents hard rotation below warning.
- [ ] settings.updated is emitted at the activity/audit boundary.
- [ ] Safety posture is visible but cannot be weakened.
- [ ] Integration cards are honest not-configured/phase states, not fake connections.
- [ ] Settings route has loading, error, dirty, save, reset, and restart-required states.
- [ ] Frontend controls are accessible and keyboard usable.
- [ ] Tracked files use safe templates; generated personal state is ignored.
- [ ] npm run privacy-check passes before public pushes.
- [ ] Tests run without Anthropic/OpenAI credentials.

## Phase 0B — One Claude agent persisted in tmux

Outcome: one configured Claude agent can be started, stopped, and resumed as a
logical agent with state persisted independently of the browser.

Acceptance gate:

- [ ] A configured personal agent launches the native claude CLI inside pa-personal.
- [ ] The logical agent and native session records persist in SQLite.
- [ ] Closing the dashboard does not kill the agent.
- [ ] Restarting the harness reconciles and resumes the session when possible.
- [ ] No model API integration exists.

## Phase 0C — Terminal dashboard, activity, and session hygiene

Outcome: the dashboard can show the real native terminal, deliver serialized
user input, expose compact/clear/rotate controls, and summarize harness
activity.

Scope:

- capture-pane for initial/backlog hydration;
- pipe-pane or another streaming mechanism for ongoing output;
- xterm.js/WebSocket terminal rendering;
- explicit idle, busy, waiting, and error states;
- checkpoint-before-clear/rotation flow;
- immutable activity feed and local-day counters.

Initial activity counters/feed:

- prompts delivered;
- scheduled runs and queued/dropped scheduled prompts;
- email reads and modifications;
- messages sent, replied, and blocked;
- calendar/reminder writes;
- memory writes/checkpoints;
- document indexing;
- browser actions;
- blocked security actions;
- failures; and
- agent starts, stops, clears, rotations, and roster changes.

Acceptance gate:

- [ ] Initial terminal backlog is hydrated with capture-pane.
- [ ] Ongoing terminal output uses a streaming mechanism rather than full-pane polling/diffing.
- [ ] Input injection is serialized per agent.
- [ ] Dashboard exposes idle, busy, waiting, and error states.
- [ ] Clear and hard rotation checkpoint before closing context.
- [ ] Activity events are immutable and visible in a feed/counter surface.
- [ ] Terminal logs remain separate from durable memory.

## Phase 0D — Codex adapter

Outcome: the same logical/session contracts support a native Codex agent
without changing the harness or adding a model API abstraction.

Acceptance gate:

- [ ] A configured work agent launches the native codex CLI in its own prefixed tmux session.
- [ ] Claude and Codex use the same lifecycle contract.
- [ ] Runtime-specific behavior remains behind adapters.
- [ ] Cross-realm and capability policy behavior is unchanged.

## Phase 0E — Dynamic agents and roster

Outcome: the dashboard can create arbitrary configured agents, reconcile them
with live tmux state, and keep active agents aware of roster changes.

Scope:

- AgentRegistry combining reviewed repository definitions, ignored runtime
  definitions/overrides, and actual pa-* sessions;
- explicit create, start, stop, and delete lifecycle behavior;
- configured-vs-active status;
- atomic runtime/roster.json snapshot;
- roster hash and agents.changed dashboard broadcast;
- lightweight roster-change notification to active agents; and
- no capabilities for unconfigured or policy-invalid sessions.

Acceptance gate:

- [ ] Dashboard creation validates ID, runtime, workspace, realm, and skills.
- [ ] Dynamic private definitions remain in ignored runtime state by default.
- [ ] Active sessions are discovered from actual prefixed tmux state.
- [ ] Stopped agents retain definitions, audit history, and durable state.
- [ ] Delete requires explicit human action and preserves immutable audit records.
- [ ] Registry changes update SQLite and atomically replace runtime/roster.json.
- [ ] agents.changed reaches the dashboard and active agents.
- [ ] Existing agents receive a lightweight notice rather than a full config dump.
- [ ] An unconfigured tmux session cannot access capabilities.
- [ ] Runtime overrides cannot widen reviewed realms, skills, credentials, browser allowlists, or capability limits without explicit human approval and audit.

## Phase 1 — Skills, SOUL, and durable memory

Outcome: agents behave consistently, load relevant procedures, and retain
grounded durable information without giant sessions.

Scope:

- vendored, reviewed OpenClaw SOUL.md with recorded source/version/commit,
  vendoring date, and local edits;
- AGENTS.md operating rules kept separate from persona;
- one canonical skills catalog;
- dashboard, iMessage, scheduler, and agent-message ingress through
  normalization, deterministic trigger matching, eligible-skill filtering,
  context injection, and native Claude/Codex skill discovery;
- SQLite FTS5 memory and generated MEMORY.md materialization; and
- checkpoint-before-clear behavior.

Acceptance gate:

- [ ] SOUL provenance is recorded and the vendored text is reviewed.
- [ ] Operational authorization rules remain outside SOUL.md.
- [ ] Canonical email, messaging, reminders, documents, memory, and agents skills exist.
- [ ] No routing LLM is introduced.
- [ ] Native skill layouts are adapters/generated views of the canonical catalog.
- [ ] Memory search uses SQLite FTS5.
- [ ] Generated memory and handoffs remain under ignored runtime paths.
- [ ] Clear checkpoints before closing a native conversation.
- [ ] A new native conversation restores durable context.

## Phase 2 — Searchable personal document vault

- [ ] Dropping a PDF into the external vault updates the database.
- [ ] TOC.md updates automatically when enabled.
- [ ] Search returns source/page provenance.
- [ ] Updating a file invalidates stale derived memory.
- [ ] No personal document is committed to this repository.

## Integration readiness

Before each external integration is enabled, verify normal credential hygiene,
narrow provider APIs, explicit account realms, deterministic capability
restrictions, blocked-action tests, and activity coverage. This is a review of
harness-managed operations, not a requirement to prove that every unrelated
path available to the same macOS user has been closed.

The native-agent trust model and its non-guarantees are documented in
[threat-model.md](threat-model.md). Stronger process isolation or separate
macOS accounts remain optional Phase 8 hardening.

## Phase 3 — iCloud actions

EventKit helper, approved targets, reminder/calendar skills, and audit events.
No implementation is implied by Phase 0/1 settings cards.

## Phase 4 — Safe email organization

Account records with explicit persisted realms, Gmail first, read/modify only,
and no send API. Account-ID prefixes remain naming defaults or migration hints,
not the authorization boundary.

## Phase 5 — Safe iMessage channel

BlueBubbles webhook, verified contacts, concrete inbound references for
message.reply, separate verified-contact proactive notifications, rate limits,
and blocked-action audit.

## Phase 6 — Persistent automation and collaboration

Existing-context scheduler, explicit per-job capability subsets, prompt queues,
tmux agent messaging, and roster metadata.

## Phase 7 — Controlled browser agents

BrowserProvider abstraction, agent-browser baseline, native adapters, realm
profiles, domain allowlists, and visible browser activity.

## Phase 8 — Continuous-operation hardening

Backups, pruning, failure recovery, dashboard auth, restore, security tests,
and optional stronger OS isolation.

Every later phase must add focused success and fail-closed security tests before
it begins. A Settings card, skill placeholder, or provider status must not be
presented as a working integration.
