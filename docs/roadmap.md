# Vertical-slice roadmap

The repository is intentionally staged so each phase produces a usable,
testable boundary.

| Phase | Outcome | Main scope |
| --- | --- | --- |
| 0 | Durable native agent harness | tmux, adapters, SQLite agents/sessions, WebSocket stream, dashboard, dynamic agents, roster, rotation |
| 1 | Consistent skills and durable memory | shared soul, agent instructions, skill triggers, FTS5 memory, checkpoint/materialization |
| 2 | Searchable personal document vault | watcher, parsers, chunks, FTS5, TOC, provenance, stale-memory invalidation |
| 3 | iCloud actions | EventKit helper, approved targets, reminder/calendar skills, audit |
| 4 | Safe email organization | account abstraction, Gmail first, realms, read/modify only, no send API |
| 5 | Safe iMessage channel | BlueBubbles webhook, verified contacts, reply/send guardrails, rate limits, blocked audit |
| 6 | Persistent automation and collaboration | existing-context scheduler, prompt queues, tmux agent messaging, roster metadata |
| 7 | Controlled browser agents | provider abstraction, agent-browser baseline, native adapters, profiles and allowlists |
| 8 | Continuous-operation hardening | backups, pruning, failure recovery, dashboard auth, restore, security tests, optional OS isolation |

## Phase 0 acceptance checklist

- [ ] Claude Code launches under the harness.
- [ ] Codex launches under the harness.
- [ ] Each agent owns one prefixed tmux session.
- [ ] Dashboard reflects active/inactive sessions automatically.
- [ ] Native terminal output streams live.
- [ ] UI prompts reach the selected native CLI.
- [ ] Closing the dashboard does not kill an agent.
- [ ] Compact, clear, and hard rotate are distinct operations.
- [ ] Startup reconciles active agents and attempts reboot restoration.
- [ ] No model API integration exists.

## Phase 1 acceptance checklist

- [ ] The reviewed shared `SOUL.md` is loaded by default agents.
- [ ] Canonical email, messaging, reminders, documents, memory, and agents skills exist.
- [ ] Dashboard prompt triggers are deterministic and tested.
- [ ] Memory search uses SQLite FTS5.
- [ ] Clear checkpoints before closing a native conversation.
- [ ] A new native conversation restores durable context.

## Phase 2–8 gates

Each integration phase must add focused tests for both successful operations
and fail-closed security paths before moving to the next external system. The
full acceptance criteria remain architectural requirements, not prompt-only
behavioral suggestions.
