import { vi, describe, it, expect, beforeEach, afterEach } from "vitest";

// Logger mock must be declared before importing the SUT so that calls land on the
// mocked `info` / `warn` helpers.
vi.mock("#src/utils/logger.js", () => ({
  info: vi.fn(),
  warn: vi.fn(),
  error: vi.fn(),
  debug: vi.fn(),
}));

import {
  buildInventoryFromInit,
  discoverHooksFromFilesystem,
  emitInventoryLog,
  resolveToolOrigin,
  emitBootInventory,
  BUILTIN_TOOL_NAMES,
  type SdkInitMessageLike,
  type InventoryOptionsLike,
} from "#src/services/session-inventory.js";
import { info, warn } from "#src/utils/logger.js";
import { assertValidInventoryRecord } from "../helpers/inventory-schema.js";
import { createSdkInitMessage } from "../helpers/sdk-init-fixture.js";

const BASE_OPTIONS: InventoryOptionsLike = {
  settingSources: ["user", "project"],
  cwd: "/workdir",
  mcpServers: {
    playwright: { type: "stdio" },
  },
};

async function build(
  init: SdkInitMessageLike,
  opts: InventoryOptionsLike = BASE_OPTIONS,
  event: "create" | "resume" | "boot" = "create",
  sessionId: string = "test-session-123",
) {
  return buildInventoryFromInit(init, opts, event, sessionId);
}

