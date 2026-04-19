## 1. Dictionary authoring

- [x] 1.1 Create `docs/traces/dictionary.md` from the seed drafted in exploration. Sections: Conventions, Tracer/ActivitySource registry, Client-originated traces, Server-originated traces, Worker-originated traces, Drift check.
- [x] 1.2 For each currently-registered ActivitySource on the server (`Homespun.AgentOrchestration`, `Homespun.GitClone`, `Homespun.FleeceSync`), document at least one representative span with attributes even though its span name may not yet be pinned — note TBD where exploration hasn't reached the code.
- [x] 1.3 Document `Microsoft.AspNetCore.Hosting` and `System.Net.Http` native spans at a summary level (refer to stable semconv rather than redocumenting).
- [x] 1.4 Create `docs/traces/README.md`: add/remove/rename a span → update dictionary in the same PR; CI drift check enforces.

## 2. Drift check — server

- [x] 2.1 Create `tests/Homespun.Tests/Observability/TraceDictionaryTests.cs`.
- [x] 2.2 Locate the dictionary file at `docs/traces/dictionary.md` via a hardcoded relative path from the test assembly.
- [x] 2.3 Parse H3 headings that start with `` `homespun. `` or `SignalR.` or `http.` to collect documented span names. Also parse the Tracer registry table.
- [x] 2.4 Regex-scan `src/Homespun.Server/**/*.cs` for:
  - `ActivitySource(` → extract first string argument (source name).
  - `StartActivity(` → extract first string argument (span name).
  - `.AddEvent(new ActivityEvent("…")` → extract event name (optional: track separately).
- [x] 2.5 Assert: every ActivitySource name found in code is in the Tracer registry table.
- [x] 2.6 Assert: every span name found in code (`StartActivity` first arg) is an H3 heading in the dictionary.
- [x] 2.7 Support an exemptions list (`[SpanNameAllowlist]` const in the test) for interpolated/dynamic names; require a comment justifying each exemption.
- [x] 2.8 Test fails with an actionable message: "Span `…` used in file:line is not documented in docs/traces/dictionary.md".

## 3. Drift check — web

- [x] 3.1 Create `src/Homespun.Web/src/test/trace-dictionary.test.ts` (Vitest).
- [x] 3.2 Read the dictionary via `fs.readFileSync`, parse H3 sections.
- [x] 3.3 Scan `src/**/*.{ts,tsx}` (excluding `test/` + `*.test.*`) for `tracer.startSpan('…')` / `startActiveSpan('…')` / `getLogger('…')`.
- [x] 3.4 Assertions mirror the server test.
- [x] 3.5 Exemptions list parallel to server.

## 4. Drift check — worker

- [x] 4.1 Create `src/Homespun.Worker/src/test/trace-dictionary.test.ts` (Vitest).
- [x] 4.2 Same shape as web drift check, scanning `src/Homespun.Worker/src/**/*.ts`.
- [x] 4.3 Exemptions list parallel to server.

## 5. Verification

- [x] 5.1 `dotnet test` passes — dictionary tests green.
- [x] 5.2 `npm test` in web + worker passes.
- [x] 5.3 Negative test: temporarily add a `tracer.startSpan('undocumented.span')` in any tier, run the drift test, confirm it fails with the expected message. Revert.
- [x] 5.4 Manual: open `docs/traces/dictionary.md`, verify every H3 has originator + kind + required attributes listed.

## 6. Documentation

- [x] 6.1 Link to `docs/traces/dictionary.md` from `CLAUDE.md` observability section.
- [x] 6.2 Link from every `proposal.md` / `design.md` that introduces new spans (retroactively on sibling changes when they land).
