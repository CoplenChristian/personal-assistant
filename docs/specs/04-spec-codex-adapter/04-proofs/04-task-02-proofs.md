# T2 Proofs: Codex tmux lifecycle and persisted work session

Status: complete
Spec: [04-spec-codex-adapter.md](../04-spec-codex-adapter.md)
Task: [04-tasks-codex-adapter.md](../04-tasks-codex-adapter.md), parent task 2.0

## Outcome

The reviewed work agent starts, adopts, reconciles, and stops through the same
durable session model as the personal agent. Codex launch uses typed tmux
argument arrays with `codex` and `codex resume <reference>`; unavailable resume
falls back to a fresh conversation without inspecting native storage.

## Acceptance evidence

- `WorkAgentSessionServiceTests.Launch_uses_codex_resume_subcommand_instead_of_claude_flag`
  asserts exact Codex launch arguments, no `send-keys`, and no `sh -c`.
- `WorkAgentSessionServiceTests.cs` covers session creation, healthy adoption,
  stopped-intent reconciliation, stop retention, and resume fallback.
- `CodexRuntimeAdapterTests.cs` covers new-session launch, supported resume,
  unavailable-resume fallback, opaque-reference validation, and codex health
  inspection without live credentials.

## Validation

- `dotnet test PersonalAssistant.sln`
- `./scripts/privacy-check.sh`
