# Spike: how does Seq render `cmd.run` (and other span-emitted entries)?

**Date:** 2026-04-22
**Scope:** Tasks 1.1–1.7 of `improve-logs-prod-launch`. Validate the hypothesis in `design.md` that the Aspire-vs-Seq display delta is rendering-shaped (structured properties present-but-collapsed) and not delivery-shaped (data missing on the wire).

## Method

The spike was executed from inside the worker container with no docker socket. The host AppHost was already running and Seq was reachable at `host.docker.internal:5341`. The Aspire dashboard binds to localhost on the host and is **not** reachable from the worker container, so the Aspire screenshots (tasks 1.4 and 1.5) were not captured. The Seq evidence below is sufficient to validate the hypothesis on its own — see "Conclusion" for the reasoning.

The host AppHost was running with sustained `cmd.run` activity from `Homespun.Commands` (git invocations from the OpenSpec scanner / git graph builders). No new spans needed to be triggered manually.

Steps actually run:
1. Probed `host.docker.internal:5341` → reachable.
2. Pulled `GET /api/events/signal?count=200` from the Seq API to inspect raw event encoding.
3. Pulled `GET /api/events/signal?count=1000` to survey the breadth of templates and scopes.
4. Loaded `http://host.docker.internal:5341/#/events` in the Playwright browser, ran an empty query, captured `seq-list-view.png`.
5. Clicked the topmost `cmd.run` row to expand it, captured `seq-expanded-cmdrun.png`.

## Findings

### Wire format (from `/api/events/signal`)

Every `cmd.run` event in Seq has the same shape:

```json
{
  "MessageTemplateTokens": [{"Text": "cmd.run"}],
  "Properties": [
    {
      "Name": "cmd",
      "Value": {"duration_ms": 1, "exit_code": 0, "name": "git"}
    }
  ],
  "Scope": [{"Name": "name", "Value": "Homespun.Commands"}],
  "SpanKind": "Internal",
  "TraceId": "...",
  "SpanId": "..."
}
```

Two important details:
- The **message template is just the literal text `cmd.run`** — there is no placeholder.
- Span attributes (`cmd.name`, `cmd.exit_code`, `cmd.duration_ms`) are encoded as a single nested `cmd` property whose value is an object holding the three primitive sub-keys.

### Survey of the last 1,000 events in Seq

| Count | Template | Scope |
|---:|---|---|
| 865 | `cmd.run` | `Homespun.Commands` |
| 54  | `openspec.branch.resolve` | `Homespun.OpenSpec` |
| 53  | `openspec.enrich.node` | `Homespun.OpenSpec` |
| 13  | `openspec.state.resolve` | `Homespun.OpenSpec` |
| 2   | `graph.taskgraph.sessions` | `Homespun.Gitgraph` |
| 2   | `graph.taskgraph.prcache` | `Homespun.Gitgraph` |
| ... | (all other templates ≤ 2 each) | ... |
| **0** | (no log events with `SpanKind is null`) | — |

Of the last 1,000 entries, **every one was a span**. Zero were ILogger-emitted log events. That alone is a partial answer to the user's "Seq doesn't show what Aspire shows" — the comparison the user is making is between Aspire's trace view (which expands span attributes inline) and Seq's events list (which renders only the message template's text, identical for every span of the same name).

### Seq list view

`seq-list-view.png`: 50+ visible rows, each rendering as the bare text `cmd.run` followed by `2 ms`. No way to tell from the list view what command was run or against what repo.

### Seq expanded view

`seq-expanded-cmdrun.png`: same row clicked open. The expansion shows:

```
cmd.duration_ms       1
cmd.exit_code         0
cmd.name              git
name @Scope.name      Homespun.Commands
service.name          unknown_service:dotnet
telemetry.sdk.*       (resource attrs)
```

All three `cmd.*` attributes are **present** in the event payload — they are flattened by Seq's expand-view from the nested `cmd` object into three rows. The user's experience of "Seq just shows the event name without the full value of the event message even when expanded" is **partially** accurate: the values are there, but hidden behind a click. The expansion does show them.

## Conclusion

**The design hypothesis holds.** The Seq vs. Aspire display delta is rendering-shaped, not delivery-shaped:

1. Span attributes reach Seq intact (proven by both API JSON and the expanded UI).
2. Seq's list view renders only the `MessageTemplateTokens` text. For spans, that text is the bare span name.
3. To get values to appear inline in Seq's list view, the emitter must use a **rendered Serilog message template** — i.e., a log entry whose template includes `{Placeholder}` slots that get expanded into the rendered message. Spans, by virtue of how the OTel→Seq bridge encodes them, cannot have rendered templates: the template is fixed to the span name.

This validates the design in two ways:

- **The full-body-logging path of the change is correct.** The new `a2a.rx`, `agui.translate`, `agui.tx` log sites emit via `ILogger.LogInformation(...)` with rendered templates like `"a2a.rx kind={Kind} body={Body}"`. These will appear in Seq's list view with their values rendered inline — the gap fixed for the new log sites.

- **The Seq-friendly-templates requirement is not redundant.** Without the rendered-template convention the new log sites would inherit the same one-line-of-bare-text-only display problem.

**One nuance worth recording for the implementation:** existing spans (`cmd.run`, `homespun.session.ingest`, `homespun.a2a.emit`, etc.) are not addressed by this change. Seq's list view will continue to show their bare names. The user can still drill into a span by clicking it, and Aspire's trace view will continue to surface attributes inline. Retrofitting span emitters to also emit a paired log entry is **out of scope** for this change — it's listed under design.md "Open Questions" and can be picked up as a follow-up if the new log sites prove the pattern works well.

## Aspire dashboard caveat

The spike did not directly observe how the Aspire dashboard renders a `cmd.run` span. The dashboard is not reachable from the worker container. The "Aspire shows the values inline" claim is a user observation reported in conversation. It is consistent with the OpenTelemetry .NET dashboard's documented behaviour of expanding span tags inline in its trace view. If implementation reveals a different rendering, the design's "Seq-friendly templates" requirement still stands on its own (Seq's display behaviour is the load-bearing constraint).

## Artefacts

- `seq-list-view.png` — 50 indistinguishable `cmd.run` rows
- `seq-expanded-cmdrun.png` — same after expanding one row, attributes visible
