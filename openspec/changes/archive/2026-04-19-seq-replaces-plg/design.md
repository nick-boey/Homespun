## Context

Homespun dev + prod ran two parallel log paths before this change:

1. `.NET` OTLP → Aspire dashboard via `UseOtlpExporter()` in
   `Homespun.ServiceDefaults.Extensions.ConfigureOpenTelemetry`.
2. Grafana/Loki/Promtail (PLG) scraping container stdout via
   `docker_sd_configs` keyed on the `logging=promtail` label.

The split forced two schemas — `JsonConsoleFormatter`'s flat-JSON envelope
for the PLG path, OTLP attributes for the Aspire path — two label
conventions (`service` vs `service.name`), and two query surfaces (LogQL
vs the Aspire dashboard). Promtail's macOS `/var/lib/docker/containers`
dependency was patched in `aspire-dev-logging-local-worker` but still
leaked into config complexity.

Seq natively ingests OTLP and stores events with property indexing instead
of Loki's label-cardinality tax — high-cardinality attributes like
`homespun.session.id` / `homespun.issue.id` cost nothing. Free tier retains
7 days unlimited events. Consolidating on Seq cuts the stack to one wire
format (OTLP) and one schema.

This change is the foundation for four sibling changes
(`server-otlp-proxy`, `worker-otel`, `client-otel`,
`session-event-log-to-spans`, `trace-dictionary`) that all assume a working
OTLP sink pair. It ships first.

**Stakeholders:** single-operator project. Owner runs dev + prod.

**Constraints:**
- Dev must boot with `dotnet run --project src/Homespun.AppHost --launch-profile <dev-*>` — no extra manual steps.
- Prod deploys via `docker-compose.yml` under Komodo. Any new env var has to be provisioned in Komodo's variable store.
- macOS Docker Desktop has no `/var/lib/docker/containers` on the host FS, so any log pipeline must not depend on it.
- `opentelemetry-dotnet#5538`: `UseOtlpExporter()` and per-signal
  `AddOtlpExporter` cannot coexist — one shape or the other.

## Goals / Non-Goals

**Goals:**
- Single observability store (Seq) receives logs + traces from every tier
  in dev and prod.
- Aspire dashboard keeps working in dev for per-run investigation.
- Dev UX unchanged: `dotnet run … --launch-profile dev-mock` still boots
  everything needed.
- SignalR hub-method invocations show up as spans (free with .NET 10 via
  `Microsoft.AspNetCore.SignalR.Server` ActivitySource).
- PLG stack fully removed from both dev (AppHost) and prod (compose) — no
  half-migration state where Loki containers linger.
- Verification scripts (`dotnet test`, web test suite, `otlp-pipeline-smoke` CI job) continue to pass end-to-end.

**Non-Goals:**
- Metrics export to Seq. Seq doesn't accept OTLP metrics; metrics remain
  single-destination to the Aspire dashboard. A separate metrics sink is
  out of scope here.
- Worker (TypeScript) OTLP wiring. Handled by the `worker-otel` sibling
  change; this one stops at ensuring the `ConnectionStrings__seq` env var
  is injected into the worker container so `worker-otel` has a target.
- Client browser telemetry. Handled by `client-otel`.
- Seq auth hardening / retention tuning beyond the free-tier default.
- Migrating Seq's data volume to an Azure managed disk (Docker named
  volume on the OS disk is acceptable until the follow-up lands).

## Decisions

### 1. Seq via Aspire hosting integration, not raw container

**Choice:** use `Aspire.Hosting.Seq`'s `AddSeq("seq")` — an Aspire-native
resource builder — rather than declaring the Seq container via
`builder.AddContainer("seq", "datalust/seq", ...)` like the old Loki
block.

**Rationale:** `AddSeq` plus `.WithReference(seq)` on each consumer auto-
wires the `ConnectionStrings__seq` env var, which `Aspire.Seq`'s
`AddSeqEndpoint("seq")` in ServiceDefaults reads back. Equivalent raw-
container wiring would require per-consumer `WithEnvironment` lines
listing the same Seq URL in five places and would break when Aspire
reallocates the host port.

**Alternative considered:** raw `AddContainer("seq", "datalust/seq",
"2024.3")` + manual env wiring. Rejected — more lines, more drift risk.

### 2. Pin Seq UI port to 5341 with `IsProxied=false`

