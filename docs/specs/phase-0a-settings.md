# Phase 0A — Settings and configuration

Spec version: 1.1
Status: implementation complete; 0A.1 corrective revision complete; review complete
Architecture baseline: v1 at commit 13930c5
Stack: React/Vite dashboard plus C#/.NET ASP.NET Core backend

This is the normative spec for the first vertical slice. It is grounded in the
frozen architecture, the Phase 0A roadmap, the privacy/security documents, and
the user's explicit stack decision.

## Goal

Deliver a providerless local settings lifecycle:

~~~text
repository YAML defaults + bootstrap environment
        ->
typed C# settings registry
        ->
SQLite global user overrides
        ->
effective settings snapshot
        ->
ASP.NET Core Settings API
        ->
React /settings route
~~~

The slice is single-user and local. It must work without model credentials,
Keychain entries, tmux sessions, native CLIs, personal documents, or live
external integrations.

## Scope

In scope:

- typed registry metadata used for backend validation and UI rendering;
- versioned runtime YAML defaults and bootstrap resolution before SQLite;
- global SQLite overrides with future scope columns;
- effective values, reset, cross-field validation, and settings.updated audit;
- GET/PATCH/DELETE localhost Settings API with ProblemDetails;
- read-only system/bootstrap and safety projections;
- honest not-configured integration cards;
- React/Vite navigation shell and metadata-driven /settings route;
- deterministic local privacy-check script; and
- C# unit/API tests, React tests, build/typecheck/lint, and browser proof.

Explicitly deferred:

- native Claude/Codex lifecycle, tmux management, terminal streaming, AgentRegistry, dynamic agents, scheduler execution, and agent messaging;
- Gmail, EventKit, BlueBubbles, browser providers, Keychain flows, document indexing, and FTS5 memory behavior;
- multi-user identity, signup, tenants, RBAC, public OAuth, or cloud hosting;
- per-turn authorization/IAM, cryptographic grants, or direct Anthropic/OpenAI API calls.

Settings cards for future phases must not probe credentials or claim an
integration is connected.

## Stack and module boundaries

~~~text
PersonalAssistant.sln

packages/harness/PersonalAssistant.Harness.csproj
  Bootstrap/
  Policies/
  Settings/
  Persistence/
  Activity/

apps/server/PersonalAssistant.Server.csproj
  Program.cs
  Contracts/SettingsContracts.cs
  Endpoints/SettingsEndpoints.cs

tests/PersonalAssistant.Harness.Tests/
tests/PersonalAssistant.Server.Tests/

apps/dashboard/
  index.html
  src/app/App.tsx
  src/api/settingsApi.ts
  src/features/settings/SettingsPage.tsx
  src/features/settings/SettingControl.tsx
  src/styles.css
  tests/SettingsPage.test.tsx

scripts/privacy-check.sh
~~~

The harness library owns bootstrap/default/policy loading, the typed registry,
validation, effective resolution, SQLite overrides, and settings activity
persistence. The ASP.NET host owns composition, Kestrel binding, ProblemDetails,
and route mapping. The React app consumes the API metadata and has no duplicate
settings or safety registry.

The .NET server may serve the built dashboard from apps/dashboard/dist for
local browser proof. Vite proxies relative /api requests during development.

## Configuration layers and boot order

Resolve these before opening SQLite:

| Setting | Environment | Default | UI |
| --- | --- | --- | --- |
| system.runtimeDirectory | PA_RUNTIME_DIR | ./runtime | read-only |
| system.serverHost | PA_SERVER_HOST | 127.0.0.1 | read-only |
| system.serverPort | PA_SERVER_PORT | 4317 | read-only |
| system.tmuxPrefix | PA_TMUX_PREFIX | pa- | read-only |

The runtime directory cannot be a database override because it is needed to
locate the database. The default server bind is loopback; wildcard binds are
rejected. Tailscale/non-loopback access is launch-time configuration, not a
Settings value. The tmux prefix is fixed/read-only once sessions exist.

PA_VAULT_DIR supplies the initial documents.vaultPath default and may have an
ordinary global override. Phase 0A never reads, creates, watches, or indexes
the vault.

Boot order:

1. resolve/validate bootstrap values;
2. load versioned repository defaults and capability/realm policy snapshots;
3. build the typed registry and baseline values;
4. open personal-assistant.sqlite under the resolved runtime directory;
5. load/validate global override rows;
6. compose and validate the complete effective snapshot once;
7. construct the host with the validated bootstrap values; and
8. start ASP.NET Core on the validated host/port.

Malformed bootstrap, defaults, policies, or persisted overrides fail closed
before the ASP.NET host starts. Startup validation is not deferred until the
first API request.

