## Context

OpenSpec changes live on feature branches, not main. Homespun today reads a single clone's working directory and surfaces a Fleece-driven issue graph. To integrate OpenSpec meaningfully we must cross-reference branches with their fleece-issue owners, show change progress in the graph, and drive stage dispatches from the run-agent panel.

The existing branch-naming convention `feat/<name>+<fleece-id>` already carries enough information to determine issue ownership. OpenSpec's CLI (`openspec status --change X --json`) already computes artefact readiness, so Homespun does not need a YAML schema parser or DAG evaluator — it shells out.

Experimental findings (2026-04-16):
- Skill set is fixed across schemas; custom schemas parameterise what skills do, not which skills exist.
- `openspec update --force` regenerates `SKILL.md` but preserves sibling files in the skill directory (so sidecars are safe).
- `openspec status --change <name> --json` returns `{changeName, schemaName, isComplete, applyRequires, artifacts[{id, outputPath, status}]}`.

## Goals / Non-Goals

**Goals:**
- Two-way linking between Fleece issues and OpenSpec changes via a sidecar file, not naming conventions or tags.
- Branch-by-branch scanning visible in the issue graph, on-demand and post-session.
- Stage-aware run-agent panel ("OpenSpec" tab) that auto-selects the next ready stage and dispatches the corresponding skill.
- Graceful orphan handling for changes without sidecars.

**Non-Goals:**
- Schema YAML parsing. Homespun shells out to `openspec status`.
- Bare-mirror / git-cat-file scanning — deferred. Phase one reads from on-disk clones only.
- Real-time fs watchers or SignalR change broadcasts. Post-session snapshot + on-demand scan is enough.
- Promote-without-dispatch UI. There is no standalone "Promote" button; promotion happens implicitly when an agent dispatched from the OpenSpec tab produces `openspec/changes/<name>/` on the branch.
- Non-agent automation in schema (CI merge, PR open, etc.). Agents handle these inside their task lists, or GitHub auto-merge.

## Decisions

### D1: Sidecar is the source of truth for linkage

**Decision:** Each OpenSpec change gets a sibling file `openspec/changes/<name>/.homespun.yaml` containing `fleeceId: <id>` and `createdBy: server|agent`. The scanner uses this + the branch-name fleece-id suffix to determine ownership.

**Rationale:** The branch can carry multiple in-flight changes (some inherited from main, not yet archived). Presence alone cannot identify "the change this branch is working on." A sidecar provides an explicit pointer that survives `openspec archive` (the sidecar is moved along with the change directory into `openspec/changes/archive/<dated>-<name>/`).

### D2: Server auto-writes sidecar post-session when missing

**Decision:** When a session ends and the snapshot shows a new orphan change on the branch, the server writes `.homespun.yaml` with `{fleeceId: <branch-id>, createdBy: agent}` automatically, provided exactly one orphan candidate exists. If multiple, the UI prompts the user to disambiguate.

**Rationale:** Keeps the agent skills minimal — agents don't need discipline to call `fleece edit` with a tag. The server reconciles based on the branch-name invariant.

### D3: Snapshot push from worker, not fs watcher

**Decision:** At session end, the worker runs the change scanner on the branch's clone, composes a snapshot, and POSTs it to the server. Server caches snapshots keyed by `(projectId, branch)`. No inter-container fs watcher.

**Rationale:** The only meaningful change surface is what agents produce; post-session is the natural integration point. Avoids inter-container filesystem plumbing. Cache invalidation is implicit (next session overwrites).

### D4: On-demand scan as cache fallback

**Decision:** When the UI requests the graph and a snapshot is absent or stale beyond a TTL (60s), the server performs a live scan against the on-disk clone.

**Rationale:** Covers the case where a user externally ran `openspec propose` in a clone without going through a managed session. TTL prevents hammering disk on rapid graph re-fetches.

### D5: Stage panel backed by `openspec status` with simple auto-selection

**Decision:** The OpenSpec tab shells out to `openspec status --change <name> --json` to get artefact state. All 8 OpenSpec skills (identified by `metadata.author == "openspec"` in frontmatter) are always available in the tab. Auto-selection logic:
1. No change or artifacts incomplete → default `openspec-explore`
2. All schema artifacts created → default `openspec-apply-change`
3. All tasks in tasks.md checked → default `openspec-archive-change`

Schema selection: if the project uses a non-default schema, inject `"use openspec schema '{schema}' for all openspec commands"` into the session's system prompt. Schema name read from `openspec/config.yaml` on the branch clone.

