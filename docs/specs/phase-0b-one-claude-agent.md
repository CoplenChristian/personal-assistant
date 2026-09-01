# Phase 0B — One Claude agent persisted in tmux

Spec version: 2
Status: implementation complete; review complete
Depends on: Phase 0A implementation plus the 0A.1 corrective revision
Architecture baseline: v1 at commit 13930c5
Stack: C#/.NET ASP.NET Core backend plus React/Vite dashboard

This is the next vertical-slice spec. It is grounded in the frozen architecture,
the completed settings slice, the 0A.1 review corrections, the Phase 0B
roadmap gate, and the native-agent/local-trust model.

## Goal

Make one configured logical Claude agent durable across browser and harness
restarts by running the native Claude CLI in one managed tmux session. The
harness persists user intent separately from its latest observation of the
native process.

~~~text
reviewed agent definition
        ->
AgentRegistry loads personal
        ->
desired state + observed session state in SQLite
        ->
TmuxSessionManager owns pa-personal
        ->
ClaudeRuntimeAdapter launches/adopts native claude
        ->
minimal status/start/stop surface
~~~

The user can start, inspect, stop, and resume the personal agent without a
second agent framework or a model API call.

## Outcome

Phase 0B is complete when:

- the configured personal agent can start a native `claude` process in
  `pa-personal`;
- the logical identity survives native process/session rotation;
- browser/dashboard closure does not stop the agent;
- harness restart reconciles an existing tmux session without relaunching a
  healthy process;
- a missing session is recreated only when the persisted desired state is
  `running`;
- startup attempts native resume through the runtime adapter when an opaque
  conversation reference exists;
- a dead Claude process or ordinary shell is not reported as running;
- stop is explicit and retains the definition, audit history, and durable
  runtime state; and
- no 0C terminal streaming, 0D Codex, 0E dynamic agents, scheduler, or
  integration work enters the change.

## Sources and constraints

Required source artifacts:

- README.md
- docs/architecture.md
- docs/development.md
- docs/roadmap.md
- docs/security-invariants.md
- docs/threat-model.md
- docs/privacy.md
- docs/specs/phase-0a-settings.md
- agents/personal/agent.yaml
- agents/personal/AGENTS.md
- agents/personal/MEMORY.template.md
- agents/personal/HANDOFF.template.md
- policies/defaults/runtime.yaml
- .env.example

Frozen constraints:

- Claude Code and Codex remain native CLI runtimes.
- The harness never calls Anthropic/OpenAI model APIs.
- Native agents run as the local macOS user; the harness makes no OS-sandbox
  claim.
- Every tmux invocation receives an argument array. No shell-built command
  string is used to construct a session name, working directory, executable,
  or runtime argument.
- Agent runtime state, memory, handoff, transcript, and session artifacts
  remain under ignored runtime paths.
- The capability broker and external integrations remain deferred.

## Scope

### In scope

- C# harness agent/session contracts;
- a shared SQLite database boundary with ordered embedded migrations;
- SQLite agents, sessions, and shared activity persistence;
- foreign-key enforcement on every SQLite connection;
- safe tmux command abstraction and fake executor seam;
- a dedicated native-process launch primitive, separate from 0C input
  serialization;
- Claude runtime adapter boundary with opaque resume references;
- configured personal-agent loading and validation;
- desired-vs-observed reconciliation for one known agent;
- minimal ASP.NET agent status/start/stop endpoints;
- a minimal dashboard agent control surface to prove the flow;
- immutable agent/session lifecycle activity events; and
- providerless tests plus an opt-in local Claude/tmux smoke test.

### Explicitly deferred

Do not implement terminal streaming, xterm.js, capture-pane backlog hydration,
pipe-pane continuous streaming, clear/compact/hard rotation, checkpoint
execution, Codex, dynamic agent creation, full roster broadcasting,
scheduling, agent-to-agent messaging, skills activation, email, EventKit,
BlueBubbles, browser providers, Keychain, document indexing, memory search,
multi-user auth, or direct model APIs.

Phase 0B must not expose a `sendLiteralInput`/typed-input contract; serialized
prompt delivery belongs to 0C.

## Agent definition and privacy

The reviewed personal definition remains the source for this slice:

~~~text
agents/personal/agent.yaml
agents/personal/AGENTS.md
agents/personal/MEMORY.template.md
agents/personal/HANDOFF.template.md
~~~

The live materialized files belong under ignored runtime state:

