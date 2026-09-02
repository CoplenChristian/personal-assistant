# T3 Proofs: Observable work lifecycle API and security-preserving activity

Status: complete
Spec: [04-spec-codex-adapter.md](../04-spec-codex-adapter.md)
Task: [04-tasks-codex-adapter.md](../04-tasks-codex-adapter.md), parent task 3.0

## Outcome

Work lifecycle is observable through `/api/agents/work`, `/api/agents/work/start`,
and `/api/agents/work/stop` using the existing agent status contract. Work
activity carries the `work` realm with safe lifecycle metadata only.

## Acceptance evidence

- `AgentApiTests.cs` proves work status, start, and stop responses, stable
  identity fields, and no personal-realm fallback.
- `HarnessStartupReconciliationTests.cs` proves both reviewed agents reconcile
  on harness startup.
- `WorkAgentSessionServiceTests.Stop_retains_logical_work_agent_session_and_activity_history`
  and reconcile adoption tests assert `work` realm activity metadata.
- Providerless API tests return only contract fields (`id`, `runtime`,
  `desiredState`, `observedState`, `tmuxSessionName`, health flags) with no
  native output, credentials, or private paths.

## Validation

- `dotnet test PersonalAssistant.sln`
- `./scripts/privacy-check.sh`
