# 04-tasks-codex-adapter.md

Status: complete
Spec: [04-spec-codex-adapter.md](04-spec-codex-adapter.md)
Planning mode: planning audit passed; implementation complete.

## Planning Context

Phase 0D is decomposed into three end-to-end parent tasks aligned with the
spec's demoable units. Each parent has reviewable proof artifacts. Sub-tasks are
ordered so the repository stays buildable after every small step.

Phase 0B/0C personal Claude lifecycle, terminal streaming, hygiene, and activity
behavior remain authoritative for the personal agent. These tasks do not add
dynamic agents, a work terminal WebSocket, Codex hygiene, scheduling, skills,
memory search, or external integrations.

## Repository Standards Evidence

| Source File | Read | Standards Extracted | Conflicts |
| --- | --- | --- | --- |
| `AGENTS.md`, `../AGENTS.md`, `../../AGENTS.md` | not found | No root or parent AI instruction file was present. | none |
| `README.md` | yes | React/Vite dashboard plus ASP.NET Core/C# backend; native CLIs remain runtimes; privacy check before public pushes. | none |
| `apps/dashboard/README.md` | yes | Dashboard consumes server metadata; no provider or credential state. | none |
| `apps/server/README.md` | yes | ASP.NET host owns composition/binding/routes; local-first operation. | none |
| `shared/AGENTS.shared.md` | yes | Native CLIs are runtimes; external content is untrusted; checkpoints precede clear/rotation. | none |
| `.codex/best-practices/README.md` | yes | Repo best-practice docs are authoritative over transient agent memory. | none |
| `package.json` | yes | Root checks use .NET solution commands and dashboard-prefixed npm scripts; privacy-check is required. | none |
| `apps/dashboard/package.json` | yes | React/Vite build, typecheck, lint, and Vitest are the frontend quality gates. | none |
| `packages/runtime-adapters/README.md` | yes | Adapters cover native CLI lifecycle without model API calls. | none |
| `CONTRIBUTING.md` | not found | No additional contribution policy was present. | none |

## Relevant Files

| File | Why It Is Relevant |
| --- | --- |
| `agents/work/agent.yaml` | Reviewed work definition with runtime `codex` and realm `work`. |
| `packages/harness/Agents/AgentRegistry.cs` | Reviewed definition loading and validation boundary. |
| `packages/harness/Agents/AgentSessionService.cs` | Shared lifecycle orchestration to generalize beyond personal Claude. |
| `packages/harness/Runtime/ClaudeRuntimeAdapter.cs` | Existing Claude adapter to fold behind a runtime-neutral contract. |
| `packages/harness/Runtime/CodexRuntimeAdapter.cs` | Planned Codex native lifecycle adapter. |
| `packages/harness/Runtime/RuntimeAdapterResolver.cs` | Planned runtime-to-adapter resolution seam. |
| `packages/harness/Runtime/TmuxRuntime.cs` | Existing safe tmux launch and health inspection boundary. |
| `packages/harness/HarnessRuntime.cs` | Composition root for adapters and lifecycle services. |
| `apps/server/Endpoints/AgentEndpoints.cs` | Personal and work lifecycle API routes. |
| `tests/PersonalAssistant.Harness.Tests/Agents/AgentRegistryTests.cs` | Registry validation tests for personal and work definitions. |
| `tests/PersonalAssistant.Harness.Tests/Agents/AgentSessionServiceTests.cs` | Personal lifecycle regression tests. |
| `tests/PersonalAssistant.Harness.Tests/Agents/WorkAgentSessionServiceTests.cs` | Planned work lifecycle and Codex adapter tests. |
| `tests/PersonalAssistant.Harness.Tests/Runtime/CodexRuntimeAdapterTests.cs` | Planned Codex launch/resume/fallback tests. |
| `tests/PersonalAssistant.Server.Tests/AgentApiTests.cs` | Work status/start/stop API contract tests. |
| `docs/roadmap.md` | Phase 0D acceptance gate status. |
| `scripts/privacy-check.sh` | Required deterministic privacy gate. |

## Tasks

### [x] 1.0 Reviewed work definition and runtime-neutral lifecycle boundary

#### 1.0 Proof Artifact(s)

- Test: registry tests demonstrate valid work loading, `codex` runtime
  preservation, `work` realm preservation, and rejection of unsafe definitions.
- Test: lifecycle tests demonstrate that existing personal Claude scenarios
  remain green after the runtime-neutral refactor.
- Test: adapter-resolution tests demonstrate that `claude` and `codex` select
  different native adapters without changing the shared session contract.

#### 1.0 Tasks

- [x] 1.1 Add `LoadWork()` to `AgentRegistry` with the same validation rules as
  personal loading and explicit `work` id/runtime/realm checks.
- [x] 1.2 Introduce `IAgentRuntimeAdapter`, `RuntimeStartResult`, and
  `RuntimeResumeResult`; make `IClaudeRuntimeAdapter` extend the shared contract.
- [x] 1.3 Add `RuntimeAdapterResolver` and `CodexRuntimeAdapter` skeleton wired in
  `HarnessRuntime`.
- [x] 1.4 Refactor `AgentSessionService` to resolve adapters from the validated
  definition while preserving personal method signatures and behavior.

### [x] 2.0 Codex tmux lifecycle and persisted work session

#### 2.0 Proof Artifact(s)

- Test: fake tmux executor tests demonstrate exact Codex launch and health
  command arguments, expected executable checks, safe working-directory
  handling, and no `sh -c` command construction.
- Test: providerless work-agent lifecycle tests demonstrate session creation,
  healthy adoption, missing/dead reconciliation, stop retention, and desired
  state preservation.
- Test: Codex adapter tests demonstrate new-session launch, supported resume,
  unavailable-resume fallback, opaque-reference validation, and safe error
  mapping without live Codex credentials.

#### 2.0 Tasks

- [x] 2.1 Implement Codex launch via `codex` and `codex resume <reference>`
  argument arrays through `TmuxSessionManager.LaunchProcess`.
- [x] 2.2 Add work lifecycle methods (`GetWork`, `StartWork`, `StopWork`,
  `ReconcileWork`, `RecordWorkConversationReference`) using the shared session
  contract and `work` realm activity metadata.
- [x] 2.3 Add providerless work lifecycle tests covering adoption, reconcile,
  stopped-intent, unverified pane, resume fallback, and stop retention.
- [x] 2.4 Add Codex adapter unit tests for launch, resume, unavailable resume,
  and opaque-reference validation.

### [x] 3.0 Observable work lifecycle API and security-preserving activity

#### 3.0 Proof Artifact(s)

- Test: server API tests demonstrate work status/start/stop responses, stable
  errors, and no personal-realm fallback.
- Test: activity/persistence assertions demonstrate correct work identity and
  realm with safe lifecycle metadata and no native terminal content.
- CLI/API trace: providerless hosted or test-host proof shows the work
  lifecycle contract without credentials, paths, transcripts, or raw CLI output.

#### 3.0 Tasks

- [x] 3.1 Add `/api/agents/work`, `/api/agents/work/start`, and
  `/api/agents/work/stop` routes with the existing ProblemDetails contract.
- [x] 3.2 Add server API tests for work status/start/stop and realm-safe
  activity assertions.
- [x] 3.3 Update roadmap status and proof artifacts after validation passes.

## Planning Audit Handoff

The planning audit at
`docs/specs/04-spec-codex-adapter/04-audit-codex-adapter.md` must evaluate
requirement-to-test traceability, proof artifact verifiability, repository
standards consistency, open-question resolution, regression-risk blind spots,
and non-goal leakage before implementation begins.