~~~text
runtime/agents/personal/agent.yaml
runtime/agents/personal/AGENTS.md
runtime/agents/personal/MEMORY.md
runtime/agents/personal/HANDOFF.md
runtime/agents/personal/transcripts/
~~~

Phase 0B loads and validates the reviewed repository definition for `personal`.
The manifest's null `working_directory` resolves to the repository root. The
registry does not write private paths into tracked files. Dynamic definitions,
runtime overrides, materialization, and roster snapshots remain 0E work.

The definition's realms, skills, browser profile, and scheduled permissions
are persisted metadata for later broker phases. 0B does not grant external
capabilities and does not widen them.

### Agent guidance decision

0B does not inject `AGENTS.md` or `SOUL.md` into a Claude prompt and does not
assume that the native Claude runtime consumes a repository instruction file.
The adapter starts Claude in the validated working directory and records the
definition identity only. Phase 1 will deliberately project the minimum
operating guidance into Claude's documented native instruction mechanism and
test that projection. No lifecycle behavior in 0B depends on guidance having
been loaded.

## State model and transitions

The two state columns have different owners and meanings:

| Record | Field | Values | Meaning |
| --- | --- | --- | --- |
| `agents` | `desired_state` | `running`, `stopped` | Durable user intent. |
| `sessions` | `observed_state` | `missing`, `starting`, `running`, `exited`, `error` | Latest harness observation of one native session. |

`auto_start` is manifest metadata, not a live state machine. On first
registration only, it establishes the initial desired state. For the reviewed
personal manifest it is `false`, so the first desired state is `stopped`.
Subsequent manifest reloads preserve the database's desired state. An explicit
POST start or stop always wins over a later `auto_start` value.

The lifecycle service applies these rules:

1. First registration inserts the logical agent and one session row. Existing
   rows update reviewed metadata but never reset `desired_state`.
2. POST start sets `desired_state = running`, then ensures the session and
   native process. A launch failure leaves desired state `running` and records
   observed `error` plus a safe `last_error`, so a later reconcile can retry.
3. POST stop attempts to stop the native process/session, then records
   `desired_state = stopped`, `exited` or `error`, `stopped_at`, and its
   lifecycle activity in one persistence transaction. It never deletes the
   agent/session row or runtime memory/handoff state.
4. On reconcile, a healthy existing Claude process is adopted as `running`
   without relaunching or resuming it. Adoption is observation, not
   resurrection.
5. If the tmux session is absent or its native process is dead and desired
   state is `running`, the service creates/repairs the session and asks the
   Claude adapter to launch. If an opaque native conversation reference exists,
   the adapter attempts resume first and falls back to a new native
   conversation when the runtime reports resume unavailable.
6. If desired state is `stopped`, reconciliation never creates, respawns, or
   resumes a missing/dead process. An externally existing healthy prefixed
   session may be observed/adopted, but an explicit stop remains the only
   harness transition that sets future desired state to stopped.
7. A tmux session containing only a shell, a dead process, or a process that
   is not the expected Claude runtime is never `running`; it is `exited`,
   `missing`, or `error` according to the observation result.

## Persistence boundary and migrations

Introduce `SqliteHarnessDatabase` as the owner of the connection, migration
runner, foreign-key setting, transaction boundary, and shared activity insert
operation. Settings, agent, and session stores consume this boundary; the
settings store does not own general database schema creation.

On every connection, execute `PRAGMA foreign_keys = ON` and verify that it is
enabled. A tiny ordered runner discovers embedded SQL resources named
`NNN_*.sql`, sorts by numeric version, creates:

~~~sql
CREATE TABLE IF NOT EXISTS schema_migrations (
    version INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    applied_at TEXT NOT NULL
);
~~~

and applies each missing migration in its own transaction before the harness
uses the database. Existing Phase 0A databases are safe to open because the
existing 001 migration remains idempotent and is recorded when first seen.
A missing, malformed, out-of-order, or failed migration fails closed.

Migration 001 owns `settings_overrides` and `activity_events`. Migration 002
owns the agent/session tables:

