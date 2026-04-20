/**
 * Post-session OpenSpec hook. At session end, scans `openspec/changes/` on the
 * branch clone, composes a `BranchStateRequest` payload matching the server
 * contract, and POSTs it to `${HOMESPUN_SERVER_URL}/api/openspec/branch-state`.
 *
 * Keep this module dependency-free (no YAML lib) so it stays easy to unit test
 * and ships with a minimal runtime footprint. The server is the source of truth
 * for any richer scanning (artifact state via `openspec status`, archive matching).
 */
import { readdir, readFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import path from "node:path";

import { info as logInfo, warn as logWarn } from "../utils/otel-logger.js";

export interface Sidecar {
  fleeceId: string;
  createdBy: string;
}

export interface ChangeTasksState {
  tasksDone: number;
  tasksTotal: number;
  nextIncomplete: string | null;
  phases: Array<{ name: string; done: number; total: number }>;
}

export interface LinkedChangePayload {
  name: string;
  createdBy: string;
  isArchived: boolean;
  archivedFolderName: string | null;
  tasksDone: number;
  tasksTotal: number;
  nextIncomplete: string | null;
  phases: Array<{ name: string; done: number; total: number }>;
}

export interface OrphanPayload {
  name: string;
  createdOnBranch: boolean;
}

export interface BranchStateRequest {
  projectId: string;
  branch: string;
  fleeceId: string;
  changes: LinkedChangePayload[];
  orphans: OrphanPayload[];
}

export interface ScanResult {
  linked: LinkedChangePayload[];
  orphans: OrphanPayload[];
}

const SIDECAR_NAME = ".homespun.yaml";
const CHANGES_SUBPATH = path.join("openspec", "changes");
const ARCHIVE_SUBPATH = path.join("openspec", "changes", "archive");

/**
 * Parses a `.homespun.yaml` document using a line-oriented extractor.
 * Intentionally forgiving: requires `fleeceId` and `createdBy` keys at
 * top-level, ignores unknown keys, tolerates optional quotes.
 */
export function parseSidecarYaml(yaml: string): Sidecar | null {
  let fleeceId: string | null = null;
  let createdBy: string | null = null;

  for (const rawLine of yaml.split("\n")) {
    const line = rawLine.replace(/\r$/, "").trim();
    if (!line || line.startsWith("#")) continue;
    const match = /^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$/.exec(line);
    if (!match) continue;
    const key = match[1];
    const raw = match[2].trim().replace(/^["']|["']$/g, "");
    if (key === "fleeceId") fleeceId = raw;
    else if (key === "createdBy") createdBy = raw;
  }

  if (!fleeceId || !createdBy) return null;
  return { fleeceId, createdBy };
}

export async function readSidecar(changeDir: string): Promise<Sidecar | null> {
  const p = path.join(changeDir, SIDECAR_NAME);
  if (!existsSync(p)) return null;
  try {
    return parseSidecarYaml(await readFile(p, "utf8"));
  } catch (err) {
    logWarn(`Failed to read sidecar ${p}: ${String(err)}`);
    return null;
  }
}

/**
 * Parses tasks.md checkbox state, grouped by `## Heading` blocks.
 * Lines like `- [x] Description` count as done; `- [ ] Description` as pending.
 */
export function parseTasksMd(content: string): ChangeTasksState {
  const phases: Array<{ name: string; done: number; total: number }> = [];
  let current: { name: string; done: number; total: number } | null = null;
  let tasksDone = 0;
  let tasksTotal = 0;
  let nextIncomplete: string | null = null;

  for (const rawLine of content.split("\n")) {
    const line = rawLine.replace(/\r$/, "");
    const phaseMatch = /^##\s+(.+?)\s*$/.exec(line);
    if (phaseMatch) {
      current = { name: phaseMatch[1], done: 0, total: 0 };
      phases.push(current);
      continue;
    }
    const taskMatch = /^\s*-\s+\[([ xX])\]\s+(.+?)\s*$/.exec(line);
    if (!taskMatch) continue;

    const isDone = taskMatch[1] === "x" || taskMatch[1] === "X";
    const desc = taskMatch[2];

    tasksTotal += 1;
    if (isDone) tasksDone += 1;
    else if (nextIncomplete === null) nextIncomplete = desc;

    if (current === null) {
      current = { name: "(unnamed)", done: 0, total: 0 };
      phases.push(current);
    }
    current.total += 1;
    if (isDone) current.done += 1;
  }

  return {
    tasksDone,
    tasksTotal,
    nextIncomplete,
    phases: phases.filter((p) => p.total > 0),
  };
}

async function readTasksState(changeDir: string): Promise<ChangeTasksState> {
  const p = path.join(changeDir, "tasks.md");
  if (!existsSync(p)) {
    return { tasksDone: 0, tasksTotal: 0, nextIncomplete: null, phases: [] };
  }
  try {
    return parseTasksMd(await readFile(p, "utf8"));
  } catch {
    return { tasksDone: 0, tasksTotal: 0, nextIncomplete: null, phases: [] };
  }
}

/**
 * Strips a leading `YYYY-MM-DD-` prefix from an archive folder name.
 */
function stripDatePrefix(name: string): string {
  if (name.length <= 11 || name[10] !== "-") return name;
  const prefix = name.slice(0, 10);
  if (/^\d{4}-\d{2}-\d{2}$/.test(prefix)) return name.slice(11);
  return name;
}

/**
 * Scans `openspec/changes/*` and `openspec/changes/archive/*` under the given
 * clone root, returning changes linked to `branchFleeceId` plus any sidecar-less
 * orphans. Inherited changes (sidecar points elsewhere) are excluded.
 */
export async function scanChangesDirectory(
  workingDir: string,
  branchFleeceId: string,
): Promise<ScanResult> {
  const linked: LinkedChangePayload[] = [];
  const orphans: OrphanPayload[] = [];

  const changesRoot = path.join(workingDir, CHANGES_SUBPATH);
  if (existsSync(changesRoot)) {
    const entries = await readdir(changesRoot, { withFileTypes: true });
    for (const entry of entries) {
      if (!entry.isDirectory() || entry.name === "archive") continue;
      const changeDir = path.join(changesRoot, entry.name);
      const sidecar = await readSidecar(changeDir);

      if (!sidecar) {
        orphans.push({ name: entry.name, createdOnBranch: false });
        continue;
      }
      if (sidecar.fleeceId !== branchFleeceId) continue;

      const tasks = await readTasksState(changeDir);
      linked.push({
        name: entry.name,
        createdBy: sidecar.createdBy,
        isArchived: false,
        archivedFolderName: null,
        tasksDone: tasks.tasksDone,
        tasksTotal: tasks.tasksTotal,
        nextIncomplete: tasks.nextIncomplete,
        phases: tasks.phases,
      });
    }
  }

  const archiveRoot = path.join(workingDir, ARCHIVE_SUBPATH);
  if (existsSync(archiveRoot)) {
    const entries = await readdir(archiveRoot, { withFileTypes: true });
    for (const entry of entries) {
      if (!entry.isDirectory()) continue;
      const archivedDir = path.join(archiveRoot, entry.name);
      const sidecar = await readSidecar(archivedDir);
      if (!sidecar || sidecar.fleeceId !== branchFleeceId) continue;

      const changeName = stripDatePrefix(entry.name);
      if (linked.some((l) => !l.isArchived && l.name === changeName)) continue;

      const tasks = await readTasksState(archivedDir);
      linked.push({
        name: changeName,
        createdBy: sidecar.createdBy,
        isArchived: true,
        archivedFolderName: entry.name,
        tasksDone: tasks.tasksDone,
        tasksTotal: tasks.tasksTotal,
        nextIncomplete: tasks.nextIncomplete,
        phases: tasks.phases,
      });
    }
  }

  return { linked, orphans };
}

/**
 * Composes the request payload matching the server's `BranchStateRequest` DTO.
 */
export function composeSnapshot(input: {
  projectId: string;
  branch: string;
  fleeceId: string;
  scan: ScanResult;
}): BranchStateRequest {
  return {
    projectId: input.projectId,
    branch: input.branch,
    fleeceId: input.fleeceId,
    changes: input.scan.linked,
    orphans: input.scan.orphans,
  };
}

/**
 * POSTs the snapshot to `{serverUrl}/api/openspec/branch-state`.
 * Errors are logged but not thrown — snapshot delivery is best-effort.
 */
export async function postBranchState(
  serverUrl: string,
  payload: BranchStateRequest,
  fetchImpl: typeof fetch = fetch,
): Promise<boolean> {
  const url = `${serverUrl.replace(/\/+$/, "")}/api/openspec/branch-state`;
  try {
    const res = await fetchImpl(url, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(payload),
    });
    if (!res.ok) {
      logWarn(
        `OpenSpec branch-state POST to ${url} failed with ${res.status}`,
      );
      return false;
    }
    return true;
  } catch (err) {
    logWarn(`OpenSpec branch-state POST to ${url} threw: ${String(err)}`);
    return false;
  }
}

export interface PostSessionHookOptions {
  workingDirectory: string;
  projectId: string;
  branch: string;
  fleeceId: string;
  serverUrl: string;
  fetchImpl?: typeof fetch;
}

/**
 * End-to-end post-session hook: scan → compose → post. Bails early (returning
 * false) when required inputs are missing.
 */
export async function runOpenSpecPostSessionHook(
  opts: PostSessionHookOptions,
): Promise<boolean> {
  if (
    !opts.workingDirectory ||
    !opts.projectId ||
    !opts.branch ||
    !opts.fleeceId ||
    !opts.serverUrl
  ) {
    return false;
  }

  try {
    const scan = await scanChangesDirectory(
      opts.workingDirectory,
      opts.fleeceId,
    );
    const payload = composeSnapshot({
      projectId: opts.projectId,
      branch: opts.branch,
      fleeceId: opts.fleeceId,
      scan,
    });

    logInfo(
      `Posting OpenSpec snapshot: ${payload.changes.length} linked, ${payload.orphans.length} orphans`,
    );
    return await postBranchState(
      opts.serverUrl,
      payload,
      opts.fetchImpl ?? fetch,
    );
  } catch (err) {
    logWarn(`OpenSpec post-session hook failed: ${String(err)}`);
    return false;
  }
}
