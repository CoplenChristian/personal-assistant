# Vertical-slice roadmap

The build remains staged so each phase produces a usable, testable boundary.
Configuration/settings is part of Phase 0 so later runtime code does not
scatter environment reads, defaults, or validation across modules.

| Phase | Outcome | Main scope |
| --- | --- | --- |
| 0 | Configurable local control plane foundation | typed settings registry, SQLite overrides, effective config, localhost Settings API and route, plus the native harness foundation |
| 1 | Consistent skills and durable memory | shared soul, agent instructions, skill triggers, FTS5 memory, checkpoint/materialization |
| 2 | Searchable personal document vault | watcher, parsers, chunks, FTS5, TOC, provenance, stale-memory invalidation |
| 3 | iCloud actions | EventKit helper, approved targets, reminder/calendar skills, audit |
| 4 | Safe email organization | account records with explicit realms, Gmail first, read/modify only, no send API |
| 5 | Safe iMessage channel | BlueBubbles webhook, verified contacts, reply/send guardrails, rate limits, blocked audit |
| 6 | Persistent automation and collaboration | existing-context scheduler, explicit per-job capability subsets, prompt queues, tmux agent messaging, roster metadata |
| 7 | Controlled browser agents | provider abstraction, agent-browser baseline, native adapters, profiles and allowlists |
| 8 | Continuous-operation hardening | backups, pruning, failure recovery, dashboard auth, restore, security tests, optional OS isolation |

## Phase 0 settings acceptance checklist

The following checks are requirements for the configuration slice. They are
not complete yet; they define the next implementation/review gate.

- [ ] Typed registry is the single metadata source for backend validation and UI rendering.
- [ ] Repository defaults remain in Git-tracked YAML and are never rewritten by Settings.
- [ ] PA_RUNTIME_DIR, PA_SERVER_HOST, and PA_SERVER_PORT are bootstrap/read-only.
- [ ] tmux prefix cannot be changed in a way that orphans active sessions.
- [ ] SQLite persists overrides only, with future global/realm/agent/integration scopes.
- [ ] GET returns effective value, default, override state, source, editability, and restart metadata.
- [ ] PATCH rejects unknown, invalid, immutable, bootstrap, and sensitive settings.
- [ ] DELETE removes an override and returns to the effective default.
- [ ] Cross-field session validation prevents hard rotation below warning.
- [ ] settings.updated is emitted at the activity/audit boundary.
- [ ] Safety posture is visible but cannot be weakened.
- [ ] Integration cards are honest not-configured/phase states, not fake connections.
- [ ] Settings route has loading, error, dirty, save, reset, and restart-required states.
- [ ] Frontend controls are accessible and keyboard usable.
- [ ] Tests run without Anthropic/OpenAI credentials.

## Phase 0 native harness acceptance checklist

- [ ] Claude Code launches under the harness.
- [ ] Codex launches under the harness.
- [ ] Each agent owns one prefixed tmux session.
- [ ] Dashboard reflects active/inactive sessions automatically.
- [ ] capture-pane hydrates the initial terminal backlog.
- [ ] pipe-pane or an equivalent mechanism streams ongoing terminal output.
- [ ] UI prompts reach the selected native CLI.
- [ ] Input injection is serialized per agent.
- [ ] Agent states include idle, busy, waiting, and error.
- [ ] Closing the dashboard does not kill an agent.
- [ ] Compact, clear, and hard rotate are distinct operations.
- [ ] Startup reconciles active agents and attempts reboot restoration.
- [ ] No model API integration exists.

## Later acceptance gates

### Phase 1

- [ ] The reviewed shared SOUL.md is loaded by default agents.
- [ ] Canonical email, messaging, reminders, documents, memory, and agents skills exist.
- [ ] Dashboard prompt triggers are deterministic and tested.
- [ ] Memory search uses SQLite FTS5.
- [ ] Clear checkpoints before closing a native conversation.
- [ ] A new native conversation restores durable context.

### Phase 2

- [ ] Dropping a PDF into the vault updates the database.
- [ ] TOC.md updates automatically when enabled.
- [ ] Search returns source/page provenance.
- [ ] Updating a file invalidates stale derived memory.

### Phases 3–8

Each integration phase must add focused tests for successful operations and
fail-closed security paths before it begins. A Settings card or capability
placeholder must not be presented as a working integration.
