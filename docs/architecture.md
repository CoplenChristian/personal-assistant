# Architecture brief

This document is the current planning baseline for the Personal Assistant
Harness. It incorporates the configuration, privacy, lifecycle, and review
feedback without implementing the runtime or external integrations.

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

The broker is the hard authorization boundary for integrations only when the
native agents cannot access integration credentials or equivalent alternate
execution paths. Skills describe how to request a capability; they do not
authorize it. The native CLI is the reasoning/runtime boundary. The dashboard
is an observation and administration surface, not a second model conversation.
See docs/threat-model.md for the required trust and containment conditions.

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
- Tracked agent memory and handoff files are templates only. Instantiated
  memory, handoffs, local agent overrides, transcripts, browser profiles, mail
  caches, screenshots, and downloads live under ignored runtime paths.
- The privacy layout and planned repository check are defined in
  docs/privacy.md. A public source repository is never treated as a safe place
  for personal state merely because its visibility is convenient.

## 4. Agent and session model

An agent is a durable logical object defined by agents/*/agent.yaml. Each
active agent owns one pa-<id> tmux session. A native conversation may rotate
without deleting the logical agent, its realm, skills, schedule references,
settings scope, or durable memory.

The AgentRegistry is the single roster authority. It reconciles:

~~~text
version-controlled reviewed definitions
  + ignored runtime definitions and local overrides
  + actual pa-* tmux sessions
  -> configured-vs-active AgentRegistry state
~~~

Only prefixed tmux sessions are considered harness sessions. An active session
without a valid agent definition is visible as an unconfigured/blocked state;
it does not receive capabilities. A configured agent can be stopped without
deleting its definition or runtime memory.

Agent lifecycle rules:

- Create is an explicit dashboard action that validates an ID, runtime, realm,
  and workspace before writing a local definition and optional runtime state.
- Start creates or resumes the prefixed tmux session only after configuration
  exists and policy validation succeeds.
- Stop ends the session but retains the logical agent, audit history, and
  durable state.
- Delete is an explicit human action. It stops the session first and archives
  or removes local runtime state without deleting immutable audit records.
- Promoting a private runtime definition into Git requires an explicit human
  review; dynamic creation must not silently publish local paths or context.

On startup and on a short reconciliation interval, the registry computes a
roster hash. When configured or active state changes, it updates SQLite,
atomically writes runtime/roster.json, broadcasts agents.changed to the
dashboard, and sends a lightweight roster-change notice to active agents.
The notice does not dump all configuration into every prompt.

### 4.1 Agent override precedence

When the same agent ID appears in more than one source, the effective
definition is resolved in this order:

~~~text
reviewed repository definition
        -> ignored runtime definition for a private/dynamic agent
        -> ignored local runtime override
        -> live tmux/session status
~~~

Runtime customization may change ordinary properties such as display name,
workspace, or presentation preferences when the value passes validation. It
must be monotonic for security-sensitive properties: a local override may
narrow an approved realm, skill set, browser allowlist, or capability limit,
but may not widen them. It may not change work to personal+work, add a
sensitive skill, add a credential reference, or increase a capability limit
without an explicit human/admin approval path and audit event.

The registry validates the merged result before activation. A runtime
definition or override does not become trusted merely because it has the same
agent ID as a reviewed repository definition.

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

### 6.1 Trusted turn authorization context

Every turn that can lead to a capability request receives a server-issued
authorization context:

~~~text
turn_context_id
source
initiated_by
agent_id
session_id
realm
allowed_capabilities
source_reference
expires_at
~~~

Source references may include a scheduled job ID, dashboard request ID,
verified sender contact ID, or verified inbound message ID. The broker
resolves the context from trusted harness state and verifies an opaque
context credential bound to the agent and native session. It does not trust
model-supplied source, initiator, realm, or allowed-capability fields.

The effective authorization is:

~~~text
hard global policy
    intersect
agent capability upper bound
    intersect
turn authorization context
    intersect
scheduled-job capability subset, when applicable
~~~

Examples include dashboard_user with the capabilities derived from the
explicit user request, scheduled_job with the named job subset,
verified_imessage with only a concrete verified inbound reply reference, and
email_content/browser_content with no capability authority. An agent-message
may carry task context but cannot grant new external capability authority.

Contexts expire, are bound to their source/agent/session, and are included in
activity records. External content can provide data but can never widen a
turn's authorization context.

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

## 10. Canonical skill activation

The repository has one canonical skill catalog under skills/. Its metadata and
procedures are portable source material; skills never contain credentials or
authorization decisions.

Every ingress path follows the same planned pipeline:

~~~text
dashboard / iMessage / scheduler / agent-message ingress
        -> normalize source, agent, and realm
        -> deterministic keyword/rule trigger matcher
        -> filter to skills eligible for that agent
        -> inject skill/context notice into the native session
        -> native Claude/Codex skill discovery and execution
~~~

No second LLM is introduced solely to route a prompt. If Claude Code and
Codex need different native directories, adapters, symlinks, or generated
views may project the canonical catalog into those layouts. The repository
catalog remains the source of truth. External content can trigger a skill
match but can never grant a capability.

## 11. Dashboard activity model

The immutable activity_events model is also the dashboard's initial activity
source. The dashboard should expose both a recent feed and counters for the
selected local day/timezone, with zero/empty states when no event exists:

- prompts delivered to agents;
- scheduled runs and queued/dropped scheduled prompts;
- email reads and modifications;
- messages sent, replied, and blocked;
- calendar and reminder writes;
- memory writes/checkpoints;
- document indexing events;
- browser actions;
- blocked security actions;
- failures; and
- agent starts, stops, clears, rotations, and roster changes.

Each event carries timestamp, agent, realm, category, operation, target,
status, duration, structured metadata, turn_context_id, source, and source
reference when applicable. A provider card being unconfigured must not create
a fake successful activity event.

## 12. Shared SOUL.md provenance

shared/SOUL.md is a reviewed persona boundary, not an authorization document.
Before Phase 1, the project must vendor the intended OpenClaw starter version
and record its upstream repository/source URL, tag or commit, vendoring date,
local edits, and acceptance decision. The vendored text must be reviewed for
this assistant. Operational rules remain in AGENTS.md and policy code, and
agents must not silently rewrite SOUL.md.

## 13. Privacy-safe file layout

The public repository contains portable instructions and templates only. The
planned private runtime layout is:

~~~text
agents/<id>/MEMORY.template.md     tracked template
agents/<id>/HANDOFF.template.md    tracked template
shared/USER.template.md            tracked template

runtime/agents/<id>/MEMORY.md      ignored generated memory
runtime/agents/<id>/HANDOFF.md     ignored handoff
runtime/agents/<id>/local/         ignored local overrides
runtime/agents/<id>/transcripts/   ignored raw session artifacts
runtime/browser-profiles/          ignored browser state
runtime/mail-cache/                ignored mail cache
runtime/screenshots/               ignored screenshots
runtime/downloads/                 ignored downloads
~~~

The document vault remains outside the repository. A planned npm run
privacy-check must inspect tracked/staged paths and reject forbidden runtime
artifacts, credential-shaped content, and personal-data directories before a
public push. See docs/privacy.md.

## 14. Threat model and pre-integration gate

Native Claude/Codex processes may eventually have shell and browser access as
the same macOS user. They are therefore partially trusted reasoning runtimes,
not security principals. The broker cannot be called a hard integration
boundary if an agent can read provider credentials, modify security state, or
use an equivalent provider path directly.

Before Phase 3, 4, or 5 is marked usable, the project must demonstrate that:

- provider credentials are available only to the broker/helper process through
  Keychain controls or an equivalent protection model;
- agent environments, prompts, files, and logs contain no provider credentials
  or broker signing secrets;
- provider CLIs/endpoints are not directly usable with agent-accessible
  credentials;
- security-sensitive runtime state has defined ownership, permissions, and
  tamper detection;
- browser profiles cannot bypass prohibited actions and are separated by realm;
- authorization contexts are source/session-bound and expire; and
- prompt-injection tests cannot route email, web, or message content into a
  prohibited external action.

If these conditions cannot be proven, the integration remains disabled or
manual-only. Stronger OS accounts or sandboxing in Phase 8 are defense in
depth, not a reason to defer this gate.

See docs/threat-model.md for the detailed threat model and review questions.

## 15. Technology choices

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
