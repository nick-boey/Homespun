# Upgrade note — A2A-native session messaging

The session messaging pipeline was rewritten so the worker is the only component
that speaks the native Claude SDK format. The server now stores A2A events
verbatim and serves a single AG-UI envelope stream to clients — live (via
SignalR `ReceiveSessionEvent`) and on refresh (via `GET /api/sessions/{id}/events`)
produce identical envelope sequences.

## What you need to know on upgrade

- **Existing session caches are reset.** The old `ClaudeMessage` JSONL files are
  no longer readable by the new code. A startup hosted service deletes the
  contents of `{SessionCache:BaseDirectory}` on first boot after this upgrade
  and logs a warning with the file count.
- **Restart any in-progress agents.** Sessions that were mid-turn when the
  upgrade landed cannot be replayed from the new event log (it starts fresh);
  terminate and re-create them.
- **Escape hatch:** set `HOMESPUN_SKIP_CACHE_PURGE=true` to skip the purge if
  you want to keep the pre-upgrade JSONL files on disk for forensic inspection.
  The new code still will not read them.

## Configuration

`appsettings.json` now includes a `SessionEvents` section:

```json
"SessionEvents": {
  "ReplayMode": "Incremental"
}
```

`Incremental` is the default and the hot path — `GET /events?since=N` returns
only events with `seq > N`. Set to `"Full"` as a kill switch if incremental
replay ever produces a gap: the server will then ignore `since` and return the
full event log from seq 1. The client deduplicates by `eventId`, so toggling to
`Full` does not corrupt client state. Clients may override per-request via
`?mode=full` (or `?mode=incremental`).

## Behavioural changes visible to operators

- Session lifecycle states (`Starting`, `Running`, `WaitingForInput`, etc.) are
  unchanged — only the event *content* flowing to the UI changes.
- The REST endpoints `/api/sessions/{id}/cache/messages`,
  `/api/sessions/{id}/cache/summary`, `/api/sessions/cache/project/{id}`, and
  `/api/sessions/cache/entity/{project}/{entity}` are gone. Any external tooling
  that polled them needs to move to `/api/sessions/{id}/events` (see
  `docs/session-events.md` for the envelope contract).

For deeper background see `openspec/changes/a2a-native-messaging/` — proposal,
design, and per-capability specs.
