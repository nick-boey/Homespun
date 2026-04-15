/**
 * Session inventory logging.
 *
 * Builds and emits a single structured `info`-level log line per session
 * lifecycle event (create / resume / boot) enumerating the six Claude Agent
 * resource categories (skills, plugins, slash commands, sub-agents, hooks,
 * MCP servers) available to the SDK session.
 *
 * See openspec/changes/worker-skills-logging/ for the full spec, data model
 * and JSON Schema contract.
 */
import { readdir } from "node:fs/promises";
import { homedir } from "node:os";
import { join, relative, resolve } from "node:path";
import { info, warn } from "../utils/logger.js";
import type { SDKMessage, Query } from "@anthropic-ai/claude-agent-sdk";

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

export type InventoryCategory =
  | "skill"
  | "plugin"
  | "command"
  | "agent"
  | "hook"
  | "mcpServer";

export type InventoryScope =
  | "user"
  | "project"
  | "plugin"
  | "inline"
  | "unknown";

export type McpTransport = "stdio" | "sse" | "http" | "sdk";

export type McpStatus = "configured" | "unavailable" | "enabled";

export interface ResourceInventoryEntry {
  category: InventoryCategory;
  name: string;
  scope: InventoryScope;
  sourcePath?: string;
  providedByPlugin?: string;
  enabled?: boolean;
  transport?: McpTransport;
  status?: McpStatus;
  statusDetail?: string;
}

export interface DiscoveryError {
  category: InventoryCategory;
  source: string;
  reason: string;
}

export type InventoryEvent = "create" | "resume" | "boot";

export interface SessionInventoryLogRecord {
  event: InventoryEvent;
  sessionId: string;
  cwd: string;
  timestamp: string;
  sdkVersion?: string;
  model?: string;
  permissionMode?: "default" | "acceptEdits" | "plan" | "bypassPermissions";
  settingSources: string[];
  skills: ResourceInventoryEntry[];
  plugins: ResourceInventoryEntry[];
  commands: ResourceInventoryEntry[];
  agents: ResourceInventoryEntry[];
  hooks: ResourceInventoryEntry[];
  mcpServers: ResourceInventoryEntry[];
  discoveryErrors: DiscoveryError[];
  truncated?: boolean;
  truncationCounts?: Record<string, { emitted: number; total: number }>;
}

/**
 * Subset of `@anthropic-ai/claude-agent-sdk.SDKSystemMessage` + `subtype: 'init'`
 * used by the inventory helper. Kept local (and structurally compatible) so
 * callers don't have to import SDK types.
 */
export interface SdkInitMessageLike {
  type: "system";
  subtype: "init";
  cwd: string;
  session_id: string;
  claude_code_version?: string;
  model?: string;
  permissionMode?: SessionInventoryLogRecord["permissionMode"];
  skills?: string[];
  slash_commands?: string[];
  agents?: string[];
  plugins?: { name: string; path: string }[];
  mcp_servers?: { name: string; status: string }[];
}

/**
 * Shape of the options bag the worker passes to `query()` that is relevant to
 * inventory enrichment (hooks + MCP server transport + settingSources).
 * Intentionally narrow.
 */
