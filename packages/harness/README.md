# Harness core

The C#/.NET class library for the reusable Phase 0A local control plane.

Planned domains:

- agent definitions and registry reconciliation
- tmux session lifecycle
- native runtime session persistence and rotation
- scheduler and per-agent prompt queues
- activity/event bus

The core should use argument arrays for every `tmux` invocation and should
never construct shell commands from model-generated text.
