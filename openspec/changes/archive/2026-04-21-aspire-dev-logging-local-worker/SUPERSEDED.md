# Superseded

This change is superseded in part by `seq-replaces-plg`.

## Landed (Tasks 1–4, 8, 9)

- Task 1 — AppHost worker-image spike (`worker:dev` tag strategy) — landed.
- Task 2 — server OTLP logging reorder (`ClearProviders` before `AddServiceDefaults`) — landed.
- Task 3 — AppHost builds worker from this repo in every profile — landed.
- Task 4 — AppHost tests asserting no-GHCR invariant — landed.
- Task 8 — documentation — landed.
- Task 9 — verification — landed.

These remain valuable and are not reverted by `seq-replaces-plg`.

## Superseded (Tasks 5–7)

- Task 5 — Alloy spike to replace Promtail — **superseded**. The PLG stack is
  being removed entirely in favour of Seq.
- Task 6 — Land Alloy replacement — **superseded**. Not pursued.
- Task 7 — Fallback: fix Promtail on macOS + label every container — partially
  landed earlier but **superseded going forward**. Labels and Promtail config
  are deleted by `seq-replaces-plg`.

## Archival

This change SHOULD remain in `openspec/changes/` with its tasks ticked as
complete (9/9 after accounting for supersession) until `seq-replaces-plg`
lands. After `seq-replaces-plg` is merged, archive this change folder.
