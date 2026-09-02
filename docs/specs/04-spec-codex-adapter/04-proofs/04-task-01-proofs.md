# T1 Proofs: Reviewed work definition and runtime-neutral lifecycle boundary

Status: complete
Spec: [04-spec-codex-adapter.md](../04-spec-codex-adapter.md)
Task: [04-tasks-codex-adapter.md](../04-tasks-codex-adapter.md), parent task 1.0

## Outcome

The harness loads the reviewed `work` definition with runtime `codex` and realm
`work`, resolves native adapters through `IRuntimeAdapterResolver`, and keeps
the existing personal Claude lifecycle contract unchanged.

## Acceptance evidence

- `AgentRegistryTests.cs` proves work loading, `codex` runtime preservation,
  `work` realm preservation, and rejection of unsafe definitions.
- `AgentSessionServiceTests.cs` remains green for all personal lifecycle
  scenarios after the runtime-neutral refactor.
- `AgentSessionServiceTests.Runtime_adapter_resolver_selects_claude_and_codex_adapters`
  proves adapter resolution without changing the shared session contract.

## Validation

- `dotnet test PersonalAssistant.sln`
- `./scripts/privacy-check.sh`