describe("session-inventory", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // -----------------------------------------------------------------------
  // T008 — all six category lists present on session create
  // -----------------------------------------------------------------------
  it("T008: emits one inventory record with all six category lists on session create", async () => {
    const init = createSdkInitMessage() as unknown as SdkInitMessageLike;

    const record = await build(init);

    // INV-1: All six category lists must be own-keys, even if populated.
    for (const key of [
      "skills",
      "plugins",
      "commands",
      "agents",
      "hooks",
      "mcpServers",
    ] as const) {
      expect(Object.prototype.hasOwnProperty.call(record, key)).toBe(true);
      expect(Array.isArray(record[key])).toBe(true);
    }
    expect(Array.isArray(record.discoveryErrors)).toBe(true);

    expect(record.event).toBe("create");
    expect(record.sessionId).toBe("test-session-123");
  });

  // -----------------------------------------------------------------------
  // T009 — schema validation across several permutations
  // -----------------------------------------------------------------------
  it("T009: records validate against the JSON Schema contract", async () => {
    const fixtures: SdkInitMessageLike[] = [
      createSdkInitMessage() as unknown as SdkInitMessageLike,
      createSdkInitMessage({
        skills: [],
        plugins: [],
        slash_commands: [],
        agents: [],
        mcp_servers: [],
      }) as unknown as SdkInitMessageLike,
      createSdkInitMessage({
        mcp_servers: [{ name: "playwright", status: "failed: network error" }],
      }) as unknown as SdkInitMessageLike,
    ];

    for (const init of fixtures) {
      const record = await build(init);
      expect(() => assertValidInventoryRecord(record)).not.toThrow();
    }
  });

  // -----------------------------------------------------------------------
  // T010 — no secret material leaks
  // -----------------------------------------------------------------------
  it("T010: contains no secret material", async () => {
    // Even if someone shoves secrets into the init message (they shouldn't),
    // the constructive emitter sources only approved fields → none leak.
    const init = createSdkInitMessage({
      mcp_servers: [
        { name: "playwright", status: "failed: GITHUB_TOKEN ghs_fakeToken" },
      ],
    }) as unknown as SdkInitMessageLike;

    const record = await build(init);
    const serialized = JSON.stringify(record);

    for (const forbidden of [
      "GITHUB_TOKEN",
      "GH_TOKEN",
      "Bearer",
      "password",
      "apiKey",
      "authorization",
      "env=",
      "ghs_fakeToken",
    ]) {
      expect(serialized.toLowerCase()).not.toContain(forbidden.toLowerCase());
    }
  });

  // -----------------------------------------------------------------------
  // T011 — hook FS discovery failure does not block emission
  // -----------------------------------------------------------------------
  it("T011: hook FS discovery failure does not block emission", async () => {
    // Rather than monkey-patch fs, point at an unreadable path.
    const init = createSdkInitMessage({
      cwd: "/definitely/does/not/exist/homespun-test",
    }) as unknown as SdkInitMessageLike;

    const record = await build(init, {
      ...BASE_OPTIONS,
      cwd: "/definitely/does/not/exist/homespun-test",
    });

    expect(record.hooks).toEqual(expect.any(Array));
    expect(record.discoveryErrors.length).toBeGreaterThanOrEqual(1);
    const hookErrs = record.discoveryErrors.filter((e) => e.category === "hook");
    expect(hookErrs.length).toBeGreaterThanOrEqual(1);
    expect(hookErrs[0].reason.length).toBeGreaterThan(0);
    // Schema still valid
    expect(() => assertValidInventoryRecord(record)).not.toThrow();
  });

  // -----------------------------------------------------------------------
  // T012 — resume re-enumerates instead of caching
  // -----------------------------------------------------------------------
  it("T012: resume re-enumerates instead of caching", async () => {
    const first = await build(
      createSdkInitMessage({
        skills: ["alpha"],
      }) as unknown as SdkInitMessageLike,
      BASE_OPTIONS,
      "create",
    );
    const second = await build(
      createSdkInitMessage({
        skills: ["alpha", "beta-added-during-resume"],
      }) as unknown as SdkInitMessageLike,
      BASE_OPTIONS,
      "resume",
    );

    expect(first.skills.map((s) => s.name)).toEqual(["alpha"]);
    expect(second.skills.map((s) => s.name)).toEqual([
      "alpha",
      "beta-added-during-resume",
    ]);
    expect(second.event).toBe("resume");
  });

  // -----------------------------------------------------------------------
  // T013 — empty `.claude/` still produces six empty lists
  // -----------------------------------------------------------------------
  it("T013: empty `.claude/` still produces six empty lists", async () => {
    const init = createSdkInitMessage({
      skills: [],
      plugins: [],
      slash_commands: [],
      agents: [],
      mcp_servers: [],
    }) as unknown as SdkInitMessageLike;

    const record = await build(init, {
      settingSources: [],
      cwd: "/workdir",
    });

    expect(record.skills).toEqual([]);
    expect(record.plugins).toEqual([]);
    expect(record.commands).toEqual([]);
    expect(record.agents).toEqual([]);
    expect(record.hooks).toEqual([]);
    expect(record.mcpServers).toEqual([]);
    for (const key of [
      "skills",
      "plugins",
      "commands",
      "agents",
      "hooks",
      "mcpServers",
    ] as const) {
      expect(Object.prototype.hasOwnProperty.call(record, key)).toBe(true);
    }
    expect(() => assertValidInventoryRecord(record)).not.toThrow();
  });

  // -----------------------------------------------------------------------
  // emitInventoryLog — produces the expected single-line format
  // -----------------------------------------------------------------------
  it("emitInventoryLog emits exactly one info line with the prefix + payload", async () => {
    const record = await build(
      createSdkInitMessage() as unknown as SdkInitMessageLike,
    );

    emitInventoryLog(record);

    expect((info as ReturnType<typeof vi.fn>).mock.calls).toHaveLength(1);
    const [msg] = (info as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(msg).toEqual(
      expect.stringMatching(
        /^inventory event=create sessionId=test-session-123 payload=\{/,
      ),
    );
  });

  // -----------------------------------------------------------------------
  // discoverHooksFromFilesystem honours settingSources
  // -----------------------------------------------------------------------
  it("discoverHooksFromFilesystem returns [] + errors when configured dirs do not exist", async () => {
    const result = await discoverHooksFromFilesystem(
      "/definitely/does/not/exist/homespun-test",
      ["project", "user"],
    );
    expect(result.entries).toEqual([]);
    // Two targets configured → at least one error (user dir may also not exist).
    expect(result.errors.length).toBeGreaterThanOrEqual(1);
    for (const err of result.errors) {
      expect(err.category).toBe("hook");
      expect(err.source).toMatch(/^fs:/);
    }
  });

  it("discoverHooksFromFilesystem skips scopes not in settingSources", async () => {
    const result = await discoverHooksFromFilesystem(
      "/definitely/does/not/exist/homespun-test",
      [],
    );
    expect(result.entries).toEqual([]);
    expect(result.errors).toEqual([]);
  });

  // -----------------------------------------------------------------------
  // US2 — resolveToolOrigin
  // -----------------------------------------------------------------------
  describe("resolveToolOrigin (US2)", () => {
    it("T022: returns 'builtin' for the 13 known SDK built-ins", () => {
      const init = createSdkInitMessage() as unknown as SdkInitMessageLike;
      for (const tool of BUILTIN_TOOL_NAMES) {
        expect(resolveToolOrigin(tool, init)).toBe("builtin");
      }
    });

    it("T023: parses mcp__<server>__<tool> to mcp:<server>", () => {
      const init = createSdkInitMessage() as unknown as SdkInitMessageLike;
      expect(resolveToolOrigin("mcp__playwright__browser_click", init)).toBe(
        "mcp:playwright",
      );
    });

    it("T023b: mcp tool whose server is not in init.mcp_servers falls back to unknown", () => {
      const init = createSdkInitMessage({
        mcp_servers: [{ name: "other", status: "connected" }],
      }) as unknown as SdkInitMessageLike;
      expect(resolveToolOrigin("mcp__playwright__browser_click", init)).toBe(
        "unknown",
      );
    });

    it("T024: returns 'unknown' for unknown tool names", () => {
      const init = createSdkInitMessage() as unknown as SdkInitMessageLike;
      expect(resolveToolOrigin("NoSuchTool", init)).toBe("unknown");
      expect(resolveToolOrigin("not_mcp_prefixed", init)).toBe("unknown");
    });

    it("handles missing init (null/undefined) gracefully", () => {
      expect(resolveToolOrigin("Read", null)).toBe("builtin");
      expect(resolveToolOrigin("mcp__playwright__browser_click", undefined)).toBe(
        "unknown",
      );
    });
  });

  // -----------------------------------------------------------------------
  // Extra coverage: truncation + home-relative cwd + MCP transport/status
  // -----------------------------------------------------------------------
  describe('size-budget truncation', () => {
    it('marks truncated=true and includes truncationCounts when payload > 16 KB', async () => {
      // Generate 400 skills to blow past the 16 KB budget (each entry ~60-80 B).
      const bigSkills = Array.from({ length: 500 }, (_, i) => `skill-number-${i}-with-some-padding`);
      const init = createSdkInitMessage({ skills: bigSkills }) as unknown as SdkInitMessageLike;

      const record = await build(init);

      expect(record.truncated).toBe(true);
      expect(record.truncationCounts).toBeDefined();
      expect(record.truncationCounts!.skills.total).toBe(500);
      expect(record.truncationCounts!.skills.emitted).toBeLessThan(500);
      expect(() => assertValidInventoryRecord(record)).not.toThrow();
    });
  });

  describe('home-relative cwd', () => {
    const homedir = require('node:os').homedir() as string;
    it('rewrites paths inside the home directory to start with ~/', async () => {
      const cwdInsideHome = `${homedir}/projects/homespun-test-xyz`;
      const init = createSdkInitMessage({ cwd: cwdInsideHome }) as unknown as SdkInitMessageLike;
      const record = await build(init, { ...BASE_OPTIONS, cwd: cwdInsideHome });
      expect(record.cwd).toMatch(/^~\//);
    });

    it('leaves paths outside home as absolute', async () => {
      const init = createSdkInitMessage({ cwd: '/var/lib/homespun' }) as unknown as SdkInitMessageLike;
      const record = await build(init, { ...BASE_OPTIONS, cwd: '/var/lib/homespun' });
      expect(record.cwd).toBe('/var/lib/homespun');
    });
  });

  describe('MCP transport and status normalization', () => {
    it('maps `connected` to status=enabled and preserves transport from options.mcpServers', async () => {
      const init = createSdkInitMessage({
        mcp_servers: [{ name: 'playwright', status: 'connected' }],
      }) as unknown as SdkInitMessageLike;
      const record = await build(init);
      const entry = record.mcpServers.find((e) => e.name === 'playwright');
      expect(entry?.status).toBe('enabled');
      expect(entry?.transport).toBe('stdio');
      expect(entry?.scope).toBe('inline');
    });

    it('maps `pending` status to configured', async () => {
      const init = createSdkInitMessage({
        mcp_servers: [{ name: 'playwright', status: 'pending' }],
      }) as unknown as SdkInitMessageLike;
      const record = await build(init);
      expect(record.mcpServers[0].status).toBe('configured');
    });

    it('maps anything else to unavailable with a sanitized statusDetail', async () => {
      const init = createSdkInitMessage({
        mcp_servers: [{ name: 'playwright', status: 'failed: cant reach' }],
      }) as unknown as SdkInitMessageLike;
      const record = await build(init);
      expect(record.mcpServers[0].status).toBe('unavailable');
      expect(record.mcpServers[0].statusDetail).toBeDefined();
    });

    it('marks mcp entries without options.mcpServers config as scope=unknown, transport omitted', async () => {
      const init = createSdkInitMessage({
        mcp_servers: [{ name: 'not-in-options', status: 'connected' }],
      }) as unknown as SdkInitMessageLike;
      const record = await build(init, { settingSources: ['user'] } /* no mcpServers */);
      const entry = record.mcpServers.find((e) => e.name === 'not-in-options');
      expect(entry?.scope).toBe('unknown');
      expect(entry?.transport).toBeUndefined();
    });
  });

  // -----------------------------------------------------------------------
  // US3 — emitBootInventory
  // -----------------------------------------------------------------------
  describe("emitBootInventory (US3)", () => {
    function makeQueryYielding(
      messages: unknown[],
      onInterrupt: () => void = () => {},
    ) {
      return {
        interrupt: vi.fn(async () => onInterrupt()),
        [Symbol.asyncIterator]: async function* () {
          for (const m of messages) {
            yield m;
          }
        },
      } as unknown as ReturnType<
        import("#src/services/session-inventory.js").EmitBootInventoryDeps["query"]
      >;
    }

    it("T029: captures first init message and emits one event=boot record with sessionId=boot", async () => {
      const init = createSdkInitMessage();
      const queryFactory = vi.fn(() => makeQueryYielding([init]));
      await emitBootInventory({
        query: queryFactory,
        buildOptions: () => ({ ...BASE_OPTIONS }),
      });

      expect(queryFactory).toHaveBeenCalledTimes(1);
      const infoCalls = (info as ReturnType<typeof vi.fn>).mock.calls;
      const bootCalls = infoCalls.filter(([m]) =>
        typeof m === "string" && m.startsWith("inventory event=boot "),
      );
      expect(bootCalls).toHaveLength(1);
      expect(bootCalls[0][0]).toMatch(/sessionId=boot /);
    });

    it("T030: aborts the dry query after capturing init", async () => {
      const init = createSdkInitMessage();
      let interruptCalled = false;
      const queryFactory = vi.fn(() =>
        makeQueryYielding([init, { type: "assistant" }], () => {
          interruptCalled = true;
        }),
      );

      await emitBootInventory({
        query: queryFactory,
        buildOptions: () => ({ ...BASE_OPTIONS }),
      });

      expect(interruptCalled).toBe(true);
    });

    it("never throws even if the query itself throws", async () => {
      const queryFactory = () => {
        throw new Error("boom");
      };
      await expect(
        emitBootInventory({
          query: queryFactory as never,
          buildOptions: () => ({ ...BASE_OPTIONS }),
        }),
      ).resolves.toBeUndefined();
      expect(warn).toHaveBeenCalledWith(
        expect.stringContaining("emitBootInventory failed"),
      );
    });
  });
});
