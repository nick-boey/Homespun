## Why

Every span name in Homespun is a contract with whoever queries Seq or the Aspire dashboard. Without an enforced catalogue, names drift: one engineer writes `homespun.a2a.emit`, another writes `worker.a2a.send`, dashboards break silently. The existing `SessionEventLogEntry` hop dictionary solves this for logs today; spans need the same discipline.

A single file — `docs/traces/dictionary.md` — is both developer-facing reference (grep for a span name → find its definition) and CI-enforceable: a test parses H3 headers, greps the codebase for `StartActivity(name)` / `tracer.startSpan(name)`, and fails when names diverge. This change establishes both the document and the drift check.

## What Changes

- **Create `docs/traces/dictionary.md`** with the seed content drafted during exploration: conventions, tracer/ActivitySource registry, section-per-span organised by originator tier (client / server / worker), attribute taxonomy (stable OTel semconv + `gen_ai.*` + `homespun.*`).
- **Add a drift check** in each tier's test suite:
  - `tests/Homespun.Tests/Observability/TraceDictionaryTests.cs` — parses the dictionary, collects H3 section names, scans `src/Homespun.Server/**/*.cs` for `StartActivity("…")` / `ActivitySource` calls, asserts every name appears in the dictionary (and vice-versa — orphan section detection).
  - `src/Homespun.Web/src/test/trace-dictionary.test.ts` — mirror for web: scans for `tracer.startSpan("…")` / `startActiveSpan("…")`.
  - `src/Homespun.Worker/src/test/trace-dictionary.test.ts` — mirror for worker.
- **Document the update workflow** in `docs/traces/README.md` (short): add a span → update dictionary in the same PR, else CI fails.
- **Seed the registry** with every ActivitySource / tracer name present TODAY: `Homespun.AgentOrchestration`, `Homespun.GitClone`, `Homespun.FleeceSync` (plus `Homespun.Signalr` + `Homespun.SessionPipeline` once sibling changes land). This change lands with the current span surface documented, then subsequent changes extend the document.

## Capabilities

### Modified Capabilities
- `observability` — governance of span / tracer naming and attributes.

## Impact

- **Files:**
  - `docs/traces/dictionary.md` — new, large (~300 lines seed).
  - `docs/traces/README.md` — new, short (update workflow).
  - `tests/Homespun.Tests/Observability/TraceDictionaryTests.cs` — new.
  - `src/Homespun.Web/src/test/trace-dictionary.test.ts` — new.
  - `src/Homespun.Worker/src/test/trace-dictionary.test.ts` — new.
  - `.github/workflows/*.yml` — verify existing test jobs run the new tests (no new workflow).

- **Dependencies:** none.

- **Risk surface:**
  - False positives / negatives in the drift check's regex over source files. Mitigation: tests use string-matching the ActivitySource name literal, not parsing C# AST. Dynamic span names (interpolated strings) must be listed explicitly in an exemption list inside the test.
  - Overly strict coupling: every exploratory span addition now requires doc update. Intended — trade-off accepted.

- **Rollback:** revert. Dictionary file + drift check removed. No runtime impact.
