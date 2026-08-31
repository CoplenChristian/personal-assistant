# Architecture brief

This document is the checked-in implementation brief for the supplied
Personal Assistant Harness plan.

## Core principle

The harness is a deterministic local control plane around native Claude Code
and Codex processes. It owns persistence, safety, routing, schedules, memory,
and observation. It never talks directly to Anthropic or OpenAI model APIs.

```text
React dashboard
      │ HTTP / WebSocket
      ▼
Local Node server ───── SQLite + FTS5
      │                         │
      ├─ Agent registry         ├─ agents/sessions/jobs/activity/memory
      ├─ Session manager        └─ document index
      ├─ Scheduler
      └─ Capability broker ─── Unix-domain socket ─── `pa` CLI
                                      │
                                      ├─ mail
                                      ├─ EventKit calendar/reminders
                                      ├─ verified BlueBubbles messaging
                                      ├─ document vault
                                      ├─ memory
                                      └─ controlled browser

tmux session pa-<agent-id>
      └─ native `claude` or `codex` process
```

## State ownership

- Git contains code, instructions, skill definitions, schemas, and policy
  defaults.
- SQLite/runtime state contains sessions, schedules, live roster data,
  verified contacts, account mappings, audit events, and searchable memory.
- macOS Keychain contains provider credentials and tokens.
- The separate local document vault contains personal documents and is indexed
  without being committed.
- `SOUL.md`, `AGENTS.md`, and generated `MEMORY.md` provide compact native-CLI
  context; terminal logs are not durable memory.

## Agent and session model

An agent is a durable logical object defined by `agents/*/agent.yaml`. Each
active agent owns one `pa-<id>` tmux session. A native conversation may rotate
without deleting the logical agent, its realm, skills, schedule references, or
durable memory.

The runtime adapter contract covers:

```text
start, stop, restart, sendPrompt, captureOutput, getStatus,
compact, clear, rotateSession, resumeSession, measureSessionDiskUsage
```

tmux survives browser/UI and harness restarts, but not a machine reboot. On
startup, the harness reconciles configured agents with live prefixed tmux
sessions and attempts native conversation resume. If resume is unavailable, it
starts a fresh native conversation with the latest durable handoff and memory.

## Session hygiene

Clear and hard rotation must checkpoint first:

1. trigger the memory checkpoint hook;
2. write durable facts to SQLite/materialized memory;
3. write unresolved work to `HANDOFF.md` or equivalent state;
4. record the current native session ID;
5. close the current conversation;
6. start/resume a fresh native conversation with compact context.

Compaction, terminal-log rotation, and native-session size limits are
configuration. Suggested starting limits are in
`policies/defaults/runtime.yaml`.

## Capability broker and realms

Skills explain how to request capabilities; they do not authorize them. Every
request carries `agent_id`, `realm`, `capability`, `operation`, `parameters`,
and `request_id`. The broker checks that request against policy and fails
closed when authorization is uncertain.

Resources and agents use `personal`, `work`, or `shared` realms. Account IDs
are always explicit. An agent must never silently fall back between personal
and work resources.

## Scheduler and collaboration

Scheduled jobs reference logical agent IDs, not native conversation IDs. A due
job is queued into the existing agent session; it does not launch a one-shot
agent. Each agent receives one injected prompt at a time.

Agent-to-agent messaging stores an audit row first, then injects a labeled
message into the recipient tmux session using argument-array `tmux` calls. The
recipient's own broker permissions still apply.

## Browser boundary

Expose a provider interface rather than baking one runtime's browser behavior
into the harness:

```text
BrowserProvider
  ClaudeChromeProvider
  CodexNativeProvider
  AgentBrowserProvider
```

Profiles and domain policy are realm-specific. Browser content is untrusted,
credentials are never automatically extracted, and visible activity is
required for sensitive workflows.

## Technology choices

| Area | Choice |
| --- | --- |
| Host | macOS |
| Language | TypeScript / Node.js |
| UI | React + Vite |
| Terminal | xterm.js |
| Transport | localhost HTTP + WebSocket; Unix socket for broker |
| Persistence | SQLite + FTS5 |
| Agent persistence | tmux |
| Apple integration | Swift/EventKit helper |
| Messaging | BlueBubbles adapter with verified contacts |
| Secrets | macOS Keychain |
| Startup | launchd for the harness only |
| Model runtime | native Claude Code and Codex CLIs |
