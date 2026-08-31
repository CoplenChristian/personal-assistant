# Development notes

## Local prerequisites

Phase 0 is macOS-first and will eventually require:

- Node.js and npm
- `tmux`
- an authenticated `claude` CLI
- an authenticated `codex` CLI
- a local browser only for dashboard verification

The EventKit helper is a later native Swift slice. BlueBubbles, Gmail, and
browser integrations are configured outside source control.

## Workspace commands

The root package is an npm workspace with placeholder packages for the planned
server, dashboard, core harness, broker, runtime adapters, and CLI. Add
dependencies to the smallest owning workspace as implementation starts.

```sh
npm install
npm run typecheck
npm test
```

No command should require a provider token merely to typecheck or run unit
tests. Integration tests should use explicit local fixtures and clearly named
opt-in environment configuration.

## Runtime configuration

Copy `.env.example` only when local configuration is needed. Never add real
credentials to `.env`, YAML skill metadata, agent instructions, or fixtures.
Provider secrets belong in macOS Keychain. Keep live verified contacts,
account mappings, and approved target identifiers in runtime state.

## Implementation order for Phase 0

1. Define typed agent/session/activity contracts.
2. Implement safe tmux argument-array wrappers.
3. Add Claude and Codex runtime adapters behind one interface.
4. Persist agent/session state in SQLite.
5. Reconcile configured manifests with live `pa-*` sessions.
6. Stream captured pane output over a localhost WebSocket.
7. Add the smallest dashboard surface for roster, terminal, prompt, and
   compact/clear/rotate controls.
8. Add dynamic agent creation only after static manifests and reconciliation
   are tested.
