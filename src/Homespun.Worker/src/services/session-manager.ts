import {
  query as createQuery,
  type SDKMessage,
  type Query,
} from '@anthropic-ai/claude-agent-sdk';
import type { SessionInfo } from '../types/index.js';
import { randomUUID } from 'node:crypto';

export type SdkPermissionMode = 'default' | 'acceptEdits' | 'plan' | 'bypassPermissions';

const PERMISSION_MODE_MAP: Record<string, SdkPermissionMode> = {
  Default: 'default',
  AcceptEdits: 'acceptEdits',
  Plan: 'plan',
  BypassPermissions: 'bypassPermissions',
};

export function mapPermissionMode(value: string | undefined): SdkPermissionMode {
  if (!value) return 'bypassPermissions';
  return PERMISSION_MODE_MAP[value] ?? 'bypassPermissions';
}

interface WorkerSession {
  id: string;
  query: Query;
  abortController: AbortController;
  commonOptions: Record<string, unknown>;
  conversationId?: string;
  mode: string;
  model: string;
  permissionMode: SdkPermissionMode;
  status: 'idle' | 'streaming' | 'closed';
  createdAt: Date;
  lastActivityAt: Date;
}

const PLAN_MODE_TOOLS = [
  'Read', 'Glob', 'Grep', 'WebFetch', 'WebSearch',
  'Task', 'AskUserQuestion', 'ExitPlanMode',
];

function buildCommonOptions(model: string, systemPrompt?: string, workingDirectory?: string) {
  const cwd = workingDirectory || process.env.WORKING_DIRECTORY || '/workdir';

  const systemPromptOption = systemPrompt
    ? { type: 'preset' as const, preset: 'claude_code' as const, append: systemPrompt }
    : { type: 'preset' as const, preset: 'claude_code' as const };

  const gitAuthorName = process.env.GIT_AUTHOR_NAME || 'Homespun Bot';
  const gitAuthorEmail = process.env.GIT_AUTHOR_EMAIL || 'homespun@localhost';
  const githubToken = process.env.GITHUB_TOKEN || process.env.GitHub__Token || '';

  return {
    model,
    cwd,
    includePartialMessages: true,
    settingSources: ['user' as const, 'project' as const],
    systemPrompt: systemPromptOption,
    mcpServers: {
      playwright: {
        type: 'stdio' as const,
        command: 'npx',
        args: ['@playwright/mcp@latest', '--headless', '--browser', 'chromium', '--no-sandbox', '--isolated'],
        env: {
          PLAYWRIGHT_BROWSERS_PATH: process.env.PLAYWRIGHT_BROWSERS_PATH || '/opt/playwright-browsers',
        },
      },
    },
    env: {
      ...process.env,
      GIT_AUTHOR_NAME: gitAuthorName,
      GIT_AUTHOR_EMAIL: gitAuthorEmail,
      GIT_COMMITTER_NAME: process.env.GIT_COMMITTER_NAME || gitAuthorName,
      GIT_COMMITTER_EMAIL: process.env.GIT_COMMITTER_EMAIL || gitAuthorEmail,
      GITHUB_TOKEN: githubToken,
      GH_TOKEN: githubToken,
    },
  };
}

export class SessionManager {
  private sessions = new Map<string, WorkerSession>();