## Repository defaults

Reuse policies/defaults/runtime.yaml. Existing session values remain in their
current sections. Add safe defaults for the new settings:

~~~yaml
appearance:
  theme: system
  browser_scrollback_lines: 5000

agents:
  defaults:
    runtime: claude
    auto_start: false

documents:
  automatic_indexing: false
  automatic_toc_regeneration: false

memory:
  max_fts5_results: 100
  auto_materialize_generated_memory: false

scheduler:
  timezone: local
  missed_run_policy: skip
  max_queued_prompts_per_agent: 10

safety:
  checkpoint_before_rotation: true
~~~

Defaults are read-only Git inputs. Settings never writes user values back to
YAML, agent manifests, templates, or native sessions.

Effective resolution is:

~~~text
bootstrap-only -> validated environment/bootstrap value
editable       -> global SQLite override, if present
                 -> environment/system-derived default, if applicable
                 -> repository YAML default
                 -> explicit safe code default
safety view    -> capability/realm/security policy, never an override
~~~

Only overrides are persisted. Reset deletes the row.

## Registry contract

Each definition contains key, category, label, description, value type/options,
default resolver/source, scope, editable, resettable, requiresRestart,
bootstrap, sensitive, and validation metadata. Keys are exact and
case-sensitive. Phase 0A persists global scope only; realm, agent, and
integration scopes are reserved and rejected.

Initial editable keys:

| Key | Type | Constraints |
| --- | --- | --- |
| appearance.theme | enum | system, light, dark |
| appearance.browserScrollbackLines | integer | 100..100000 |
| agents.defaults.runtime | enum | claude, codex; future agents only |
| agents.defaults.autoStart | boolean | future agents only |
| sessions.tmuxHistoryLines | integer | 100..100000 |
| sessions.terminalLogWarningBytes | integer | 1..1073741824 |
| sessions.terminalLogRotatedFiles | integer | 1..100 |
| sessions.nativeSessionWarningBytes | integer | 1..1073741824 |
| sessions.nativeSessionRotateBytes | integer | 1..4294967296 and greater than warning |
| sessions.nativeSessionArchiveTtlDays | integer | 1..3650 |
| documents.vaultPath | path string | normalized, outside repository |
| documents.automaticIndexing | boolean | future indexer |
| documents.automaticTocRegeneration | boolean | future indexer |
| memory.maxFts5Results | integer | 1..1000 |
| memory.autoMaterializeGeneratedMemory | boolean | future Phase 1 |
| automation.timezone | string | valid system timezone |
| automation.missedRunPolicy | enum | skip, run-once |
| automation.maxQueuedPromptsPerAgent | integer | 0..100 |

System/bootstrap keys are visible but immutable:

- system.runtimeDirectory
- system.serverHost
- system.serverPort
- system.tmuxPrefix

Safety keys are separate locked projections, derived from actual policy inputs:

- safety.emailSending
- safety.unverifiedMessageRecipients
- safety.groupMessaging
- safety.crossRealmFallback
- safety.consequentialAudit
- safety.checkpointBeforeRotation

Sensitive definitions are never accepted by the generic settings store. No
credential, token, credential reference, or provider account is a Phase 0A
setting.

Changing an agent default affects only future agent creation; existing
manifests and sessions are not rewritten.

## SQLite and activity contract

Use an explicit harness-owned migration:

~~~sql
CREATE TABLE settings_overrides (
    scope_type TEXT NOT NULL
        CHECK (scope_type IN ('global', 'realm', 'agent', 'integration')),
    scope_id TEXT NOT NULL DEFAULT '',
    key TEXT NOT NULL,
    value_json TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY (scope_type, scope_id, key),
    CHECK (
        (scope_type = 'global' AND scope_id = '')
        OR
        (scope_type <> 'global' AND length(scope_id) > 0)
    )
);
~~~

Phase 0A accepts only global with an empty scope ID. Defaults are never
inserted. value_json is canonical JSON for the registry scalar type.

The same SQLite transaction boundary appends settings.updated activity:

~~~sql
CREATE TABLE activity_events (
    id TEXT PRIMARY KEY,
    timestamp TEXT NOT NULL,
    agent_id TEXT,
    realm TEXT,
    category TEXT NOT NULL,
    operation TEXT NOT NULL,
    target TEXT,
    status TEXT NOT NULL,
    duration_ms INTEGER,
    metadata_json TEXT NOT NULL
);
~~~

Activity metadata contains operation, scope, changed keys, and restart
information only. It never contains setting values, paths, credentials, or
tokens. Rejected and no-op requests do not emit a success event.

## API contract

~~~text
GET    /api/settings
PATCH  /api/settings
DELETE /api/settings/{key}
~~~

