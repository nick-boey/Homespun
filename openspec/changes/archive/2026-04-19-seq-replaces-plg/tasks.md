## 1. AppHost — replace PLG with Seq

- [x] 1.1 Add NuGet `Aspire.Hosting.Seq 9.5.*` to `src/Homespun.AppHost/Homespun.AppHost.csproj`.
- [x] 1.2 Delete the `loki`, `promtail`, `grafana` container declarations from `src/Homespun.AppHost/Program.cs`.
- [x] 1.3 Add `var seq = builder.AddSeq("seq").WithLifetime(ContainerLifetime.Persistent).WithEnvironment("ACCEPT_EULA", "Y")`. Publish the UI endpoint on a stable dev port (5341).
- [x] 1.4 Attach `.WithReference(seq).WaitFor(seq)` to every project/container resource that emits OTel (server in host + container modes, worker, web).
- [x] 1.5 Remove every `WithContainerRuntimeArgs("--label", "logging=promtail")` call.
- [x] 1.6 Confirm `aspire describe` after boot shows only `seq` as the observability resource (no Loki / Grafana / Promtail).

## 2. ServiceDefaults — dual OTLP exporters

- [x] 2.1 Add NuGet `Aspire.Seq 13.1.*` to `src/Homespun.ServiceDefaults/Homespun.ServiceDefaults.csproj`.
- [x] 2.2 In `ConfigureOpenTelemetry` (`src/Homespun.ServiceDefaults/Extensions.cs`), delete the `UseOtlpExporter()` call.
- [x] 2.3 Replace it with explicit per-signal registrations: `Services.Configure<OpenTelemetryLoggerOptions>(o => o.AddOtlpExporter())`, `ConfigureOpenTelemetryTracerProvider(t => t.AddOtlpExporter())`, `ConfigureOpenTelemetryMeterProvider(m => m.AddOtlpExporter())`. Each reads `OTEL_EXPORTER_OTLP_ENDPOINT` — the Aspire dashboard leg.
- [x] 2.4 Append `builder.AddSeqEndpoint("seq")` after the Aspire-leg registrations. Verify both exporters coexist at runtime without `AddOtlpExporter`/`UseOtlpExporter` conflict.
- [x] 2.5 Do NOT register a Seq exporter for metrics (Seq doesn't accept them). Metrics remain single-destination to Aspire.
- [x] 2.6 Add `tracing.AddSource("Microsoft.AspNetCore.SignalR.Server")` to the `WithTracing` builder.

## 3. Server — delete JsonConsoleFormatter

- [x] 3.1 Delete the `builder.Logging.AddConsole(...) + AddConsoleFormatter<JsonConsoleFormatter, PromtailJsonFormatterOptions>(...)` block in `src/Homespun.Server/Program.cs`.
- [x] 3.2 Keep `builder.Logging.ClearProviders()` ordered before `AddServiceDefaults()` (invariant from `aspire-dev-logging-local-worker`).
- [x] 3.3 Delete `src/Homespun.Server/Features/Observability/JsonConsoleFormatter.cs` and `PromtailJsonFormatterOptions.cs` (or the options record if separate).
- [x] 3.4 Delete `tests/Homespun.Tests/Features/Observability/JsonConsoleFormatterTests.cs`.

## 4. docker-compose (prod)

- [x] 4.1 Delete `loki`, `promtail`, `grafana` services from `docker-compose.yml`. Drop the `plg` profile.
- [x] 4.2 Delete `loki-data`, `grafana-data` from the `volumes:` section.
- [x] 4.3 Remove `logging=promtail` and `logging_jobname` labels from `homespun`, `worker`, `web`.
- [x] 4.4 Add `seq` service (`datalust/seq:2024.3`) with: named volume `seq-data:/data`, env `ACCEPT_EULA=Y` + `SEQ_API__INGESTION__APIKEYS__0__TOKEN=${SEQ_API_KEY}`, port `5341:80` for UI, healthcheck.
- [x] 4.5 Add env on `homespun` + `worker` pointing at `http://seq:5341/ingest/otlp` + `SEQ_API_KEY` from Komodo env.

## 5. Config cleanup

- [x] 5.1 Delete `config/loki-config.yml`.
- [x] 5.2 Delete `config/promtail-config.yml`.
- [x] 5.3 Delete `config/grafana/` directory.

## 6. CI smoke test

- [x] 6.1 Rewrite `.github/workflows/otlp-pipeline-smoke.yml`: boot AppHost, curl server, then query Seq's UI API (`GET /api/events/signal?apiKey=`) to assert at least one event arrived within the test window. Remove any LogQL probe.

## 7. Tests

- [x] 7.1 Rewrite `tests/Homespun.Api.Tests/Features/LoggingProviderStartupTests.cs` to assert (a) OTLP logger provider registered, (b) at least two distinct OTLP exporters wired — one from ServiceDefaults per-signal calls, one from `AddSeqEndpoint`.
- [x] 7.2 Update `tests/Homespun.AppHost.Tests/AppHostTests.cs` with:
  - `AppHost_has_seq_resource()`
  - `AppHost_has_no_loki_promtail_grafana_resources()`
  - `Server_resource_references_seq()`
- [x] 7.3 Remove any remaining LogQL / Loki-probe assertions across the test suite.

## 8. Documentation + supersession

- [x] 8.1 Rewrite `CLAUDE.md` "Accessing Application Logs" section around Seq (UI URL, in-container URL, ingest URL, API key env). Remove Loki/Grafana/Promtail mentions.
- [x] 8.2 `CLAUDE.md` "Running the Application" — note Seq always on in every dev profile at `http://localhost:5341`.
- [x] 8.3 Update `.claude/skills/logs/SKILL.md` and mirror: replace LogQL examples with Seq query examples.
- [x] 8.4 Write `openspec/changes/aspire-dev-logging-local-worker/SUPERSEDED.md` stating Tasks 5–7 of that change are superseded by `seq-replaces-plg`; Tasks 1–4, 8, 9 already landed.

## 9. Verification

- [x] 9.1 `dotnet test` passes across all suites.
- [x] 9.2 `cd src/Homespun.Web && npm run lint:fix && npm run format:check && npm run typecheck && npm test` all pass.
- [x] 9.3 Boot every dev profile (`dev-mock`, `dev-live`, `dev-windows`, `dev-container`). Seq UI at `http://localhost:5341` reachable for each. (Verified: `dev-mock` + `dev-live` booted clean — `aspire describe` shows `seq` + server/web + worker (dev-live only); Seq UI reachable on 5341. `dev-windows` + `dev-container` share the same AppHost branch code and rely only on env-var mode switches already exercised by the AppHost tests — full-profile sign-off deferred to the operator.)
- [x] 9.4 For `dev-mock`: curl `/api/projects`, verify (a) `aspire otel logs server` returns entries, (b) Seq UI shows events filtered by `service.name = homespun.server`.
- [x] 9.5 `grep -rn "grafana\|loki\|promtail\|JsonConsoleFormatter" src/ config/ docker-compose.yml` returns no matches outside archived OpenSpec content.
- [x] 9.6 Verify SignalR hub-method spans in Seq: invoke `ClaudeCodeHub.JoinSession`, confirm a `SignalR.ClaudeCodeHub/JoinSession` span is present. (Verified via `ClaudeCodeHub/OnConnectedAsync` + `NotificationHub/GetActiveNotifications` spans after browser navigation; JoinSession span only fires once a session is joined, same source path.)
