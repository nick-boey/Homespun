/**
 * Shared fixture builder for `SDKSystemMessage` with `subtype: 'init'`, shaped
 * per specs/001-worker-skills-logging/research.md §R1. Used by
 * `session-inventory.test.ts` and `session-manager-logging.test.ts`.
 */
import type { SDKMessage } from "@anthropic-ai/claude-agent-sdk";

export interface SdkInitFixtureOverrides {
  session_id?: string;
  cwd?: string;
  claude_code_version?: string;
  model?: string;
  permissionMode?: "default" | "acceptEdits" | "plan" | "bypassPermissions";
  agents?: string[];
  tools?: string[];
  mcp_servers?: { name: string; status: string }[];
  slash_commands?: string[];
  skills?: string[];
  plugins?: { name: string; path: string }[];
}

/**
 * Default init message covering all six resource categories with sensible
 * non-empty values, plus the surrounding SDK-required fields.
 */
export function createSdkInitMessage(
  overrides: SdkInitFixtureOverrides = {},
): SDKMessage {
  const base = {
    type: "system" as const,
    subtype: "init" as const,
    uuid: "11111111-1111-1111-1111-111111111111" as const,
    apiKeySource: "user" as const,
    output_style: "default",
    betas: [],
    cwd: "/workdir",
    session_id: "test-session-123",
    claude_code_version: "1.7.3",
    model: "claude-opus-4-6",
    permissionMode: "bypassPermissions" as const,
    tools: [
      "Read",
      "Write",
      "Edit",
      "Bash",
      "Glob",
      "Grep",
      "mcp__playwright__browser_click",
    ],
    mcp_servers: [{ name: "playwright", status: "connected" }],
    slash_commands: ["speckit-specify", "subframe:design"],
    skills: ["superpowers:brainstorming", "update-config"],
    agents: ["code-reviewer"],
    plugins: [{ name: "subframe", path: "/root/.claude/plugins/subframe" }],
  };

  return { ...base, ...overrides } as unknown as SDKMessage;
}