export interface InventoryOptionsLike {
  settingSources?: string[];
  cwd?: string;
  mcpServers?: Record<
    string,
    {
      type?: McpTransport | string;
      [k: string]: unknown;
    }
  >;
  hooks?: Record<string, unknown>;
  [k: string]: unknown;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * SDK built-in tools (per research R8). Tool names that match this list are
 * attributed to `builtin`, regardless of what the init message carries.
 */
export const BUILTIN_TOOL_NAMES: ReadonlySet<string> = new Set([
  "Read",
  "Write",
  "Edit",
  "Bash",
  "Glob",
  "Grep",
  "Task",
  "WebFetch",
  "WebSearch",
  "AskUserQuestion",
  "ExitPlanMode",
  "TodoWrite",
  "NotebookEdit",
]);

/**
 * Soft payload-size budget. When `JSON.stringify(record)` exceeds this, each
 * category list is clipped proportionally and `truncated` + `truncationCounts`
 * are set (INV-1 is still honoured — fields remain present).
 */
const SOFT_PAYLOAD_BUDGET_BYTES = 16 * 1024;

// ---------------------------------------------------------------------------
// Hooks discovery
// ---------------------------------------------------------------------------

/**
 * Best-effort filesystem scan of `~/.claude/hooks/` and `<cwd>/.claude/hooks/`,
 * gated on which scopes are listed in `settingSources`. All failures are
 * captured and returned as `errors`; the function never throws (FR-006, R2).
 */
export async function discoverHooksFromFilesystem(
  cwd: string,
  settingSources: string[],
): Promise<{ entries: ResourceInventoryEntry[]; errors: DiscoveryError[] }> {
  const entries: ResourceInventoryEntry[] = [];
  const errors: DiscoveryError[] = [];

  const targets: { scope: "user" | "project"; dir: string; label: string }[] =
    [];
  if (settingSources.includes("user")) {
    targets.push({
      scope: "user",
      dir: join(homedir(), ".claude", "hooks"),
      label: "fs:~/.claude/hooks",
    });
  }
  if (settingSources.includes("project")) {
    targets.push({
      scope: "project",
      dir: join(cwd, ".claude", "hooks"),
      label: "fs:.claude/hooks",
    });
  }

  for (const t of targets) {
    try {
      const names = await readdir(t.dir);
      for (const rawName of names) {
        // Strip any trailing extension so `PreToolUse.sh` → `PreToolUse`.
        const stem = rawName.replace(/\.[^./]+$/, "");
        const entry: ResourceInventoryEntry = {
          category: "hook",
          name: stem,
          scope: t.scope,
          sourcePath:
            t.scope === "user"
              ? `~/.claude/hooks/${rawName}`
              : `.claude/hooks/${rawName}`,
        };
        entries.push(entry);
      }
    } catch (err) {
      errors.push({
        category: "hook",
        source: t.label,
        reason: sanitizeErrorReason(err),
      });
    }
  }

  return { entries, errors };
}

// ---------------------------------------------------------------------------
// Record construction
// ---------------------------------------------------------------------------

/**
 * Build a `SessionInventoryLogRecord` from the SDK init message plus the
 * options bag passed to `query()` (to surface hooks + MCP transport).
 *
 * Never throws: any discovery failure is recorded in `discoveryErrors` and
 * the record is still returned (FR-006, INV-1).
 */
export async function buildInventoryFromInit(
  init: SdkInitMessageLike,
  options: InventoryOptionsLike,
  event: InventoryEvent,
  sessionId: string,
): Promise<SessionInventoryLogRecord> {
  const discoveryErrors: DiscoveryError[] = [];

  const settingSources = Array.isArray(options.settingSources)
    ? options.settingSources.map(String)
    : [];

  const cwd = homeRelative(init.cwd ?? options.cwd ?? "");

  // Plugins → name-only set for cross-referencing provenance of other categories.
  const pluginNames = new Set<string>(
    (init.plugins ?? []).map((p) => p.name),
  );

  const plugins: ResourceInventoryEntry[] = (init.plugins ?? []).map((p) => ({
    category: "plugin",
    name: p.name,
    scope: "plugin",
    providedByPlugin: p.name,
    sourcePath: p.path ? homeRelative(p.path) : undefined,
  }));

  const skills: ResourceInventoryEntry[] = (init.skills ?? []).map((raw) =>
    buildSimpleEntry("skill", raw, pluginNames),
  );
  const commands: ResourceInventoryEntry[] = (init.slash_commands ?? []).map(
    (raw) => buildSimpleEntry("command", raw, pluginNames),
  );
  const agents: ResourceInventoryEntry[] = (init.agents ?? []).map((raw) =>
    buildSimpleEntry("agent", raw, pluginNames),
  );

  // MCP servers: cross init (name + status) with options.mcpServers (transport).
  const mcpServers: ResourceInventoryEntry[] = (init.mcp_servers ?? []).map(
    (s) => {
      const inlineCfg = options.mcpServers?.[s.name];
      const transport = normalizeTransport(inlineCfg?.type);
      const { status, statusDetail } = normalizeMcpStatus(s.status);
      const entry: ResourceInventoryEntry = {
        category: "mcpServer",
        name: s.name,
        scope: inlineCfg ? "inline" : "unknown",
        transport,
        status,
      };
      if (statusDetail) entry.statusDetail = statusDetail;
      return entry;
    },
  );

  // Hooks: programmatic (options.hooks) + FS discovery.
  const hooks: ResourceInventoryEntry[] = [];
  if (options.hooks && typeof options.hooks === "object") {
    for (const name of Object.keys(options.hooks)) {
      hooks.push({
        category: "hook",
        name,
        scope: "inline",
      });
    }
  }
  const fsHooks = await discoverHooksFromFilesystem(
    init.cwd ?? options.cwd ?? process.cwd(),
    settingSources,
  );
  hooks.push(...fsHooks.entries);
  discoveryErrors.push(...fsHooks.errors);

  const record: SessionInventoryLogRecord = {
    event,
    sessionId,
    cwd,
    timestamp: new Date().toISOString(),
    settingSources,
    skills,
    plugins,
    commands,
    agents,
    hooks,
    mcpServers,
    discoveryErrors,
  };
  if (init.claude_code_version) record.sdkVersion = init.claude_code_version;
  if (init.model) record.model = init.model;
  if (init.permissionMode) record.permissionMode = init.permissionMode;

  return enforceSizeBudget(record);
}

// ---------------------------------------------------------------------------
// Emission
// ---------------------------------------------------------------------------

/**
 * Emit the record as a single `info`-level log line of the form
 * `inventory event=<e> sessionId=<id> payload={json}` (FR-007 / R5).
 */
export function emitInventoryLog(record: SessionInventoryLogRecord): void {
  const json = JSON.stringify(record);
  info(
    `inventory event=${record.event} sessionId=${record.sessionId} payload=${json}`,
  );
}

// ---------------------------------------------------------------------------
// Tool origin (US2)
// ---------------------------------------------------------------------------

/**
 * Resolve the origin of a tool name to one of:
 *   - `builtin`   — known SDK built-in tool (see BUILTIN_TOOL_NAMES).
 *   - `mcp:<server>` — parsed from the `mcp__<server>__<tool>` naming scheme
 *     AND the server is present in `init.mcp_servers[].name`.
 *   - `unknown`   — anything else (incl. plugin-provided, which the SDK does
 *     not yet expose per-tool).
 */
export function resolveToolOrigin(
  toolName: string,
  init: SdkInitMessageLike | undefined | null,
): string {
  if (BUILTIN_TOOL_NAMES.has(toolName)) return "builtin";

  if (toolName.startsWith("mcp__")) {
    // mcp__<server>__<rest>
    const rest = toolName.slice("mcp__".length);
    const sepIdx = rest.indexOf("__");
    if (sepIdx > 0) {
      const server = rest.slice(0, sepIdx);
      const serverNames = new Set((init?.mcp_servers ?? []).map((s) => s.name));
      if (serverNames.has(server)) return `mcp:${server}`;
    }
  }

  return "unknown";
}

// ---------------------------------------------------------------------------
// Boot-time inventory (US3)
// ---------------------------------------------------------------------------

/**
 * Injected dependencies for `emitBootInventory` so tests can stub the SDK
 * `query()` factory without mocking the module.
 */
export interface EmitBootInventoryDeps {
  query: (args: {
    prompt: AsyncIterable<unknown> | AsyncGenerator<unknown>;
    options: Record<string, unknown>;
  }) => Query;
  buildOptions: () => InventoryOptionsLike & Record<string, unknown>;
}

/**
 * Run one dry SDK `query()` against the default working directory, consume
 * messages until the first `system`/`init`, emit a single
 * `inventory event=boot sessionId=boot` record, then abort.
 *
 * Fire-and-forget: never throws, never rejects — observability must not
 * block worker startup (FR-006).
 */
export async function emitBootInventory(
  deps: EmitBootInventoryDeps,
): Promise<void> {
  try {
    const options = deps.buildOptions();
    // The SDK's Claude CLI subprocess doesn't emit `system/init` until it sees
    // a prompt arrive on stdin. Yield a single minimal user message so init
    // fires; we `interrupt()` before the CLI ever runs a turn, so this costs
    // effectively zero beyond spinning the CLI up.
    async function* minimalPrompt(): AsyncGenerator<unknown> {
      yield {
        type: "user",
        session_id: "",
        message: { role: "user", content: [{ type: "text", text: "." }] },
        parent_tool_use_id: null,
      };
      // Then block until aborted.
      await new Promise(() => {});
    }
    const q = deps.query({
      prompt: minimalPrompt(),
      options: options as Record<string, unknown>,
    });

    try {
      for await (const msg of q as AsyncIterable<SDKMessage>) {
        if (
          (msg as { type?: string }).type === "system" &&
          (msg as { subtype?: string }).subtype === "init"
        ) {
          const record = await buildInventoryFromInit(
            msg as unknown as SdkInitMessageLike,
            options,
            "boot",
            "boot",
          );
          emitInventoryLog(record);
          break;
        }
      }
    } finally {
      try {
        await q.interrupt?.();
      } catch {
        // swallow — interrupt is best-effort
      }
    }
  } catch (err) {
    warn(
      `emitBootInventory failed: ${err instanceof Error ? err.message : String(err)}`,
    );
  }
}

// ---------------------------------------------------------------------------
// Helpers (private)
// ---------------------------------------------------------------------------

function buildSimpleEntry(
  category: Extract<InventoryCategory, "skill" | "command" | "agent">,
  raw: string,
  pluginNames: ReadonlySet<string>,
): ResourceInventoryEntry {
  // Plugin-provided resources use the `pluginName:resourceName` convention
  // (matches the names surfaced in this conversation's own tool list, e.g.
  // `superpowers:brainstorming`, `subframe:design`).
  const colonIdx = raw.indexOf(":");
  if (colonIdx > 0) {
    const maybePlugin = raw.slice(0, colonIdx);
    if (pluginNames.has(maybePlugin)) {
      return {
        category,
        name: raw,
        scope: "plugin",
        providedByPlugin: maybePlugin,
      };
    }
  }
  return {
    category,
    name: raw,
    scope: "unknown",
  };
}

function normalizeTransport(
  raw: unknown,
): McpTransport | undefined {
  if (raw === "stdio" || raw === "sse" || raw === "http" || raw === "sdk") {
    return raw;
  }
  return undefined;
}

function normalizeMcpStatus(raw: string): {
  status: McpStatus;
  statusDetail?: string;
} {
  const s = (raw ?? "").toLowerCase();
  if (s === "connected" || s === "enabled" || s === "ready") {
    return { status: "enabled" };
  }
  if (s === "configured" || s === "pending" || s === "starting") {
    return { status: "configured" };
  }
  // Anything else (failed, disconnected, error, ...) is surfaced as
  // `unavailable` with the verbatim-but-sanitized original as detail.
  return { status: "unavailable", statusDetail: sanitizeStatusDetail(raw) };
}

/**
 * Short sanitizer used on error messages / free-form status strings. Strips
 * anything that looks like a secret substring (FR-010 / INV-2). Errs on the
 * side of stripping too much.
 */
function sanitizeErrorReason(err: unknown): string {
  const raw =
    err instanceof Error ? `${err.name}: ${err.message}` : String(err);
  return sanitizeStatusDetail(raw);
}

const SECRET_SUBSTRINGS = [
  "GITHUB_TOKEN",
  "GH_TOKEN",
  "Bearer",
  "password",
  "apiKey",
  "api_key",
  "authorization",
  "secret",
];

function sanitizeStatusDetail(raw: string): string {
  let out = raw ?? "";
  for (const s of SECRET_SUBSTRINGS) {
    // Replace the substring (case-insensitive) wherever it appears, plus any
    // `=value` / `: value` tail up to the next whitespace or comma.
    const re = new RegExp(`${s}[^\\s,]*`, "gi");
    out = out.replace(re, "[REDACTED]");
  }
  // Drop any `env=...` token (may be key=value pair).
  out = out.replace(/env=[^\s,]+/gi, "env=[REDACTED]");
  // Strip well-known credential-token prefixes (GitHub PATs, OpenAI keys,
  // Slack tokens) regardless of length.
  out = out.replace(
    /\b(ghs|gho|ghp|ghu|ghr|ght)_[A-Za-z0-9_-]+/g,
    "[REDACTED]",
  );
  out = out.replace(/\bsk-[A-Za-z0-9_-]{10,}/g, "[REDACTED]");
  out = out.replace(/\bxox[baprs]-[A-Za-z0-9-]+/g, "[REDACTED]");
  // Finally: any long opaque token-looking run (≥ 24 chars) is redacted.
  out = out.replace(/[A-Za-z0-9_-]{24,}/g, "[REDACTED]");
  // Clip to keep log lines tidy.
  if (out.length > 300) out = out.slice(0, 300) + "…";
  return out;
}

function homeRelative(path: string): string {
  if (!path) return path;
  const home = homedir();
  const abs = resolve(path);
  if (home && (abs === home || abs.startsWith(home + "/"))) {
    const rel = relative(home, abs);
    return rel ? `~/${rel}` : "~";
  }
  return abs;
}

/**
 * If the stringified payload would exceed the soft budget, clip every
 * non-empty category list proportionally and annotate the record with
 * `truncated: true` + per-category `{ emitted, total }` counts.
 */
function enforceSizeBudget(
  record: SessionInventoryLogRecord,
): SessionInventoryLogRecord {
  const encoded = JSON.stringify(record);
  if (encoded.length <= SOFT_PAYLOAD_BUDGET_BYTES) return record;

  const categories: (keyof SessionInventoryLogRecord)[] = [
    "skills",
    "plugins",
    "commands",
    "agents",
    "hooks",
    "mcpServers",
  ];

  const totals: Record<string, number> = {};
  let grandTotal = 0;
  for (const c of categories) {
    const list = record[c] as ResourceInventoryEntry[];
    totals[c as string] = list.length;
    grandTotal += list.length;
  }
  if (grandTotal === 0) return record;

  // Each entry contributes ~roughly (encoded.length / grandTotal) bytes. Find
  // the proportional per-category keep count that brings us under budget.
  const ratio = SOFT_PAYLOAD_BUDGET_BYTES / encoded.length;
  const truncationCounts: Record<string, { emitted: number; total: number }> =
    {};
  for (const c of categories) {
    const list = record[c] as ResourceInventoryEntry[];
    const keep = Math.max(0, Math.floor(list.length * ratio));
    truncationCounts[c as string] = { emitted: keep, total: list.length };
    (record as unknown as Record<string, ResourceInventoryEntry[]>)[
      c as string
    ] = list.slice(0, keep);
  }
  record.truncated = true;
  record.truncationCounts = truncationCounts;
  return record;
}