**Choice:** `AddSeq("seq", port: 5341)` + `.WithEndpoint("http", e =>
{ e.Port = 5341; e.TargetPort = 80; e.IsProxied = false; })`.

**Rationale:** Aspire's DCP by default wraps every container endpoint in
its reverse proxy with a dynamically allocated host port (we observed
`127.0.0.1:59834->80/tcp`). That breaks bookmarks, Playwright config, the
`/logs` skill examples, the `otlp-pipeline-smoke` CI probe, and
`CLAUDE.md` docs. Disabling the proxy pins `5341:80` directly in Docker.

**Alternative considered:** accept the dynamic port and read it via
service discovery. Rejected — every doc + script + skill would have to
resolve the port at query time; the constant cost outweighs the small
flexibility gain.

### 3. Persistent container lifetime for Seq in dev

**Choice:** `.WithLifetime(ContainerLifetime.Persistent)`.

**Rationale:** keeps Seq's event history across AppHost restarts during
an inner-dev loop, so a restart of the AppHost doesn't wipe the last
hour's traces. Matches Aspire's stated intent for long-lived
observability sinks.

**Consequence:** if the AppHost's Seq wiring changes (new port, new env),
the existing container must be removed manually (`docker rm -f seq-*`)
for the new config to take effect. Documented in the `/logs` skill.

### 4. Per-signal `AddOtlpExporter` + `AddSeqEndpoint` instead of `UseOtlpExporter`

**Choice:** inside `ConfigureOpenTelemetry`, register three per-signal
OTLP exporters explicitly:

```csharp
builder.Services.Configure<OpenTelemetryLoggerOptions>(o => o.AddOtlpExporter());
builder.Services.ConfigureOpenTelemetryTracerProvider(t => t.AddOtlpExporter());
builder.Services.ConfigureOpenTelemetryMeterProvider(m => m.AddOtlpExporter());
```

then append `builder.AddSeqEndpoint("seq")` (which registers another
OTLP exporter internally).

**Rationale:** `UseOtlpExporter()` installs a provider-wide exporter
that's incompatible with any second OTLP exporter in the same pipeline
(`opentelemetry-dotnet#5538`). Per-signal `AddOtlpExporter` is additive,
so Aspire's OTLP exporter and Seq's OTLP exporter coexist cleanly.

**Alternative considered:** keep `UseOtlpExporter()` for Aspire and emit
to Seq via a separate `ILogger` sink. Rejected — splits schemas again
(OTLP logs to Aspire, Serilog-shaped logs to Seq), defeats the whole
point of this change.

### 5. Guard `AddSeqEndpoint` behind connection-string presence

**Choice:**

```csharp
var seqEndpoint = builder.Configuration.GetConnectionString("seq");
if (!string.IsNullOrWhiteSpace(seqEndpoint))
{
    builder.AddSeqEndpoint("seq");
}
```

