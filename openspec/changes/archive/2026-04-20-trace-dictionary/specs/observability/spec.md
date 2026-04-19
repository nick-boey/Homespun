## ADDED Requirements

### Requirement: All emitted spans are documented in the trace dictionary

`docs/traces/dictionary.md` SHALL be the single source of truth for span names, tracer/ActivitySource names, and Homespun-namespaced attribute keys used by any tier.

#### Scenario: dictionary is present at the canonical location
- **WHEN** a developer opens `docs/traces/dictionary.md`
- **THEN** it contains sections for conventions, tracer registry, client traces, server traces, worker traces, and the drift-check description

#### Scenario: dictionary entries name originator and kind
- **WHEN** a developer reads any H3 entry naming a span
- **THEN** the entry names the originator tier (client / server / worker), span kind (SERVER / CLIENT / INTERNAL / PRODUCER / CONSUMER), and required + optional attributes

### Requirement: CI drift check enforces dictionary coverage

Each tier's test suite SHALL include a drift check that fails when span or tracer names in source code are missing from the dictionary, or vice-versa.

#### Scenario: undocumented span name fails CI
- **WHEN** a contributor adds `tracer.startSpan("homespun.new.span")` without adding the corresponding H3 entry to the dictionary
- **THEN** the tier's drift-check test fails with a message identifying the undocumented name and its source location

#### Scenario: orphan dictionary entry fails CI
- **WHEN** the dictionary lists an H3 entry for `homespun.defunct.span` that no code path in the corresponding tier emits
- **THEN** the tier's drift-check test fails with a message identifying the orphan

#### Scenario: dynamic span names are allowlisted
- **WHEN** a contributor uses an interpolated span name (e.g. `SignalR.{hub}/{method}`) that cannot be matched statically
- **THEN** an allowlist entry with a justifying comment exempts it from the drift check
