# Task 03 Proofs - Checkpoint-gated session hygiene and terminal logs

## Task Summary

This task adds the first safe context-management boundary for the configured
personal Claude session. Compact, clear, and rotate are typed harness actions
that checkpoint first; terminal output is retained only in ignored runtime log
artifacts; and the dashboard exposes explicit, retry-safe controls.

## What This Task Proves

- Checkpoints materialize generated sections only under ignored runtime paths,
  preserve human-maintained template content, and write privacy-safe manifests
  and activity events.
- Typed compact, clear, and rotate actions are serialized per logical agent,
  replay successful request IDs without repeating native work, preserve desired
  agent state, and clear stale opaque references after rotation.
- Failed checkpoints block native mutation; native failures retain the logical
  session row and record a safe failure event.
- Hygiene routes return versioned receipts or ProblemDetails without runtime
  paths, terminal content, or checkpoint payloads.
- Terminal logs use an ignored active path, bounded writes, threshold warnings,
  atomic rotation, and configured retention.
- The hosted dashboard renders checkpoint-first controls and reports skipped,
  blocked, and successful outcomes without claiming work that did not happen.

## Evidence Summary

- `dotnet test PersonalAssistant.sln`: 88 harness tests and 22 server tests
  passed.
- Dashboard Vitest: 6 test files and 20 tests passed; typecheck, lint, and
  production Vite build passed.
- `./scripts/privacy-check.sh` and `git diff --check` passed.
- Hosted ASP.NET proof used a deterministic providerless Claude fixture. The
  page showed the canonical terminal, four hygiene controls, checkpoint-first
  copy, and a real successful compact result. No provider credentials or
  personal transcript were used.

## Artifact: Checkpoint and runtime-service tests

**What it proves:** Checkpoint ordering, marker preservation, request
idempotency, concurrency rejection, failed-checkpoint blocking, stale-reference
clearing, and retained logical session state are covered by fake providerless
tests.

**Why it matters:** These tests protect the destructive/context-changing
boundary without requiring Anthropic credentials or a live personal session.

**Command:**

~~~bash
dotnet test PersonalAssistant.sln
~~~

**Result summary:** Both C# projects passed with zero failures.

~~~text
PersonalAssistant.Harness.Tests: 88 passed, 0 failed
PersonalAssistant.Server.Tests: 22 passed, 0 failed
~~~

## Artifact: Terminal log writer tests

**What it proves:** The writer uses `runtime/agents/personal/terminal/active.log`,
rejects oversized chunks, emits warning/rotation events without content or
paths, rotates the active file, and retains only the configured number of
rotated files.

**Why it matters:** Terminal logs remain operational artifacts and cannot be
mistaken for durable memory or leak into activity metadata.

**Command:**

~~~bash
dotnet test tests/PersonalAssistant.Harness.Tests/PersonalAssistant.Harness.Tests.csproj --filter 'FullyQualifiedName~TerminalLogWriterTests|FullyQualifiedName~TmuxTerminalStreamTests'
~~~

**Result summary:** 9 focused stream/log tests passed.

~~~text
Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9
~~~

## Artifact: Hygiene API contracts and routes

**What it proves:** The explicit compact, clear, rotate, and checkpoint routes
accept typed checkpoint requests and return only versioned opaque receipts.
Stable blocked/failure ProblemDetails codes cover malformed requests,
checkpoint failure, concurrent action rejection, and runtime failure.

**Why it matters:** The browser cannot provide arbitrary executables, tmux
sessions, native references, or shell commands, and response metadata cannot
expose private runtime state.

**Command:**

~~~bash
dotnet test tests/PersonalAssistant.Server.Tests/PersonalAssistant.Server.Tests.csproj --filter FullyQualifiedName~HygieneApiTests
~~~

**Result summary:** All 5 API contract tests passed.

~~~text
Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5
~~~

## Artifact: Dashboard control tests and production bundle

**What it proves:** Controls show checkpoint progress, disable every action
while one is in flight, provide accessible status/alert announcements, keep
blocked errors retryable, and do not call a skipped native action successful.

**Why it matters:** Context-changing operations remain explicit and honest in
the user-facing surface.

**Commands:**

~~~bash
npm ci --prefix apps/dashboard
npm --prefix apps/dashboard test
npm --prefix apps/dashboard run typecheck
npm --prefix apps/dashboard run lint
npm --prefix apps/dashboard run build
~~~

**Result summary:** The clean dependency install completed without
vulnerabilities. The dashboard suite passed 20 tests across 6 files; typecheck,
lint, and the Vite production build completed successfully.

~~~text
Test Files 6 passed (6)
Tests 20 passed (20)
vite production build: 39 modules transformed
~~~

## Artifact: Hosted browser proof

**What it proves:** The ASP.NET-served `/agents/personal` route renders the
canonical screen beside the checkpoint-gated controls, then shows a real
successful compact result from a deterministic providerless native fixture.

**Why it matters:** This confirms the integrated user-facing experience rather
than only isolated backend and React behavior.

**Artifact path:** `screenshots/sdd_t3_hygiene_success.png`

**Result summary:** The hosted page showed `LIVE SCREEN`, `HYDRATED SCREEN`,
`STATE: IDLE`, `COMPACT CONTEXT`, `CLEAR CONTEXT`, `ROTATE CONVERSATION`, and
`CHECKPOINT NOW`. After the compact control was exercised, the visible result
was `Checkpoint complete. Compact context was accepted by the harness.` The
fixture screen contained only `PROVIDERLESS CLAUDE FIXTURE` and
`Harness-only screen proof`.

![Hosted personal-agent screen with checkpoint-gated session hygiene controls](../../../../screenshots/sdd_t3_hygiene_success.png)

## Artifact: Quality and privacy gates

**What it proves:** The full repository quality gates and privacy guard pass
before staging.

**Commands:**

~~~bash
npm run build
npm run typecheck
npm run lint
./scripts/privacy-check.sh
git diff --check
~~~

**Result summary:** .NET build completed with 0 warnings and 0 errors, the
dashboard bundle built, typecheck and lint completed without findings, the
privacy check passed, and the diff contained no whitespace errors.

~~~text
Build succeeded. 0 Warning(s). 0 Error(s).
privacy-check: passed
~~~

## Reviewer Conclusion

The T3 implementation provides a checkpoint-first, typed session-hygiene
boundary with safe runtime-only logs and an honest dashboard control surface.
All providerless C# and React evidence passes, and the hosted proof contains
only deterministic fixture content.
