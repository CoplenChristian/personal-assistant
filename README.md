# Personal Assistant Harness

A thin, macOS-first local harness around native Claude Code and Codex CLIs.
The harness owns persistence, safety policy, routing, scheduling, memory, and
the local dashboard. The native CLIs remain the model runtimes.

## Status

This repository contains the reviewed Phase 0A configuration/settings slice
and the Phase 0B one-agent lifecycle implementation. Terminal streaming,
external integrations, and later agent lifecycle features remain deferred. The
documents in this repository are the current design and implementation baseline.

The first implementation slice is configuration/settings because it gives the
later runtime a single lifecycle for defaults, overrides, validation, and
effective values.

## Architectural boundary

~~~text
Claude Code / Codex decide what they want to do.

The harness decides whether they may do it
and how that action is safely executed.
~~~

The harness must never call Anthropic or OpenAI model APIs directly. It will
start and control native CLI processes inside durable tmux sessions.

## Configuration model

Configuration has separate ownership boundaries:

~~~text
bootstrap environment / launch configuration
  -> process startup only
  -> PA_RUNTIME_DIR, PA_SERVER_HOST, PA_SERVER_PORT, and fixed tmux prefix

hard security invariants
  -> constrain every effective configuration value

repository/system defaults
  -> policies/defaults/*.yaml, including runtime.yaml

runtime SQLite overrides
  -> ordinary user preferences only

realm/agent/integration overrides
  -> future scoped support, subject to the same constraints

effective configuration
  -> Settings API and dashboard
~~~

Bootstrap values are available before SQLite opens and therefore cannot be
overridden by a database stored inside the runtime directory. User changes
never write back to Git-tracked defaults. Secrets belong in macOS Keychain,
personal documents belong in an external vault, and mutable security state
belongs in protected runtime state.

The first settings surface is intentionally small:

- appearance: theme and browser terminal scrollback
- agent defaults: runtime and auto-start for newly created agents
- session limits from runtime.yaml
- document vault path and indexing preferences
- FTS5 memory limits and materialization preference
- scheduler timezone, missed-run policy, and queue limit
- read-only system/bootstrap values
- read-only safety posture
- honest not-configured integration cards

Existing agents are not rewritten when a default changes.

## Safety baseline

- Email sending does not exist as a broker capability.
- Unverified or arbitrary message recipients remain blocked.
- Group messaging remains disabled.
- Agents cannot create or modify verified contacts.
- External email, web, and message content is untrusted input.
- Work and personal account access is checked against persisted account realm metadata.
- Scheduled jobs receive an explicit subset of an agent's allowed capabilities.
- Consequential actions produce immutable audit events.
- Session clear/rotation checkpoints durable memory first.
- Unknown, invalid, immutable, bootstrap, and sensitive setting writes fail closed.
- Operations invoked through Personal Assistant capabilities obey deterministic broker guardrails.
- Native agents run with the privileges of the local macOS user; this harness does not sandbox unrelated local actions.

See [docs/security-invariants.md](docs/security-invariants.md) and
[docs/architecture.md](docs/architecture.md). The required threat model is in
[docs/threat-model.md](docs/threat-model.md).

## State boundaries

| Data | Source of truth |
| --- | --- |
| Code, instructions, skill definitions, schemas, policy defaults, safe templates | Git |
| Bootstrap startup configuration | Environment or launchd |
| User preferences and runtime overrides | SQLite in the ignored runtime directory |
| Provider credentials and tokens | macOS Keychain |
| Verified contacts, account metadata, approved targets | Protected runtime state |
| Personal documents | External local vault, never Git |
| Searchable memory and document index | SQLite/FTS5 runtime state |
| Terminal/session logs | Rotated runtime artifacts, never durable memory |

See [docs/privacy.md](docs/privacy.md) for the tracked-template and ignored
runtime layout. This repository is public, so privacy review is required even
when the source repository itself has no credentials.

## Access and trust model

~~~text
Internet
   X

Tailscale / trusted local network
        |
        v
single-user Personal Assistant dashboard
        |
        v
local harness and native local agents
~~~

The dashboard is a single-user local application. Tailscale is the intended
remote access path; the server must retain a safe bind configuration and must
not be casually exposed to the public internet. There is no signup, tenant,
organization, multi-user RBAC, or public OAuth login design.

The harness guarantee applies to operations performed through its pa CLI and
capability broker. A separately trusted Claude Code, Codex, or other local
agent process may independently use an application, CLI, browser profile, or
OS API available to the same macOS user; that action is outside this harness's
security boundary.

## Repository map

~~~text
apps/                 Server and React dashboard entry points
packages/             Harness, capability broker, and runtime adapter domains
cli/                  The local pa capability/request CLI
agents/               Safe agent manifests, instructions, and templates
shared/               Shared persona and operating guidance
skills/               Canonical procedural skill catalog
hooks/                Deterministic routing, checkpoint, and activity hooks
policies/             Schemas and fail-closed policy defaults
macos/                Native macOS helpers, including EventKit
docs/                 Architecture, roadmap, and development notes
runtime/              Ignored local state created when the harness runs
~~~

## Roadmap

Phase 0A includes the typed configuration registry, SQLite override store,
localhost Settings API, real Settings route, privacy check, and providerless
tests. Phase 0B adds one persisted Claude agent with desired/observed state,
safe tmux lifecycle management, and minimal status controls. Later slices add
terminal streaming, document indexing, external integrations, browser
providers, scheduling, collaboration, and hardening.

See [docs/roadmap.md](docs/roadmap.md) for acceptance gates and
[docs/development.md](docs/development.md) for the implementation order.
Run scripts/privacy-check.sh before staging or pushing public changes.
