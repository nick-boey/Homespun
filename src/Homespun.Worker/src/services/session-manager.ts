import {
  unstable_v2_createSession,
  unstable_v2_resumeSession,
  type SDKMessage,
} from '@anthropic-ai/claude-agent-sdk';
import type { SessionInfo } from '../types/index.js';
import { randomUUID } from 'node:crypto';

type Session = ReturnType<typeof unstable_v2_createSession>;

interface WorkerSession {
  id: string;
  session: Session;
  conversationId?: string;
  mode: string;
  model: string;
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
    const common = buildCommonOptions(opts.model, opts.systemPrompt, opts.workingDirectory);

    const sessionOptions: Record<string, unknown> = {
      ...common,
    };

    if (isPlan) {
      sessionOptions.permissionMode = 'plan';
      sessionOptions.allowedTools = PLAN_MODE_TOOLS;
    } else {
      sessionOptions.permissionMode = 'bypassPermissions';
      sessionOptions.allowDangerouslySkipPermissions = true;
    }

    let session: Session;
    if (opts.resumeSessionId) {
      session = unstable_v2_resumeSession(opts.resumeSessionId, sessionOptions as Parameters<typeof unstable_v2_resumeSession>[1]);
    } else {
      session = unstable_v2_createSession(sessionOptions as Parameters<typeof unstable_v2_createSession>[0]);
    }

    const workerSession: WorkerSession = {
      id,
      session,
      conversationId: opts.resumeSessionId,
      mode: opts.mode,
      model: opts.model,
      status: 'idle',
      createdAt: new Date(),
      lastActivityAt: new Date(),
    };

    this.sessions.set(id, workerSession);

    // Send the initial prompt
    await session.send(opts.prompt);
    workerSession.status = 'streaming';

    return workerSession;
  }

  async send(sessionId: string, message: string, model?: string): Promise<WorkerSession> {
    const ws = this.sessions.get(sessionId);
    if (!ws) {
      throw new Error(`Session ${sessionId} not found`);
    }

    ws.lastActivityAt = new Date();
    await ws.session.send(message);
    ws.status = 'streaming';

    return ws;
  }

  async *stream(sessionId: string): AsyncGenerator<SDKMessage> {
    const ws = this.sessions.get(sessionId);
    if (!ws) {
      throw new Error(`Session ${sessionId} not found`);
    }

    try {
      for await (const msg of ws.session.stream()) {
        // Capture the conversation ID from any message
        if (msg.session_id) {
          ws.conversationId = msg.session_id;
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

    ws.session.close();
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
