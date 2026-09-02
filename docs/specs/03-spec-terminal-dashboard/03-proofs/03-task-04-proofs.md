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
  states, blocked/failure styling, and independent refresh.

## Evidence Summary

- `dotnet test PersonalAssistant.sln`: 94 harness tests and 25 server tests
  passed.
- Dashboard Vitest: 7 test files and 24 tests passed; typecheck, lint, and
  production Vite build passed.
- `./scripts/privacy-check.sh` and `git diff --check` passed.
- Hosted ASP.NET proof on `/agents/personal` showed the terminal workspace,
  activity counters with zero states, and a recent feed without provider
  credentials or transcript content.

## Artifact: Activity aggregation and privacy tests

**What it proves:** Seeded immutable events demonstrate exact counters, midnight
boundaries, stable ordering, zero-valued future categories, metadata redaction,
and no mutation on refresh.

**Why it matters:** The feed must be trustworthy without inferring integration
success from settings cards.

**Command:**

~~~bash
dotnet test PersonalAssistant.sln --filter 'FullyQualifiedName~ActivityQueryServiceTests'
~~~

**Result summary:** All focused harness activity tests passed.

~~~text
Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6
~~~

## Artifact: Versioned activity API

**What it proves:** `GET /api/activity` returns versioned JSON, explicit zero
counters, bounded recent events, blocked/failure statuses, and redacted metadata.

**Why it matters:** The dashboard consumes a stable contract separate from the
terminal WebSocket.

**Command:**

~~~bash
dotnet test tests/PersonalAssistant.Server.Tests/PersonalAssistant.Server.Tests.csproj --filter FullyQualifiedName~ActivityApiTests
curl -s "http://127.0.0.1:4325/api/activity?timezone=UTC"
~~~

**Result summary:** API tests passed. Live proof JSON included every counter
key with zero defaults plus a safe `terminal_state` event containing only
`eventType`, `outcome`, and `state` metadata.

~~~json
{
  "contractVersion": "phase-0c-activity.v1",
  "date": "2026-09-02",
  "timezone": "UTC",
  "counters": {
    "promptsDelivered": 0,
    "agentStarts": 1,
    "securityBlocked": 0
  }
}
~~~

## Artifact: Dashboard activity panel tests

**What it proves:** The React panel renders local-day labels, zero counters,
blocked labels, empty states, errors, and independent refresh without coupling
to the terminal WebSocket.

**Commands:**

~~~bash
npm ci --prefix apps/dashboard
npm --prefix apps/dashboard test
npm --prefix apps/dashboard run typecheck
npm --prefix apps/dashboard run lint
npm --prefix apps/dashboard run build
~~~

**Result summary:** 24 dashboard tests passed across 7 files, including four new
`ActivityPanel` cases.

~~~text
Test Files 7 passed (7)
Tests 24 passed (24)
~~~

## Artifact: Hosted browser proof

**What it proves:** The ASP.NET-served `/agents/personal` route renders the
canonical terminal beside the activity summary with local-day labeling and zero
states at desktop and narrow widths.

**Why it matters:** This confirms the integrated control-room surface rather
than isolated API or component behavior.

**Artifact path:** `screenshots/sdd_t4_workspace_desktop.png`

**Result summary:** The hosted workspace showed the activity panel with local-day
labeling, zero-valued deferred-integration counters, blocked/failure styling in
the feed, and terminal/hygiene surfaces in one layout. Screenshot capture was
limited to the hosted proof session; the artifact path is listed for local
review and remains gitignored.

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

T4 delivers a privacy-safe immutable activity read path, terminal/hygiene event
emission at the SQLite boundary, a versioned API with explicit zero counters,
and a dashboard activity surface that refreshes independently from the terminal
WebSocket. Providerless C# and React evidence passes, and hosted proof contains
only sanitized fixture/runtime metadata.
