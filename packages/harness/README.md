# Harness core

The C#/.NET class library for the reusable Phase 0A/0B local control plane.

Planned domains:

- reviewed agent definitions and registry reconciliation
- tmux session lifecycle and process-aware health
- native runtime session persistence
- ordered SQLite migrations and shared activity persistence
- scheduler and per-agent prompt queues
- activity/event bus

The core should use argument arrays for every `tmux` invocation and should
never construct shell commands from model-generated text.
