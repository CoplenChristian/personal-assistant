# Personal Assistant Harness

A thin, macOS-first local harness around the native `claude` and `codex` CLIs.
The harness owns persistence, safety policy, routing, scheduling, memory, and
the local dashboard. The native CLIs remain the model runtimes.

## Status

This repository is an initial architecture scaffold. The Phase 0 runtime is
not implemented yet; the checked-in layout and documents are the starting
point for that work.

## Design boundary

```text
Claude Code / Codex decide what they want to do.

The harness decides whether they may do it
and how that action is safely executed.
```

The harness must never call Anthropic or OpenAI model APIs directly. It starts
and controls native CLI processes inside durable `tmux` sessions.

## Repository map

```text
apps/                 Server and React dashboard entry points
packages/             Harness, capability broker, and runtime adapter domains
cli/                  The local `pa` capability/request CLI
agents/               Durable agent manifests and instruction/memory files
shared/               Shared persona and operating guidance
skills/               Canonical procedural skill catalog
hooks/                Deterministic routing, checkpoint, and activity hooks
policies/             Schemas and fail-closed policy defaults
macos/                Native macOS helpers, including EventKit
docs/                 Architecture, roadmap, and development notes
runtime/              Ignored local state created when the harness runs
```

## Safety baseline

- There is no email send capability.
- Outbound messages require a verified runtime contact; groups are disabled.
- Realm/account identifiers are explicit; cross-realm access fails closed.
- Credentials and mutable security state stay in Keychain/runtime state.
- External email, web, and message content is always untrusted input.
- Consequential actions produce immutable audit events.
- Session rotation checkpoints durable memory first.

See [docs/security-invariants.md](docs/security-invariants.md) and
[docs/architecture.md](docs/architecture.md).

## Development

The repository uses npm workspaces and TypeScript as the intended application
stack. The workspace packages currently contain contracts and scaffolding;
implementation dependencies will be added as each vertical slice begins.

```sh
npm install
npm run typecheck
npm test
```

Those commands are intentionally lightweight at this stage. See
[docs/development.md](docs/development.md) for the Phase 0 prerequisites and
local setup decisions.
