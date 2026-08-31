# Development notes

This repository is still planning-level. The settings/configuration slice is
the next implementation target; the native agent runtime and external
integrations remain deferred.

## Local prerequisites

The eventual Phase 0 implementation is macOS-first and will require:

- Node.js and npm
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

## Typed settings implementation order

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

## Workspace checks

The current npm lifecycle scripts validate only the scaffold until actual
implementation scripts are added. Once the settings slice exists, the
applicable checks should include:

~~~sh
npm run build
npm run typecheck
npm test
npm run lint
~~~

All settings tests must use local/in-memory fixtures. No provider token should be
required for build, typecheck, unit tests, or the Settings route test harness.
