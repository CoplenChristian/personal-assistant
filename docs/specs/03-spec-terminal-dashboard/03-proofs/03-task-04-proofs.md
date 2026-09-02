# Task 04 Proofs - Activity feed and local-day counters

## Task Summary

This task adds immutable SQLite-backed activity aggregation, a versioned
`GET /api/activity` read API, safe terminal/hygiene event emission, and an
`ActivityPanel` beside the personal terminal workspace. Activity refresh stays
independent from the terminal WebSocket.

## What This Task Proves

- Canonical counter keys always return explicit zero values for deferred
  integrations.
- Local-day bucketing, timezone boundaries, deterministic ordering, and
  metadata redaction work over immutable activity rows.
- Terminal hydration/stream/input/state events are recorded without raw
  terminal or input payloads.
- The dashboard shows local-day labels, counters, recent feed, empty/error
  states, blocked/failure styling, independent refresh, and audit-degraded
  warnings when telemetry recording has failed.

## Evidence Summary

- `dotnet test PersonalAssistant.sln`: 104 harness tests and 26 server tests
  passed after second-pass remediation (bounded counter reads, `auditDegraded`
  contract, migration 003 upgrade path, lower date bounds, abortable refresh).
- Dashboard Vitest: 7 test files and 26 tests passed; typecheck, lint, and
  production Vite build passed.
- `./scripts/privacy-check.sh` and `git diff --check` passed.
- Hosted ASP.NET proof on `/agents/personal` showed the terminal workspace,
  activity counters with zero states, and a recent feed without provider
  credentials or transcript content. Narrow-width layout for this task is
  **unverified** (no narrow screenshot was captured for T4).

## Artifact: Harness activity tests

**What it proves:** Immutable activity queries, recursive redaction, counter
mapping, UTC-ms day bounds, SQL-bounded feed retrieval, telemetry degradation
signaling, and migration 003 backfill from a pre-003 database.

**Why it matters:** The feed must be trustworthy without inferring integration
success from settings cards or loading full metadata for every counter query.

**Command:**

~~~bash
dotnet test tests/PersonalAssistant.Harness.Tests/PersonalAssistant.Harness.Tests.csproj \
  --filter 'FullyQualifiedName~Harness.Tests.Activity'
~~~

**Result summary:** All harness activity tests passed across five test classes.

~~~text
Passed! - Failed: 0, Passed: 16, Skipped: 0, Total: 16
~~~

## Artifact: Versioned activity API

**What it proves:** `GET /api/activity` returns versioned JSON, explicit zero
counters, bounded recent events, blocked/failure statuses, redacted metadata,
and `auditDegraded` when telemetry recording has failed.

**Why it matters:** The dashboard consumes a stable contract separate from the
terminal WebSocket and can surface degraded audit state without blocking input.

**Command:**

~~~bash
dotnet test tests/PersonalAssistant.Server.Tests/PersonalAssistant.Server.Tests.csproj \
  --filter 'FullyQualifiedName~ActivityApiTests'
~~~

**Result summary:** Four API contract tests passed, including `auditDegraded`
reporting.

~~~text
Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4
~~~

## Artifact: Dashboard activity panel tests

**What it proves:** The React panel renders local-day labels, zero counters,
blocked labels, empty states, errors, timezone-aware times, audit-degraded
warnings, and independent refresh without coupling to the terminal WebSocket.

**Commands:**

~~~bash
npm ci --prefix apps/dashboard
npm --prefix apps/dashboard test
npm --prefix apps/dashboard run typecheck
npm --prefix apps/dashboard run lint
npm --prefix apps/dashboard run build
~~~

**Result summary:** 26 dashboard tests passed across 7 files, including six
`ActivityPanel` cases and unhealthy-page activity visibility on
`PersonalAgentPage`.

~~~text
Test Files 7 passed (7)
Tests 26 passed (26)
~~~

## Artifact: Hosted browser proof

**What it proves:** The ASP.NET-served `/agents/personal` route renders the
canonical terminal beside the activity summary with local-day labeling and zero
states at desktop width.

**Why it matters:** This confirms the integrated control-room surface rather
than isolated API or component behavior.

**Artifact path:** `screenshots/sdd_t4_workspace_desktop.png`

**Result summary:** The hosted workspace showed the activity panel with local-day
labeling, zero-valued deferred-integration counters, blocked/failure styling in
the feed, and terminal/hygiene surfaces in one layout. Narrow-width layout is
**unverified** for T4; only a desktop screenshot path is listed for local review
and remains gitignored.

![Hosted workspace with activity counters and recent feed](../../../../screenshots/sdd_t4_workspace_desktop.png)

## Artifact: Quality and privacy gates

**What it proves:** Full repository quality gates pass before staging.

**Commands:**

~~~bash
npm run build
npm run typecheck
npm run lint
./scripts/privacy-check.sh
git diff --check
~~~

**Result summary:** .NET and dashboard builds completed without errors, privacy
check passed, and the diff contained no whitespace errors.

~~~text
Build succeeded. 0 Warning(s). 0 Error(s).
privacy-check: passed
~~~

## Reviewer Conclusion

T4 delivers a privacy-safe immutable activity read path with recursive metadata
redaction, UTC-ms local-day bucketing, SQL-bounded feed retrieval and counter
aggregation, corrected `securityBlocked` semantics, isolated telemetry recording
with `auditDegraded` surfacing, terminal failure emission, and a dashboard
activity surface that stays visible when the terminal is unhealthy and formats
times in the activity timezone. Providerless C# and React evidence passes;
hosted proof contains only sanitized fixture/runtime metadata.
