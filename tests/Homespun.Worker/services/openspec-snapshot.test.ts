import { describe, it, expect, afterEach } from "vitest";
import { mkdtemp, rm, mkdir, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";

import {
  parseSidecarYaml,
  parseTasksMd,
  scanChangesDirectory,
  composeSnapshot,
  postBranchState,
  runOpenSpecPostSessionHook,
} from "#src/services/openspec-snapshot.js";

describe("parseSidecarYaml", () => {
  it("parses fleeceId and createdBy", () => {
    const parsed = parseSidecarYaml("fleeceId: abc123\ncreatedBy: agent\n");
    expect(parsed).toEqual({ fleeceId: "abc123", createdBy: "agent" });
  });

  it("tolerates quoted values and comments", () => {
    const parsed = parseSidecarYaml(
      "# header\nfleeceId: 'quoted-id'\ncreatedBy: \"server\"\n",
    );
    expect(parsed).toEqual({ fleeceId: "quoted-id", createdBy: "server" });
  });

  it("returns null when required keys missing", () => {
    expect(parseSidecarYaml("fleeceId: abc\n")).toBeNull();
    expect(parseSidecarYaml("createdBy: agent\n")).toBeNull();
    expect(parseSidecarYaml("")).toBeNull();
  });
});

describe("parseTasksMd", () => {
  it("groups tasks by phase heading and counts checkbox state", () => {
    const md =
      "## 1. Setup\n\n- [x] Task one\n- [ ] Task two\n\n## 2. Build\n\n- [ ] Task three\n";
    const result = parseTasksMd(md);
    expect(result.tasksTotal).toBe(3);
    expect(result.tasksDone).toBe(1);
    expect(result.nextIncomplete).toBe("Task two");
    expect(result.phases).toHaveLength(2);
    expect(result.phases[0]).toMatchObject({ name: "1. Setup", done: 1, total: 2 });
    expect(result.phases[1]).toMatchObject({ name: "2. Build", done: 0, total: 1 });
  });

  it("returns zero counts for empty content", () => {
    expect(parseTasksMd("")).toEqual({
      tasksDone: 0,
      tasksTotal: 0,
      nextIncomplete: null,
      phases: [],
    });
  });
});

describe("scanChangesDirectory", () => {
  let workingDir: string;

  afterEach(async () => {
    if (workingDir) await rm(workingDir, { recursive: true, force: true });
  });

  it("separates linked changes from orphans and skips inherited", async () => {
    workingDir = await mkdtemp(path.join(tmpdir(), "worker-scan-"));
    await setupChangeDir(workingDir, "linked", {
      sidecar: "fleeceId: my-id\ncreatedBy: agent\n",
      tasks: "## 1. Phase\n\n- [x] Done\n- [ ] Pending\n",
    });
    await setupChangeDir(workingDir, "inherited", {
      sidecar: "fleeceId: other-id\ncreatedBy: server\n",
    });
    await setupChangeDir(workingDir, "orphan", {});

    const scan = await scanChangesDirectory(workingDir, "my-id");

    expect(scan.linked).toHaveLength(1);
    expect(scan.linked[0].name).toBe("linked");
    expect(scan.linked[0].tasksDone).toBe(1);
    expect(scan.linked[0].tasksTotal).toBe(2);
    expect(scan.linked[0].nextIncomplete).toBe("Pending");

    expect(scan.orphans).toHaveLength(1);
    expect(scan.orphans[0].name).toBe("orphan");
  });

  it("includes archived changes whose sidecar matches", async () => {
    workingDir = await mkdtemp(path.join(tmpdir(), "worker-scan-"));
    await setupArchivedChangeDir(workingDir, "2026-04-16-gone-change", {
      sidecar: "fleeceId: my-id\ncreatedBy: agent\n",
    });

    const scan = await scanChangesDirectory(workingDir, "my-id");

    expect(scan.linked).toHaveLength(1);
    expect(scan.linked[0].name).toBe("gone-change");
    expect(scan.linked[0].isArchived).toBe(true);
    expect(scan.linked[0].archivedFolderName).toBe("2026-04-16-gone-change");
  });

  it("returns empty when no openspec directory exists", async () => {
    workingDir = await mkdtemp(path.join(tmpdir(), "worker-scan-"));
    const scan = await scanChangesDirectory(workingDir, "my-id");
    expect(scan.linked).toEqual([]);
    expect(scan.orphans).toEqual([]);
  });
});

describe("composeSnapshot", () => {
  it("produces a BranchStateRequest with scan contents", () => {
    const payload = composeSnapshot({
      projectId: "p",
      branch: "feat/foo+id",
      fleeceId: "id",
      scan: {
        linked: [
          {
            name: "x",
            createdBy: "agent",
            isArchived: false,
            archivedFolderName: null,
            tasksDone: 1,
            tasksTotal: 2,
            nextIncomplete: "Next",
            phases: [],
          },
        ],
        orphans: [{ name: "o", createdOnBranch: true }],
      },
    });

    expect(payload.projectId).toBe("p");
    expect(payload.branch).toBe("feat/foo+id");
    expect(payload.fleeceId).toBe("id");
    expect(payload.changes[0].name).toBe("x");
    expect(payload.orphans[0].name).toBe("o");
  });
});

describe("postBranchState", () => {
  it("POSTs JSON to /api/openspec/branch-state and reports success", async () => {
    const calls: Array<{ url: string; init?: RequestInit }> = [];
    const fakeFetch: typeof fetch = async (url, init) => {
      calls.push({ url: String(url), init });
      return new Response("{}", { status: 200 });
    };

    const ok = await postBranchState(
      "http://server:8080/",
      {
        projectId: "p",
        branch: "b",
        fleeceId: "f",
        changes: [],
        orphans: [],
      },
      fakeFetch,
    );

    expect(ok).toBe(true);
    expect(calls).toHaveLength(1);
    expect(calls[0].url).toBe("http://server:8080/api/openspec/branch-state");
    expect(calls[0].init?.method).toBe("POST");
    expect(JSON.parse(String(calls[0].init?.body))).toMatchObject({
      projectId: "p",
      fleeceId: "f",
    });
  });

  it("returns false on non-2xx response", async () => {
    const fakeFetch: typeof fetch = async () =>
      new Response("boom", { status: 500 });
    const ok = await postBranchState(
      "http://server",
      { projectId: "p", branch: "b", fleeceId: "f", changes: [], orphans: [] },
      fakeFetch,
    );
    expect(ok).toBe(false);
  });

  it("returns false when fetch throws", async () => {
    const fakeFetch: typeof fetch = async () => {
      throw new Error("network down");
    };
    const ok = await postBranchState(
      "http://server",
      { projectId: "p", branch: "b", fleeceId: "f", changes: [], orphans: [] },
      fakeFetch,
    );
    expect(ok).toBe(false);
  });
});

describe("runOpenSpecPostSessionHook", () => {
  let workingDir: string;

  afterEach(async () => {
    if (workingDir) await rm(workingDir, { recursive: true, force: true });
  });

  it("scans on-disk changes then POSTs the snapshot", async () => {
    workingDir = await mkdtemp(path.join(tmpdir(), "worker-hook-"));
    await setupChangeDir(workingDir, "my-change", {
      sidecar: "fleeceId: fleece-1\ncreatedBy: agent\n",
      tasks: "## Phase\n\n- [x] Done\n- [ ] Todo\n",
    });

    let posted: unknown = null;
    const fakeFetch: typeof fetch = async (_url, init) => {
      posted = JSON.parse(String(init?.body));
      return new Response("{}", { status: 200 });
    };

    const ok = await runOpenSpecPostSessionHook({
      workingDirectory: workingDir,
      projectId: "proj-1",
      branch: "feat/whatever+fleece-1",
      fleeceId: "fleece-1",
      serverUrl: "http://server:8080",
      fetchImpl: fakeFetch,
    });

    expect(ok).toBe(true);
    expect(posted).toMatchObject({
      projectId: "proj-1",
      branch: "feat/whatever+fleece-1",
      fleeceId: "fleece-1",
      changes: [
        {
          name: "my-change",
          tasksDone: 1,
          tasksTotal: 2,
          nextIncomplete: "Todo",
        },
      ],
      orphans: [],
    });
  });

  it("no-ops when required inputs are missing", async () => {
    let called = false;
    const fakeFetch: typeof fetch = async () => {
      called = true;
      return new Response("{}", { status: 200 });
    };
    const ok = await runOpenSpecPostSessionHook({
      workingDirectory: "",
      projectId: "p",
      branch: "b",
      fleeceId: "f",
      serverUrl: "http://server",
      fetchImpl: fakeFetch,
    });
    expect(ok).toBe(false);
    expect(called).toBe(false);
  });
});

// --- helpers ---

async function setupChangeDir(
  workingDir: string,
  changeName: string,
  files: { sidecar?: string; tasks?: string },
) {
  const dir = path.join(workingDir, "openspec", "changes", changeName);
  await mkdir(dir, { recursive: true });
  if (files.sidecar !== undefined) {
    await writeFile(path.join(dir, ".homespun.yaml"), files.sidecar);
  }
  if (files.tasks !== undefined) {
    await writeFile(path.join(dir, "tasks.md"), files.tasks);
  }
}

async function setupArchivedChangeDir(
  workingDir: string,
  folder: string,
  files: { sidecar?: string; tasks?: string },
) {
  const dir = path.join(workingDir, "openspec", "changes", "archive", folder);
  await mkdir(dir, { recursive: true });
  if (files.sidecar !== undefined) {
    await writeFile(path.join(dir, ".homespun.yaml"), files.sidecar);
  }
  if (files.tasks !== undefined) {
    await writeFile(path.join(dir, "tasks.md"), files.tasks);
  }
}