  async create(opts: {
    prompt: string;
    model: string;
    mode: string;
    systemPrompt?: string;
    workingDirectory?: string;
    resumeSessionId?: string;
  }): Promise<WorkerSession> {
    const id = randomUUID();
    const isPlan = opts.mode.toLowerCase() === 'plan';
    console.log(`[Worker][SessionManager] create() - mode='${opts.mode}', isPlan=${isPlan}, model='${opts.model}'`);
    const common = buildCommonOptions(opts.model, opts.systemPrompt, opts.workingDirectory);

    const abortController = new AbortController();
    const queryOptions: Record<string, unknown> = {
      ...common,
      abortController,
      permissionMode: isPlan ? 'plan' : 'bypassPermissions',
      allowDangerouslySkipPermissions: !isPlan,
      ...(isPlan && { allowedTools: PLAN_MODE_TOOLS }),
      ...(opts.resumeSessionId && { resume: opts.resumeSessionId }),
    };
    console.log(`[Worker][SessionManager] create() - permissionMode='${queryOptions.permissionMode}', allowDangerouslySkipPermissions=${queryOptions.allowDangerouslySkipPermissions}, cwd='${common.cwd}'`);

    const q = createQuery({ prompt: opts.prompt, options: queryOptions as any });

    const workerSession: WorkerSession = {
      id,
      query: q,
      abortController,
      commonOptions: common,
      conversationId: opts.resumeSessionId,
      mode: opts.mode,
      model: opts.model,
      permissionMode: isPlan ? 'plan' : 'bypassPermissions',
      status: 'streaming',
      createdAt: new Date(),
      lastActivityAt: new Date(),
    };

    this.sessions.set(id, workerSession);
    console.log(`[Worker][SessionManager] create() - session created, workerSessionId='${id}'`);

    return workerSession;
  }

  async send(sessionId: string, message: string, model?: string, permissionMode?: string): Promise<WorkerSession> {
    console.log(`[Worker][SessionManager] send() - sessionId='${sessionId}', messageLength=${message?.length}, model=${model || 'default'}, permissionMode=${permissionMode || 'unchanged'}`);
    const ws = this.sessions.get(sessionId);
    if (!ws) {
      throw new Error(`Session ${sessionId} not found`);
    }

    if (permissionMode) {
      ws.permissionMode = mapPermissionMode(permissionMode);
    }

    const abortController = new AbortController();
    const queryOptions: Record<string, unknown> = {
      ...ws.commonOptions,
      abortController,
      model: model || ws.model,
      permissionMode: ws.permissionMode,
      allowDangerouslySkipPermissions: ws.permissionMode === 'bypassPermissions',
      resume: ws.conversationId,
    };

    ws.query = createQuery({ prompt: message, options: queryOptions as any });
    ws.abortController = abortController;
    ws.lastActivityAt = new Date();
    ws.status = 'streaming';

    return ws;
  }

  async *stream(sessionId: string): AsyncGenerator<SDKMessage> {
    const ws = this.sessions.get(sessionId);
    if (!ws) {
      throw new Error(`Session ${sessionId} not found`);
    }

    try {
      for await (const msg of ws.query) {
        // Capture the conversation ID from any message
        if (msg.session_id) {
          ws.conversationId = msg.session_id;
        }

        if (msg.type === 'system' && (msg as any).subtype === 'init') {
          console.log(`[Worker][SessionManager] stream() - SDK init: permissionMode='${(msg as any).permissionMode}', model='${(msg as any).model}'`);
        }
        if (msg.type === 'result') {
          const r = msg as any;
          console.log(`[Worker][SessionManager] stream() - result: subtype='${r.subtype}', is_error=${r.is_error}`);
        }
        yield msg;
      }
    } finally {
      ws.status = 'idle';
    }
  }

  async close(sessionId: string): Promise<void> {
    const ws = this.sessions.get(sessionId);
    if (!ws) return;

    ws.abortController.abort();
    ws.status = 'closed';
    this.sessions.delete(sessionId);
  }

  get(sessionId: string): WorkerSession | undefined {
    return this.sessions.get(sessionId);
  }

  list(): SessionInfo[] {
    return Array.from(this.sessions.values()).map((ws) => ({
      sessionId: ws.id,
      conversationId: ws.conversationId,
      mode: ws.mode,
      model: ws.model,
      status: ws.status,
      createdAt: ws.createdAt.toISOString(),
      lastActivityAt: ws.lastActivityAt.toISOString(),
    }));
  }

  async closeAll(): Promise<void> {
    for (const [id] of this.sessions) {
      await this.close(id);
    }
  }
}