GET returns contractVersion phase-0a-settings.v1, editable setting metadata,
effective value, baseline default, override state, source, global scope,
restart/bootstrap/sensitive flags, constraints, separate locked safety rows,
and separate integration rows.

Integration rows are honest static states:

~~~text
Email                  not-configured / Phase 4
Calendar & Reminders   not-configured / Phase 3
BlueBubbles             not-configured / Phase 5
Browser                 not-configured / Phase 7
~~~

PATCH accepts an atomic batch:

~~~json
{
  "changes": [
    { "key": "appearance.theme", "value": "dark" }
  ]
}
~~~

The server rejects unknown, unsupported-scope, immutable, bootstrap, safety,
sensitive, malformed, wrongly typed, and invalid cross-field changes before
any write. The candidate effective snapshot must be valid before the
transaction commits. A value equal to its baseline default deletes its
override. If the requested effective values and persisted override rows are
already unchanged, the operation is a no-op: it performs no settings-row write
and appends no success activity event. Successful changed operations return the
complete effective snapshot and append settings.updated.

DELETE removes an editable global override and returns the complete effective
snapshot. Reset is idempotent when no override exists. It never writes defaults.

Use RFC 7807 ProblemDetails with stable codes:

~~~text
invalid_request
unknown_setting
unsupported_scope
immutable_setting
bootstrap_setting
sensitive_setting
invalid_value
cross_setting_invalid
settings_store_invalid
settings_unavailable
~~~

Errors identify the rejected key/reason without echoing sensitive values or
private credential material.

## React route contract

The /settings route uses a simple navigation shell and these sections:

- General
- Agents
- Sessions
- Documents & Memory
- Automation
- System
- Integrations
- Safety

It loads actual API data with a relative /api/settings request. Controls are
rendered from response metadata, not a second client registry. The route must
support loading, API failure/retry, draft edits, dirty state, explicit Save
changes, per-setting Reset, server validation errors, successful-save feedback,
and restart-required indicators.

System/bootstrap and safety values are visibly read-only. Future integrations
show their not-configured phase state. Controls have accessible labels,
descriptions, errors/status announcements, keyboard behavior, and responsive
layout. The route has no credential inputs, fake activity, telemetry, or
localStorage settings store.

The visual direction is a compact local control room: dark ink/slate,
warm amber/cream emphasis, teal safety signals, characterful serif display
type, monospace utility labels, subtle texture, and restrained motion.

## Privacy contract

scripts/privacy-check.sh is deterministic, local, providerless, and independent
of the application database. It inspects tracked, staged, and visible
untracked paths and reports every exact rejection.

Reject runtime databases/logs, generated memory/handoffs, local agent
overrides, transcripts, caches, browser profiles, screenshots, downloads,
personal-vault paths, credentials/secrets/OAuth/tokens, cookies/storage state,
JSONL, private-key markers, obvious access tokens, and .NET bin/obj/TestResults
or coverage output. Tracked templates remain allowed.

The check uses no network or LLM. Personal documents remain in an external
vault. The dashboard commits package-lock.json for reproducible dependency
resolution; node_modules remains ignored and is never committed.

## Proof and definition of done

All proof is providerless and uses temporary/in-memory fixtures.

Backend/API proof must cover defaults, override persistence, reset, unknown and
invalid values, cross-field rotation thresholds, atomic batches, locked
safety/bootstrap values, sensitive definitions, future-scope rejection,
restart metadata, settings.updated transaction behavior, malformed persisted
rows, startup rejection of invalid persisted/effective configuration, an
already-default no-op PATCH with no rows/events, unchanged
YAML/manifests/templates, API 404 behavior for unknown /api routes, and
ProblemDetails responses.

Frontend/browser proof must cover /settings loading/error/retry, metadata-driven
editing, dirty/save/reset, server validation failure, restart indicators,
locked fields, honest integration cards, keyboard/accessibility behavior,
responsive layout, and the ASP.NET-hosted route.

Phase 0A.1 is done when:

1. React/Vite and ASP.NET Core/.NET implement this contract;
2. the C# registry is the only settings metadata source;
3. bootstrap resolves before SQLite;
4. YAML remains read-only and SQLite stores overrides only;
5. API, UI, reset, validation, and activity behavior work end-to-end;
6. privacy and providerless tests pass;
7. applicable .NET and dashboard checks plus browser proof pass; and
8. the committed dashboard lockfile reproduces the declared dependency graph;
9. no 0B–0E, integration, native-runtime, or model-API work enters the diff.

After the corrective review is complete, freeze Phase 0A.1 and use the revised
Phase 0B spec as the next implementation boundary.
