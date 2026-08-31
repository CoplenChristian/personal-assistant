# Development notes

This repository is still planning-level. The settings/configuration slice is
the next implementation target; the native agent runtime and external
integrations remain deferred.

## Local prerequisites

The eventual Phase 0 implementation is macOS-first and will require:

- .NET SDK 10 or the repository-selected compatible SDK
- Node.js and npm for the React/Vite dashboard only
- tmux
- an authenticated claude CLI
- an authenticated codex CLI
- a local browser for dashboard verification

The EventKit helper, BlueBubbles adapter, Gmail provider, and browser providers
are later slices. Their credentials and configuration must be external to the
source repository.

## Configuration ownership rules

Keep these boundaries explicit while implementing:

| Concern | Location | Editable from Settings? |
| --- | --- | --- |
| Hard safety invariants | Code/policy model | No |
| PA_RUNTIME_DIR, PA_SERVER_HOST, PA_SERVER_PORT | Environment/launchd | No; read-only display |
| tmux prefix | Bootstrap/runtime startup | No after sessions exist |
| Safe repository defaults | policies/defaults/*.yaml | No; never rewrite from UI |
| Ordinary user preferences | SQLite overrides | Yes, when registry metadata says editable |
| Provider secrets/tokens | macOS Keychain | No generic settings API |
| Verified contacts/account metadata/allowlists | Protected runtime state | No generic settings API |
| Personal documents | External vault | Not indexed by this task |

The effective configuration resolver must apply hard invariants to every layer.
It must not open a database to discover the path of that same database, and it
must never silently fall back across realms.

## Privacy boundary before implementation

Because the source repository is public, populate only portable tracked files.
The agents directory contains safe definitions and MEMORY.template.md/
HANDOFF.template.md files; instantiated MEMORY.md, HANDOFF.md, local agent
overrides, transcripts, browser profiles, mail caches, screenshots, and
downloads belong under ignored runtime/ paths. shared/USER.template.md is the
tracked template for private runtime user context.

See [privacy.md](privacy.md) for the layout and planned
scripts/privacy-check.sh gate. The check must be deterministic, local, and able to name
the exact staged path or credential-shaped content it rejects. .gitignore is a
guardrail, not a substitute for review.

## Phase 0 vertical slices

Phase 0 is intentionally split into independently reviewable slices:

- 0A — settings/configuration: typed registry, defaults, SQLite overrides,
  API, Settings route, and tests;
- 0B — one Claude agent persisted in tmux;
- 0C — terminal dashboard, input serialization, clear, compact, and rotation;
- 0D — Codex runtime adapter;
- 0E — dynamic agent lifecycle, AgentRegistry, roster reconciliation, and
  agents.changed notifications.

Each slice must end with a usable outcome and a focused acceptance gate. Do
not implement the later integrations merely because their settings cards or
skill placeholders exist.

The basic capability-policy contract is part of the Phase 0A design even
though external integrations are deferred. The broker will provide a stable
local integration interface with deterministic guardrails; it will not become
an IAM system around individual native-agent turns.

## Phase 0A typed settings implementation order

Implement the narrow settings vertical slice in this order:

1. Define typed setting keys, value types, categories, scopes, metadata, and
   validation constraints.
2. Load repository defaults from policies/defaults/runtime.yaml and resolve
   environment/system-derived defaults without copying defaults into SQLite.
3. Resolve bootstrap startup configuration before opening SQLite.
4. Add a SQLite settings override table with scope_type, scope_id, key,
   value_json, and updated_at. Store overrides only.
5. Add candidate-snapshot validation, including cross-field session thresholds.
6. Add GET/PATCH/DELETE localhost API routes and a settings.updated activity
   boundary.
7. Add the React Settings route with real loading, error, dirty, save, reset,
   and restart-required states.
8. Add tests for backend policy boundaries and frontend load/edit/save/reset
   behavior.
9. Only after this foundation is reviewed, let later runtime modules consume
   effective settings.

## Required backend tests

Tests must run without Anthropic/OpenAI credentials and should cover:

- defaults with no override;
- valid override persistence;
- reset to default;
- unknown-key rejection;
- invalid-value rejection;
- hard-rotate threshold below warning threshold;
- immutable safety setting rejection;
- sensitive/secret setting rejection;
- bootstrap/read-only setting rejection;
- preservation of requiresRestart metadata;
- settings.updated activity/audit boundary;
- realm/account metadata is not authorized by naming prefix alone.
- populated runtime memory/handoff files are never staged;
- settings/default changes do not rewrite existing agent definitions; and
- unconfigured tmux sessions receive no capabilities;
- scheduled jobs cannot use a capability outside their explicit subset; and
- the broker's guarantee is limited to operations invoked through the pa interface.

## Required frontend behavior

The Settings route should be a real local-admin surface, not a mock:

- load GET /api/settings;
- expose accessible controls only for editable settings;
- display locked safety and bootstrap values read-only;
- validate for usability while treating the server as authoritative;
- track dirty state and use an explicit Save changes action;
- support per-setting reset where useful;
- preserve and display requiresRestart metadata;
- show loading, error, and successful-save feedback;
- render honest not-configured integration states;
- work responsively with keyboard navigation.

## Eventual Phase 0 terminal work

When the native runtime begins:

- use capture-pane for initial/backlog hydration;
- use pipe-pane or another streaming mechanism for ongoing pane output;
- serialize input injection per agent;
- model idle, busy, waiting, and error states explicitly;
- keep terminal logs separate from durable memory;
- pass tmux arguments as arrays rather than shell-constructed strings.

Do not turn scheduled prompts or agent-to-agent messaging on until those states
and serialization rules are tested.

## Phase 0E roster and skill test boundaries

The eventual AgentRegistry tests should cover configured definitions, ignored
runtime definitions, active pa-* session discovery, stopped agents, explicit
create/start/stop/delete lifecycle rules, atomic roster snapshots, and
agents.changed delivery. A tmux session with no valid definition must remain
blocked from capability access.

The eventual skill tests should cover every ingress path—dashboard, iMessage,
scheduler, and agent message—through normalization, deterministic trigger
matching, agent-eligible skill filtering, context injection, and native skill
discovery. No routing LLM is permitted.

## Integration readiness under the local-trust model

Before Phase 3, 4, 5, or 7 is enabled, add local tests and an implementation
review for the normal guardrails in [threat-model.md](threat-model.md). At a
minimum, verify:

- the pa interface exposes only the intended provider operations;
- email send/reply/forward/draft-send remain absent;
- messaging validates verified contacts, exact participants, realm, and rate limits;
- account authorization uses stored realm metadata rather than name prefixes;
- credentials stay in Keychain or integration-specific protected state and
  never enter skills, prompts, logs, or generic settings storage;
- scheduled jobs use only their declared subset within the agent upper bound;
- message.reply requires a concrete verified inbound message reference; and
- activity records capture successful and blocked broker operations.

These checks validate harness-managed operations. They do not attempt to prove
that another trusted local agent or application cannot use an unrelated path
available to the same macOS user. Stronger OS isolation remains optional
hardening rather than a prerequisite for normal integration use.

## Workspace checks

The current npm lifecycle scripts validate only the scaffold until actual
implementation scripts are added. Once the settings slice exists, the
applicable checks should include:

~~~sh
dotnet build PersonalAssistant.sln
dotnet test PersonalAssistant.sln
npm --prefix apps/dashboard run build
npm --prefix apps/dashboard run typecheck
npm --prefix apps/dashboard test
npm --prefix apps/dashboard run lint
./scripts/privacy-check.sh
~~~

All settings tests must use local/in-memory fixtures. No provider token should be
required for build, typecheck, unit tests, the Settings route test harness, or
the privacy check.
