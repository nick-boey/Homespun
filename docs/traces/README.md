# Trace documentation

The [trace dictionary](./dictionary.md) is the single source of truth for
every span / tracer / logger name Homespun emits. A CI drift check across
all three tiers refuses to merge a PR whose code and dictionary are out of
sync.

## When you must update `dictionary.md`

Whenever you, in the same PR:

- Add a new `ActivitySource(name)` / `trace.getTracer(name)` /
  `logs.getLogger(name)` call site → add the name to the **Tracer /
  ActivitySource registry** table.
- Add a new `StartActivity("…")` / `tracer.startSpan("…")` /
  `tracer.startActiveSpan("…")` call site → add an H3 entry under the
  tier section for its originator (client / server / worker).
- Rename or remove any of the above → update or delete the corresponding
  entry.
- Add a span whose name is built from an interpolated string (dynamic) →
  add it to the tier test's dynamic-name allowlist and document the
  canonical shape under the relevant tier section.

## CI enforcement

Three drift tests — one per tier — parse `dictionary.md` and scan that
tier's source tree:

| Tier   | Test                                                                    |
|--------|-------------------------------------------------------------------------|
| Server | `tests/Homespun.Tests/Observability/TraceDictionaryTests.cs`            |
| Client | `src/Homespun.Web/src/test/trace-dictionary.test.ts`                    |
| Worker | `tests/Homespun.Worker/observability/trace-dictionary.test.ts`          |

Each test fails with a message pointing at the offending file + line
(e.g. `Span "homespun.new.thing" used in src/Homespun.Server/...:42 is
not documented in docs/traces/dictionary.md`). The companion directions —
dictionary → code (orphan detection) and registry → code (unregistered
tracer) — use the same message format.

## Planned spans

Entries under the dictionary's `Planned / reserved` H2 are reference only
and ignored by the drift check. Promote an entry up to a tier section in
the same PR that wires its first emit site.
