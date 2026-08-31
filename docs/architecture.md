# Architecture brief

This document is the current planning baseline for the Personal Assistant
Harness. It incorporates the configuration/settings review feedback without
implementing the runtime or external integrations.

## 1. Core principle

The harness is a deterministic local control plane around native Claude Code
and Codex processes. It owns persistence, safety, routing, schedules, memory,
configuration, and observation. It never talks directly to Anthropic or OpenAI
model APIs.

~~~text
React dashboard
      | localhost HTTP / WebSocket
      v
Local Node server ---------------- SQLite + FTS5
      |                                      |
      |- Agent registry                      |- agents, sessions, jobs
      |- Session manager                     |- activity/audit events
      |- Scheduler                           |- settings overrides
      |- Settings service                    |- memory and document index
      '- Capability broker
             | Unix-domain socket
             v
           pa CLI
             |
             |- mail
             |- EventKit calendar/reminders
             |- verified BlueBubbles messaging
             |- document vault
             |- memory
             '- controlled browser

tmux session pa-<agent-id>
      '- native claude or codex process
~~~

The broker is the authorization boundary. Skills describe how to request a
capability; they do not authorize it. The native CLI is the reasoning/runtime
boundary. The dashboard is an observation and administration surface, not a
second model conversation.

## 2. Configuration lifecycle

The planned configuration flow is:

~~~text
bootstrap environment / launch configuration
        |
        v
typed settings registry + repository defaults
        |
        v
SQLite persisted user overrides
        |
        v
effective runtime configuration
        |
        v
Settings API
        |
        v
React Settings page
~~~

Hard security invariants constrain every layer. Bootstrap configuration is a
parallel startup boundary, not an ordinary database setting:

- PA_RUNTIME_DIR determines where SQLite and runtime artifacts live.
- PA_SERVER_HOST and PA_SERVER_PORT determine the local server bind.
- The tmux session prefix is fixed/bootstrapped once sessions exist.
- PA_VAULT_DIR may provide the initial document-vault default, but the vault
  preference may later have a user override.

The runtime database must not contain an override for PA_RUNTIME_DIR, because
that would require reading the database before knowing where it is. Bootstrap
fields shown in the UI are read-only and explain that startup configuration
and a restart are required.

### 2.1 Typed registry

The settings service has one canonical TypeScript registry. Each definition
contains at least:

~~~text
key
category
label
description
value type and enum options
safe default/effective-default resolver
scope
editable
requiresRestart
sensitive
validation constraints
~~~

Version-controlled defaults remain in policies/defaults/runtime.yaml where
they already exist. The registry maps those YAML values into typed settings
rather than copying all defaults into SQLite. Defaults that need local
resolution, such as the system timezone or document-vault path, resolve at
startup from the environment/system context.

The initial registry is intentionally limited:

| Category | Settings |
| --- | --- |
| Appearance | theme; terminal scrollback/history shown in the browser |
| Agent defaults | default runtime; default auto-start for newly created agents |
| Sessions | tmux history; terminal-log warning size; rotated log count; native-session warning size; hard-rotate size; archive TTL |
| Documents | vault path; automatic indexing; automatic TOC regeneration |
| Memory | maximum FTS5 results; automatic generated-memory materialization |
| Automation | local timezone; missed-run policy (skip or run-once); maximum queued prompts per agent |
| System | runtime directory, server host, server port, tmux prefix, all read-only |
| Safety | locked posture derived from policy/default models, all read-only |
| Integrations | status cards only until the corresponding phase exists |

Changing an agent default affects future agent creation only. It does not
rewrite existing agent manifests or sessions. Existing agent-specific
configuration belongs on a future agent detail/edit surface.

### 2.2 Scopes

The storage model supports these scopes:

~~~text
global
realm
agent
integration
~~~

The first implementation uses global settings only. The schema should still
include scope_type and scope_id so later realm, agent, and integration
overrides do not require replacing the store. This is a local personal
assistant, not a multi-user identity/auth system.

Only overrides are persisted. A reset deletes the override and returns the
effective value to its repository/system or environment-derived default.

### 2.3 Settings API

The planned localhost API is:

~~~text
GET    /api/settings
PATCH  /api/settings
DELETE /api/settings/:key
~~~

GET returns effective values and rendering metadata, including:

- current effective value
- default value
- whether an override exists
- source (override, repo-default, environment, or policy)
- category and descriptive metadata
- editable
- requiresRestart
- scope
- validation constraints

PATCH must reject unknown keys, immutable keys, bootstrap keys, sensitive
values, and invalid values on the server. A valid request persists only an
override, returns the new effective snapshot, and emits a settings.updated
activity event. DELETE removes an override; it cannot weaken a locked policy
or create an invalid cross-setting state.

The settings store must never be a generic secret store. Provider credentials,
tokens, and credential references belong to macOS Keychain/protected
integration state.

### 2.4 Cross-setting validation

Settings validation is evaluated against the candidate effective snapshot,
not one field in isolation. In particular:

- warning sizes and hard-rotate sizes must be positive;
- a hard-rotate threshold must be greater than its warning threshold;
- history and queue limits must remain within reasonable bounds;
- archive TTL must remain within reasonable bounds;
- enum settings must reject values outside their declared options.

Checkpoint-before-clear/rotation is not a setting that can be disabled. It is a
hard safety invariant.

## 3. State ownership