**Rationale:** `AddSeqEndpoint` throws `InvalidOperationException: Unable
to add a Seq health check because the 'ServerUrl' setting is missing.`
when the connection string isn't set. That happens in test hosts
(`HomespunWebApplicationFactory` doesn't boot Seq) and during cold-boot
failure modes. Guarding keeps the production path identical while
letting unit/integration tests run without Seq.

**Risk accepted:** an operator who forgets to wire `.WithReference(seq)`
silently loses the Seq leg. Mitigated by the new
`Server_resource_references_seq` AppHost test.

### 6. Pin `OTEL_SERVICE_NAME=homespun.<tier>` per resource

**Choice:** every OTel-emitting resource gets
`.WithEnvironment("OTEL_SERVICE_NAME", "homespun.<tier>")` —
`homespun.server`, `homespun.worker`, `homespun.web`.

**Rationale:** Aspire defaults `service.name` to the resource name
(`server`, `worker`, `web`). The `observability` spec explicitly
requires `service.name = homespun.server` for the Seq scenario. Pinning
the env var overrides Aspire's default and gives us a stable, Homespun-
scoped namespace for cross-tier trace-dictionary work in the follow-up
changes.

**Alternative considered:** let Aspire pick and rewrite every scenario /
query to use the short name. Rejected — breaks the spec contract and
leaks Aspire internals into user-facing dashboards.

### 7. Delete `JsonConsoleFormatter` entirely, don't soft-deprecate

**Choice:** full deletion — class, options type, registration in
`Program.cs`, and the `JsonConsoleFormatterTests` fixture.

**Rationale:** there is no remaining consumer. Loki is gone; Aspire
dashboard reads OTLP, not stdout JSON. Leaving it would be dead weight
that confuses future readers about whether stdout is authoritative.

### 8. Supersede `aspire-dev-logging-local-worker` tasks 5–7 via marker file

**Choice:** drop a `SUPERSEDED.md` note in the sibling change's folder
listing which tasks landed and which are superseded, rather than
rewriting that change's tasks.md or archiving it early.

**Rationale:** the sibling change is mid-implementation — 29/36 tasks
landed, all in production. Archiving it now would require back-filling
tasks.md with checkboxes for work that's already ancient. A marker file
makes the supersession traceable without rewriting history.

## Risks / Trade-offs

- **Seq's data volume is a Docker named volume on the OS disk in prod.**
  VM rebuild = data loss. → Mitigated by keeping Seq's retention at the
  7-day free-tier default (no large historical corpus to lose). Dedicated
  managed-data-disk migration is a follow-up outside this change's scope.

- **`UseOtlpExporter()` cannot coexist with `AddOtlpExporter()`.** →
  Mitigated by the per-signal rewrite (decision 4) and the new
  `LoggingProviderStartupTests.OpenTelemetryLoggerOptions_has_at_least_two_otlp_exporter_processors`
  test asserting ≥2 `IConfigureOptions<OpenTelemetryLoggerOptions>` are
  registered end-to-end.

- **Developer-local LogQL queries / saved Grafana dashboards all die.** →
  No LogQL queries / dashboards owned by this repo (grep confirms).
  External consumers: flagged in the `CLAUDE.md` "Accessing Application
  Logs" rewrite and the `.claude/skills/logs/SKILL.md` rewrite so the
  `/logs` skill surfaces Seq query syntax, not LogQL.

- **Aspire's DCP may start a stale `Persistent` Seq container from an
  earlier AppHost run whose port binding differs from the current code.**
  → Documented in the `/logs` skill: remove the stale container
  (`docker rm -f seq-*`) and re-boot. Encountered during implementation
  verification.

- **`AddSeqEndpoint` adds a Seq health check that polls the Seq URL.**
  When Seq is down or not wired (CI smoke, isolated tests), the health
  check pings a dead URL. → Guard added (decision 5) so the registration
  is skipped when the connection string isn't set.

- **`Aspire.Seq` version drift vs `Aspire.Hosting.Seq`.** Spec text asked
  for `13.1.*` / `9.5.*`. Implemented with `Aspire.Seq 13.2.0` to align
  with `Aspire.Hosting.AppHost 13.2.0` already pinned in the AppHost. →
  Runtime + tests + CI green; harmless drift, noted in verify report.

- **`OTEL_SERVICE_NAME` override is env-var-only — not a resource
  attribute in code.** If a consumer bypasses the AppHost wiring (e.g.
  runs `dotnet run --project src/Homespun.Server` directly), it falls
  back to the assembly name. → Acceptable; direct-run isn't a supported
  path per `CLAUDE.md`.

## Migration Plan

1. **Land this change on `feat/opentelemetry-seq`.** Single commit or
   coherent PR — the stack is atomic (AppHost, ServiceDefaults,
   docker-compose, configs, tests, docs move together).
2. **Dev rollout:** first boot of any `dev-*` profile pulls
   `datalust/seq` (~80 MB) and builds the persistent container. No
   manual cleanup required; stale Loki/Grafana/Promtail named volumes
   (`homespun-loki-data`, `homespun-grafana-data`) linger until the
   operator prunes — harmless (orphan volumes, no containers reference
   them).
3. **Prod rollout (Komodo):**
   - Add `SEQ_API_KEY` variable to Komodo's variable store.
   - Trigger the stack's auto-sync (`config/komodo/resources.toml`
     already sets `SEQ_PORT=5341` + `SEQ_API_KEY`).
   - First `docker compose up -d` pulls `datalust/seq:2024.3` and mounts
     the `seq-data` named volume.
   - Verify via the Tailscale gateway: `https://<ts-host>:5341`.
4. **Rollback:** single `git revert` of this change restores the PLG
   stack dev + prod. Seq's event log is append-only — the Seq container
   simply stops being addressed by the reverted code. No schema
   migration, no data cleanup needed.

## Open Questions

None outstanding. Follow-ups are separate sibling changes, not gaps in
this one.