Dispatch: app reads skill SKILL.md body + appends change name as args → sends as initialMessage.

**Rationale:** The skill set is fixed at 8 regardless of schema (confirmed 2026-04-16). The three-tier auto-selection (explore → apply → archive) covers the common progression. Users can override by selecting any other skill manually. No DAG evaluation needed on the Homespun side.

### D6: Change symbol is five-state, mapped to auto-selection tiers

**Decision:** The per-row change indicator encodes the same state used by auto-selection:

| State | Detection | Symbol | Colour |
|---|---|---|---|
| No change | no openspec/changes/<name>/ on branch | — | — |
| Change created, incomplete | change exists, artifacts incomplete | ◐ | red |
| Ready to apply | all schema artifacts created | ◐ | amber |
| Ready to archive | all tasks checked | ● | green |
| Archived | change in archive dir | ✓ | blue |

**Rationale:** Because auto-selection uses a fixed three-tier progression (explore → apply → archive) that doesn't vary by schema, the symbol set is also fixed. The red/amber split distinguishes "still proposing/designing" from "ready to implement", which is a meaningful signal in the graph.

### D7: Multi-change per branch is allowed

**Decision:** A branch may host multiple OpenSpec changes. Each carries its own `.homespun.yaml`. Sidecars that point to the branch's fleece-id appear as sibling nodes under the issue; sidecars pointing elsewhere are treated as "inherited from main", hidden.

**Rationale:** Real workflow: an agent may decide to split a proposal into several changes on the same branch if they're tightly related. If the agent decides to split into sub-issues instead, it creates the sub-issues (each with its own branch) and re-links changes accordingly.

### D8: Archive preserves linkage

**Decision:** When a change archives (moves to `openspec/changes/archive/<date>-<name>/`), its sidecar moves with it. The scanner checks the archive location when the live change disappears. The fleece issue auto-transitions to `complete` at that point.

**Rationale:** Linkage is durable; history is queryable. "Completed change" gets a distinct visual (muted + check).

### D9: OpenSpec skills are hard-coded, not sidecar-augmented

**Decision:** The 8 OpenSpec skills are identified by `metadata.author == "openspec"` in their SKILL.md frontmatter. Homespun hard-codes their names and knows their args shapes (e.g., `openspec-apply-change` takes a change name). No `.homespun.yaml` sidecar needed for OpenSpec skills — only for custom Homespun prompt skills.

**Rationale:** OpenSpec skills are a fixed, stable set (confirmed 2026-04-16). Their args are simple and well-known. Adding sidecars to them would just be restating what we already know. The sidecar mechanism exists for user-authored custom skills whose shapes Homespun can't predict.

## Risks / Trade-offs

- **[R1]** On-disk scanning only covers branches with active clones. Branches fetched-but-not-cloned are invisible to the graph. Phase two introduces a bare-mirror reader.
- **[R2]** The scanner depends on `openspec` CLI being available in the server container. Ship it in the server image.
- **[R3]** Sidecar collisions: if a user manually writes `.homespun.yaml` pointing to the wrong issue, it's the user's error. No validation beyond "does this fleece id exist."
- **[R4]** Agent may forget to create a sidecar during custom skill dispatches. D2 covers the simple case (exactly one orphan). Multi-orphan ambiguity requires UI resolution.
- **[R5]** Phase-dispatch pre-flight (refusing `/openspec-apply-change` when prior phases incomplete) is deferred to fleece issue `wA0N2U`. Initial version allows out-of-order dispatch.

## Resolved Questions

- **OQ1 (resolved):** The OpenSpec tab always respects readiness — no "advance anyway" override. This only applies to `apply`, `verify`, `sync`, and `archive` (which have artifact/task prerequisites). `explore`, `propose`, `new-change`, and `continue-change` are always available. The Task Agent tab serves as the escape hatch for arbitrary dispatch.
- **OQ2 (resolved):** Abandoned branches simply unlink. No "formerly linked" history in the graph. Archived changes on main provide the completion record; deleted branches leave no trace.
- **OQ3 (resolved):** Keep orphan-change surfacing for robustness. Users running the OpenSpec CLI outside Homespun can create changes that lack sidecars. Cheap to implement, prevents invisible state.
- **OQ4 (resolved):** Acceptable. Schema changes across branches are rare, and `openspec status` runs inside the branch's clone (picking up the branch's schema). No special handling needed.