- Git contains code, instructions, skill definitions, schemas, and safe policy
  defaults.
- SQLite/runtime state contains settings overrides, sessions, schedules, live
  roster data, verified contacts, account records, audit events, searchable
  memory, and document indexes.
- macOS Keychain contains provider credentials and tokens.
- A separate local document vault contains personal documents and is indexed
  without being committed.
- SOUL.md, AGENTS.md, and generated MEMORY.md provide compact native-CLI
  context; terminal logs are not durable assistant memory.
- Mutable allowlists and verified-contact records are protected runtime state,
  not skill text or Git-tracked defaults.

## 4. Agent and session model

An agent is a durable logical object defined by agents/*/agent.yaml. Each
active agent owns one pa-<id> tmux session. A native conversation may rotate
without deleting the logical agent, its realm, skills, schedule references,
settings scope, or durable memory.

The runtime adapter contract eventually covers:

~~~text
start, stop, restart, sendPrompt, captureOutput, getStatus,
compact, clear, rotateSession, resumeSession, measureSessionDiskUsage
~~~

tmux survives browser/UI and harness restarts, but not a machine reboot. On
startup, the harness reconciles configured agents with live prefixed tmux
sessions and attempts native conversation resume. If resume is unavailable, it
starts a fresh native conversation with the latest durable handoff and memory.

Clear and hard rotation must checkpoint first:

1. trigger the memory checkpoint hook;
2. write durable facts to SQLite/materialized memory;
3. write unresolved work to HANDOFF.md or equivalent state;
4. record the current native session ID;
5. close the current conversation;
6. start/resume a fresh native conversation with compact context.

## 5. Terminal stream and input model

The dashboard should render the actual native CLI stream.

- Use tmux capture-pane for initial/backlog hydration where appropriate.
- Prefer tmux pipe-pane or an equivalent streaming mechanism for ongoing output
  rather than repeatedly polling and diffing the entire pane.
- Rotate terminal logs independently; logs are not durable assistant memory.
- Serialize injected input per agent.
- Never construct shell commands from model-generated text; use argument arrays.
- Track explicit states such as idle, busy, waiting, and error before scheduler
  or agent-to-agent injection becomes active.

The dashboard can use xterm.js for rendering and WebSockets for transport while
the native CLI remains the source of the terminal conversation.

## 6. Capability broker and realms

Every broker request carries:

~~~text
agent_id
realm
capability
operation
parameters
request_id
~~~

The broker checks the request against policy and fails closed when
authorization is uncertain.

Realm enforcement must use explicit persisted account metadata, not account-ID
string prefixes as the security boundary. A future account record is:

~~~text
account_id
provider
display_name
realm
credential_ref
enabled
~~~

The prefix conventions in policies/defaults/realm-policy.yaml may remain useful
as naming defaults and migration hints, but account authorization must use the
stored realm field. Never silently fall back from a work account to a personal
account or the reverse.

## 7. Scheduler and scheduled capabilities

Scheduled jobs target logical existing agents, not native conversation IDs and
not fresh one-shot agents. An agent's scheduled permissions are an upper
bound, not an automatic grant.

Every scheduled job must explicitly declare the subset of capabilities it may
use. The broker checks:

~~~text
job capability subset
        subset of
agent scheduled-permission upper bound
        subset of
global hard policy
~~~

A scheduled prompt is queued for the existing logical agent. It does not
automatically inherit every write permission available to that agent. Each
agent receives only one injected prompt at a time, subject to its queue limit.

message.reply must require a concrete verified inbound message reference.
A future proactive notification capability must target an already verified,
configured contact; it must not abuse message.reply as a general notification
primitive.

## 8. Agent-to-agent messaging

Keep collaboration intentionally simple. Persist an audit record first, then
inject a labeled message into the destination tmux session. Use argument-array
tmux calls, literal send-keys behavior, a fixed short paste delay, and
per-destination serialization.

Agent-to-agent messaging cannot bypass the recipient's realm, capabilities,
or broker policy. The receiving agent uses the agents skill if it needs to
respond.

## 9. Safety and integrations

Safety settings are a read-only view derived from actual policy/default state.
The UI may show:

~~~text
Email sending                 Disabled / Locked
Unverified recipients         Blocked / Locked
Group messaging               Disabled / Locked
Cross-realm fallback          Denied / Locked
Consequential audit           Required / Locked
Checkpoint before rotation    Required / Locked
~~~

The Settings page may show honest integration cards, but it must not invent
connected accounts or credentials:

- Email — not configured / Phase 4
- Calendar & Reminders — not configured / Phase 3
- BlueBubbles — not configured / Phase 5
- Browser — not configured / Phase 7

External integrations remain deferred. This settings work does not implement
Gmail, BlueBubbles, EventKit, browser automation, messaging, or scheduling.

## 10. Technology choices

| Area | Choice |
| --- | --- |
| Host | macOS |
| Language | TypeScript / Node.js |
| UI | React + Vite |
| Terminal | xterm.js |
| Transport | localhost HTTP + WebSocket; Unix socket for broker |
| Persistence | SQLite + FTS5 |
| Agent persistence | tmux |
| Apple integration | Swift/EventKit helper in a later phase |
| Messaging | BlueBubbles adapter in a later phase |
| Secrets | macOS Keychain |
| Startup | launchd for the harness only |
| Model runtime | native Claude Code and Codex CLIs |
