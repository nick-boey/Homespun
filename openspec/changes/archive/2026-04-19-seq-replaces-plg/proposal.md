## Why

Dev + prod logging today splits across two paths: .NET OpenTelemetry (server → Aspire dashboard via OTLP) and Grafana/Loki/Promtail (PLG) via JSON stdout + docker_sd scraping. The split forces two schemas (JsonConsoleFormatter shape + OTLP), two label conventions (LogQL `service` label vs OTLP `service.name` resource attribute), two troubleshooting surfaces. Promtail's macOS `/var/lib/docker/containers` dependency was patched in `aspire-dev-logging-local-worker` but still leaks into config complexity.

Consolidating on Seq — a single events-first store that natively ingests OTLP — cuts the stack to: .NET OTel exporter → Seq (dev + prod) + Aspire dashboard (dev only). One wire format. One schema. Seq's event-store model means high-cardinality attributes (`homespun.session.id`, `homespun.issue.id`) cost nothing — Loki's label-cardinality tax was the hidden friction. Free tier retains 7 days unlimited events, sufficient for this project's scale.

This change is the foundation for four sibling changes (`server-otlp-proxy`, `worker-otel`, `client-otel`, `session-event-log-to-spans`, `trace-dictionary`) that assume a working OTLP sink pair. Ships first.

## What Changes

- **Add Seq to AppHost and compose.** Dev: `Aspire.Hosting.Seq` 9.5.x, anonymous auth, persistent container lifetime. Prod: `datalust/seq:2024.3` service in `docker-compose.yml` with `X-Seq-ApiKey` via Komodo env. Data volume uses a Docker named volume on the OS disk — migration to a dedicated Azure managed data disk is a follow-up.
- **Dual OTLP export in `Homespun.ServiceDefaults`.** Replace the monolithic `UseOtlpExporter()` call with explicit per-signal `AddOtlpExporter()` calls (Aspire dashboard leg) plus `AddSeqEndpoint("seq")` via `Aspire.Seq` 13.1.x (Seq leg). Logs + traces flow to both; metrics stay on the Aspire leg only (Seq does not accept metrics; metrics observability deferred per user decision).
- **Enable native SignalR tracing** via `AddSource("Microsoft.AspNetCore.SignalR.Server")` — free with .NET 10. Scoped here because it directly affects what spans show up in Seq/Aspire; downstream changes rely on it being present.
- **Delete the PLG stack** from dev (AppHost) and prod (`docker-compose.yml`): Loki, Promtail, Grafana resources + `config/loki-config.yml`, `config/promtail-config.yml`, `config/grafana/` directory, `logging=promtail` labels.
- **Delete `JsonConsoleFormatter`** and its registration in `Homespun.Server/Program.cs` — dead weight now that OTLP export is the authoritative sink. Server stdout falls back to ASP.NET Core's default console formatter for local-only diagnostics.
- **Supersede the remaining 7 tasks of `aspire-dev-logging-local-worker`** (Promtail macOS fix + label plumbing) by dropping a `SUPERSEDED.md` note in that change's folder. Leave its 29 completed tasks landed; the next change archive cycle will clean it up.

## Capabilities

### New Capabilities
- `observability` — how Homespun emits, proxies, and sinks OpenTelemetry signals across server, worker, and client tiers. This change introduces the capability and covers the sink topology + dual OTLP export + native SignalR tracing.

### Modified Capabilities
- `dev-orchestration` — replaces the PLG container set with Seq; "Accessing Application Logs" guidance and related skill docs (`/logs` LogQL snippets) shift to Seq's query surface.

## Impact

- **Files touched:**
  - `src/Homespun.AppHost/Program.cs` — Loki/Promtail/Grafana block → Seq block.
  - `src/Homespun.AppHost/Homespun.AppHost.csproj` — +`Aspire.Hosting.Seq`.
  - `src/Homespun.ServiceDefaults/Extensions.cs` — rewrite OTLP wiring.
  - `src/Homespun.ServiceDefaults/Homespun.ServiceDefaults.csproj` — +`Aspire.Seq`.
  - `src/Homespun.Server/Program.cs` — delete `AddConsole` + formatter registration block.
  - `src/Homespun.Server/Features/Observability/JsonConsoleFormatter.cs` — DELETE.
  - `tests/Homespun.Tests/Features/Observability/JsonConsoleFormatterTests.cs` — DELETE.
  - `tests/Homespun.Api.Tests/Features/LoggingProviderStartupTests.cs` — assert Seq + Aspire OTLP exporters both registered.
  - `tests/Homespun.AppHost.Tests/AppHostTests.cs` — assert `seq` resource present, PLG resources absent, Seq endpoint referenced by every OTel-emitting project.
  - `docker-compose.yml` — PLG services + labels removed, `seq` service added.
  - `config/loki-config.yml`, `config/promtail-config.yml`, `config/grafana/**` — DELETE.
  - `.github/workflows/otlp-pipeline-smoke.yml` — Loki probe → Seq probe.
  - `CLAUDE.md` — observability section rewritten.
  - `openspec/changes/aspire-dev-logging-local-worker/SUPERSEDED.md` — new note.

- **Dependencies:** `Aspire.Hosting.Seq` 9.5.2 (AppHost), `Aspire.Seq` 13.1.2 (ServiceDefaults). No new JS deps.

- **Risk surface:**
  - `UseOtlpExporter()` + signal-specific `AddOtlpExporter` are incompatible (opentelemetry-dotnet#5538). The new ServiceDefaults wiring avoids `UseOtlpExporter` entirely; tests must assert both exporters export end-to-end at least once.
  - Any developer-local LogQL queries / saved dashboards die — none owned by this repo; flag in CLAUDE.md update.
  - Prod Seq data on a Docker named volume (OS disk) means VM rebuild = data loss. Acceptable until the managed-data-disk follow-up lands; flag in ops notes.

- **Rollback:** single revert restores PLG stack dev + prod. Seq's events are append-only; the Seq container simply won't start on the reverted code path. No schema migration.
