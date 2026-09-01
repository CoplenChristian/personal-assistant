# Phase 0B — One Claude agent persisted in tmux

Spec version: 1
Status: proposed for review
Depends on: Phase 0A implementation commit 8e66e5c
Stack: C#/.NET ASP.NET Core backend plus React/Vite dashboard

This is the next vertical-slice spec. It is grounded in the frozen architecture,
the completed Phase 0A settings slice, the Phase 0B roadmap gate, and the
native-agent/local-trust model.

## Goal

Make one configured logical Claude agent durable across browser and harness
restarts by running the native Claude CLI in one managed tmux session.

~~~text
tracked or local agent definition
        ->
AgentRegistry loads personal
        ->
AgentSessionManager owns pa-personal
        ->
ClaudeRuntimeAdapter starts native claude
        ->
SQLite records logical agent and native session state
~~~

The user should be able to start, inspect, stop, and resume the personal agent
without creating a second agent framework or calling a model API.

## Outcome

Phase 0B is complete when:

- the configured personal agent can start a native claude process in pa-personal;
- its logical identity survives native process/session rotation;
- browser/dashboard closure does not stop the agent;
- harness restart reconciles the existing tmux session;
- startup attempts to resume the native conversation when supported;
- stop is explicit and retains definition, audit history, and durable state; and
- no 0C terminal streaming, 0D Codex, 0E dynamic-agent, scheduler, or
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
- Native agents run as the local macOS user; the harness makes no OS-sandbox claim.
- tmux arguments are passed as process argument arrays, never shell-built strings.
- Agent runtime state, memory, handoff, transcript, and session artifacts remain
  under ignored runtime paths.
- The capability broker and external integrations remain deferred.

## Scope

### In scope

- C# harness agent/session contracts;
- safe tmux command abstraction and fake executor seam;
- Claude runtime adapter boundary;
- SQLite agents and sessions migrations;
- configured-vs-active reconciliation for one known agent;
- minimal ASP.NET agent status/start/stop endpoints;
- minimal dashboard agent status/start/stop surface if needed to prove the flow;
- immutable agent lifecycle activity events; and
- providerless tests plus an opt-in local Claude/tmux smoke test.

### Explicitly deferred

Do not implement terminal streaming, xterm.js, capture-pane backlog hydration,
pipe-pane continuous streaming, clear/compact/hard rotation, checkpoint
execution, Codex, dynamic agent creation, full roster broadcasting,
scheduling, agent-to-agent messaging, skills activation, email, EventKit,
BlueBubbles, browser providers, Keychain, document indexing, memory search,
multi-user auth, or direct model APIs.

Phase 0B may expose a status/control surface, but the full terminal dashboard
belongs to Phase 0C.

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

For Phase 0B, load the reviewed repository definition for personal. A local
runtime definition may override ordinary workspace/display values only if it
passes the existing monotonic override rule. It may not widen realms, skills,
credential references, browser policy, or capability limits.

A null working_directory uses the repository root for the providerless slice
unless a validated local runtime override supplies another workspace. No
private path is written to a tracked manifest.

## Data model

Add an explicit harness-owned migration for:

~~~sql
CREATE TABLE agents (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    runtime TEXT NOT NULL CHECK (runtime IN ('claude', 'codex')),
    working_directory TEXT NOT NULL,
    realms_json TEXT NOT NULL,
    skills_json TEXT NOT NULL,
    auto_start INTEGER NOT NULL CHECK (auto_start IN (0, 1)),
    state TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE sessions (
    id TEXT PRIMARY KEY,
    agent_id TEXT NOT NULL REFERENCES agents(id),
    tmux_session_name TEXT NOT NULL UNIQUE,
    runtime TEXT NOT NULL,
    native_conversation_ref TEXT,
    state TEXT NOT NULL,
    started_at TEXT,
    last_seen_at TEXT,
    stopped_at TEXT,
    last_error TEXT
);
~~~

Native conversation references are opaque runtime metadata. The harness does
not emulate a vendor conversation protocol. Session state is local operational
state, not durable assistant memory.

Use existing activity_events for:

~~~text
agent.start
agent.stop
agent.resume
agent.exit
agent.error
session.reconciled
~~~

Activity metadata must omit credentials and private document contents.

## Agent/session contracts

### AgentRegistry

The registry loads the known personal definition and produces:

~~~text
id
name
runtime
working directory
approved realms
skills
auto-start
logical state
tmux session name
native session reference
~~~

Phase 0B needs only the configured personal agent. The registry must still
distinguish:

- configured and stopped;
- configured and running;
- configured but errored;
- configured with a missing tmux session; and
- active prefixed tmux session without a valid definition.

An unknown prefixed session is visible as unconfigured and receives no
capabilities. Full arbitrary creation and roster notifications remain 0E.

### TmuxSessionManager

Expose typed operations:

~~~text
hasSession(name)
createSession(name, workingDirectory)
sendLiteralInput(name, text)
stopSession(name)
getSessionMetadata(name)
listManagedSessions(prefix)
~~~

Each operation invokes tmux with an argument list and validates session names,
agent IDs, and working directories before execution. Session names use the
bootstrap prefix plus a validated agent ID, for example pa-personal.

Phase 0B does not use repeated whole-pane polling as a stream. Backlog and
continuous output proof belong to 0C.

### ClaudeRuntimeAdapter

Expose the native-runtime boundary:

~~~text
start(agent, session)
stop(agent, session)
restart(agent, session)
getStatus(agent, session)
tryResume(agent, session, nativeConversationRef)
recordConversationReference(agent, session, reference)
~~~

The adapter launches the installed claude command inside the already-created
tmux session. It must not call Anthropic endpoints, parse a private API, or
replace the CLI's authentication/context behavior.

If native resume is unavailable, record that result and start a new native
conversation with the agent's durable instruction files. Full checkpoint and
handoff behavior is 0C.

## Lifecycle

### Start

1. Resolve bootstrap configuration and effective settings.
2. Load and validate the personal agent definition.
3. Ensure one sessions row exists for personal.
4. Check for pa-personal.
5. Create the tmux session if absent.
6. Launch the native claude command through the runtime adapter.
7. Persist running state and emit agent.start.

Starting an already-running healthy session is idempotent and returns its
current status.

### Stop

1. Resolve the logical personal agent.
2. Stop the native process/session through the manager.
3. Persist stopped state and stopped_at.
4. Emit agent.stop.
5. Retain the logical definition, sessions row, audit history, and runtime
   memory/handoff state.

Stop does not delete files or definitions.

### Harness restart

On startup:

1. load bootstrap/default/policy configuration;
2. open SQLite;
3. load the personal definition;
4. inspect managed tmux sessions;
5. reconcile the sessions row with pa-personal;
6. mark missing/stopped/error state accurately;
7. attempt native conversation resume when a reference exists; and
8. emit session.reconciled.

A browser or dashboard restart has no effect on the tmux session. A machine
reboot may destroy tmux; the harness recreates the session on startup and
attempts native resume when possible.

## ASP.NET API

Minimal localhost routes:

~~~text
GET    /api/agents/personal
POST   /api/agents/personal/start
POST   /api/agents/personal/stop
~~~

GET returns logical agent metadata, tmux session name, state, native runtime,
last-seen/error fields, and whether a session is currently detected. It does
not return credentials, transcript contents, or private document contents.

Start/stop return the updated agent status. Invalid lifecycle transitions return
ProblemDetails with stable local error codes. There is no generic dynamic-agent
creation endpoint in 0B.

## Dashboard proof surface

A minimal agent control card may show:

~~~text
Personal
Claude Code
Configured / Stopped
tmux: pa-personal
[Start]
~~~

When running, show the detected state and [Stop]. The full terminal stream is
not part of this slice. Do not add a fake transcript or a second conversation
model.

The card must distinguish unavailable/error from stopped and must not claim
Claude is responding merely because tmux exists.

## Tests and proof

### Unit/integration tests

- manifest parsing and null-workspace fallback;
- invalid agent IDs/session names/workspaces rejected;
- only claude is launchable in 0B;
- tmux command arguments are passed literally through a fake executor;
- start creates exactly one session;
- repeated start is idempotent;
- stop retains logical state and audit history;
- harness restart reconciles an existing pa-personal session;
- missing tmux session becomes configured/stopped or configured/missing;
- unconfigured prefixed sessions cannot become capability-bearing;
- native resume success/failure is recorded without model API calls;
- agent/session rows survive a new service instance;
- activity events are emitted for lifecycle changes; and
- tracked manifests/templates/YAML remain unchanged.

### Local smoke proof

On a machine with tmux and an authenticated native claude CLI:

1. start the ASP.NET server;
2. start personal through the control surface;
3. verify pa-personal with tmux list-sessions;
4. close/reopen the dashboard and verify the session remains;
5. restart the harness and verify reconciliation;
6. stop personal and verify the definition remains; and
7. confirm no provider API dependency was added.

The smoke proof is opt-in and is not required for providerless unit/API tests.

## Definition of done

Phase 0B is ready for review when:

1. one personal logical agent starts/stops through ASP.NET and owns pa-personal;
2. session and agent state persist in SQLite;
3. harness/browser restart reconciliation works;
4. Claude remains behind a native runtime adapter;
5. tmux command construction is safe and tested;
6. lifecycle activity is recorded;
7. no terminal-streaming or later-phase work enters the diff; and
8. build, typecheck, lint, tests, privacy check, and the opt-in smoke proof
   applicable to the environment pass.

After review, freeze 0B and write the Phase 0C terminal dashboard/session
hygiene spec. Do not start 0C implementation in the 0B change set.