~~~sql
CREATE TABLE agents (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    runtime TEXT NOT NULL CHECK (runtime IN ('claude', 'codex')),
    working_directory TEXT NOT NULL,
    realms_json TEXT NOT NULL,
    skills_json TEXT NOT NULL,
    browser_profile TEXT,
    memory_scope TEXT,
    scheduled_task_permissions_json TEXT NOT NULL,
    auto_start INTEGER NOT NULL CHECK (auto_start IN (0, 1)),
    desired_state TEXT NOT NULL CHECK (desired_state IN ('running', 'stopped')),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE sessions (
    id TEXT PRIMARY KEY,
    agent_id TEXT NOT NULL REFERENCES agents(id),
    tmux_session_name TEXT NOT NULL UNIQUE,
    runtime TEXT NOT NULL CHECK (runtime IN ('claude', 'codex')),
    native_conversation_ref TEXT,
    observed_state TEXT NOT NULL CHECK (
        observed_state IN ('missing', 'starting', 'running', 'exited', 'error')
    ),
    started_at TEXT,
    last_seen_at TEXT,
    stopped_at TEXT,
    last_error TEXT,
    UNIQUE (agent_id)
);
~~~

The unique `agent_id` constraint enforces one current session row per logical
agent. Delete behavior and arbitrary agent lifecycle remain deferred to 0E;
0B never silently cascades away audit history.

Agent/session mutations and their lifecycle activity event use one SQLite
transaction. Activity metadata contains event type, logical agent ID,
transition, observed state, and safe error classification only; it never
contains credentials, native transcripts, private document content, absolute
private paths, or full command arguments.

## Contracts

### AgentRegistry

`AgentRegistry` loads the reviewed personal YAML manifest and produces a typed
definition with:

~~~text
id, display name, runtime, validated working directory,
approved realms, skills, auto_start, tmux session name
~~~

IDs are conservative session-safe tokens. The only launchable Phase 0B runtime
is `claude`; a `codex` manifest is rejected or reported unavailable until 0D.
An unknown prefixed tmux session can be listed as unconfigured, but it cannot
become a capability-bearing logical agent.

### TmuxSessionManager

Expose typed operations equivalent to:

~~~text
hasSession(name)
ensureSession(name, workingDirectory)
launchProcess(name, workingDirectory, executable, args[])
stopSession(name)
getHealth(name, expectedRuntime)
listManagedSessions(prefix)
~~~

`launchProcess` is the dedicated runtime-launch primitive. It uses tmux's
process-launch/respawn operation with one validated argument vector; it is not
implemented by `sendLiteralInput`, `send-keys`, an interpolated shell command,
or a public prompt endpoint. Input serialization and prompt delivery are 0C.

The manager validates the bootstrap prefix plus agent ID, the working
directory, the target session, and the executable/argument contract before
execution. The fake executor records the exact argument array for tests.

Health requires both a live tmux session and a foreground/process-tree
observation identifying the expected native runtime. The implementation must
not treat tmux existence alone as health. A shell, exited Claude process, or
unrecognized foreground process returns a non-running observation.

### ClaudeRuntimeAdapter

Expose the native boundary:

~~~text
start(agent, session)
startNewConversation(agent, session)
stop(agent, session)
getStatus(agent, session)
tryResume(agent, session, nativeConversationRef)
recordConversationReference(agent, session, reference)
~~~

The adapter calls `launchProcess` with the installed `claude` executable and
runtime-supported arguments. It never calls an Anthropic endpoint, parses
private Claude storage, or emulates a vendor conversation protocol.

Native conversation references are opaque strings returned by the adapter or
runtime. Resume is attempted only through the documented runtime CLI contract
(for example a supported `--resume` reference). If the runtime rejects or does
not support resume, or the process becomes unhealthy immediately after a
resume launch, the adapter records a safe resume-unavailable result and uses
`startNewConversation`. A healthy adopted process is never re-launched or
resumed during reconciliation. Conversation references enter persistence only
through the adapter/service recording path; private native storage is never
scanned.

## Harness lifecycle

### Startup/reconcile

After Phase 0A settings startup validation and before ASP.NET begins serving:

1. load and validate the personal definition;
2. upsert its reviewed metadata while preserving persisted desired state;
3. inspect `pa-personal` with process-aware health;
4. adopt a healthy Claude process without launching it;
5. if desired state is running and the process is absent/dead, repair the
   session and launch/resume through the adapter;
6. if desired state is stopped, record the observation without resurrection; and
7. persist the observed state and `session.reconciled` activity.

An unavailable local `tmux` or `claude` executable is an observed agent error,
not a reason to bypass settings validation or claim the agent is running. The
server remains available for status and retry when the lifecycle action can
report the failure safely.

### Start

1. resolve the known personal definition;
2. set desired state to `running`;
3. inspect process-aware health;
4. adopt an already healthy Claude process idempotently;
5. otherwise ensure `pa-personal`, launch/resume through the runtime adapter,
   and persist observed `starting`/`running` or `error`; and
6. emit `agent.start` or `agent.error` in the same mutation boundary.

### Stop

1. resolve the known personal definition;
2. attempt to stop the managed native process/session;
3. persist desired `stopped`, observed `exited` or `error`, and `stopped_at`
   together with `agent.stop` or `agent.error` in one SQLite transaction; and
4. retain the logical agent, session row, and audit history.

## ASP.NET API

Minimal routes:

~~~text
GET  /api/agents/personal
POST /api/agents/personal/start
POST /api/agents/personal/stop
~~~

GET returns the contract version, logical ID/name/runtime, desired state,
observed state, tmux session name, session detection/health, last-seen and
safe error information. It does not return credentials, command arguments,
transcript contents, native conversation contents, or private document data.

Start/stop return the updated status. Invalid lifecycle transitions, an
unavailable native dependency, and a failed transition use RFC 7807
ProblemDetails with stable local error codes. There is no generic dynamic-agent
creation endpoint in 0B.

## Dashboard proof surface

A minimal agent card may show:

~~~text
Personal       Claude Code       desired: stopped
Observed: not running            tmux: pa-personal
[Start]
~~~

When running, show both desired and observed state plus [Stop]. Distinguish
missing, stopped, and unavailable/error. The card must not claim Claude is
responding merely because tmux exists. No fake transcript, prompt composer,
terminal stream, or second conversation model belongs in this slice.

## Tests and proof

### Unit/integration tests

- manifest parsing, personal ID validation, and null-workspace fallback;
- invalid agent IDs/session names/workspaces rejected;
- ordered migrations are applied once and legacy 001 databases remain usable;
- foreign keys are enabled and one current session per agent is enforced;
- only Claude is launchable in 0B;
- tmux command arguments pass literally through a fake executor;
- process-aware health rejects a shell/dead/unrecognized process;
- first registration derives desired state from `auto_start`;
- explicit stop survives manifest reload and harness restart;
- start creates exactly one session and repeated start adopts/idempotently
  returns an already healthy session;
- stop retains logical state and audit history;
- restart adopts an existing healthy Claude process without relaunch/resume;
- desired-running missing/dead sessions are recreated and resume is attempted;
- desired-stopped missing/dead sessions are not resurrected;
- resume failure falls back to a new native conversation without model API calls;
- agent/session rows survive a new service instance;
- lifecycle activity events are emitted without sensitive metadata; and
- tracked manifests/templates/YAML remain unchanged.

### Browser/API proof

Against the ASP.NET-hosted dashboard, exercise GET status, Start/Stop error
handling with unavailable native dependencies, and the visible desired versus
observed state. After a failed Start, refresh the status and show persisted
`desired: running` plus an observed error rather than stale stopped UI. Confirm
dashboard closure does not own the tmux lifecycle. A real native smoke run is
not required where tmux/Claude credentials are absent.

### Local smoke proof

On a machine with tmux and an authenticated native Claude CLI:

1. start the ASP.NET server;
2. start personal through the control surface;
3. verify `pa-personal` and the foreground Claude process;
4. close/reopen the dashboard and verify the session remains;
5. restart the harness and verify adoption without a duplicate Claude process;
6. stop personal and verify desired stopped plus retained database/history; and
7. confirm no provider API dependency was added.

The smoke proof is opt-in and is not required for providerless unit/API tests.

## Definition of done

Phase 0B is ready for review when:

1. one personal logical agent starts/stops through ASP.NET and owns
   `pa-personal`;
2. desired and observed state persist independently in SQLite;
3. ordered migrations, foreign keys, and one-session-per-agent invariants are
   enforced;
4. harness/browser restart reconciliation adopts healthy processes and only
   recreates missing/dead sessions when desired state is running;
5. Claude remains behind a native runtime adapter with no guidance-injection
   assumption and no model API;
6. tmux command construction and process-aware health are safe and tested;
7. lifecycle activity is immutable and privacy-safe;
8. no terminal-streaming or later-phase work enters the diff; and
9. build, typecheck, lint, tests, privacy check, and the applicable browser
   proof pass.

After review, freeze 0B and write the Phase 0C terminal dashboard/session
hygiene spec. Do not start 0C implementation in the 0B change set.
