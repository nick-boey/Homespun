import {
  query,
  type SDKMessage,
  type Query,
  type PermissionResult,
} from '@anthropic-ai/claude-agent-sdk';
import type { SessionInfo, AskUserQuestionInput, ExitPlanModeInput } from '../types/index.js';
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

interface PendingQuestionState {
  questions: AskUserQuestionInput['questions'];
  resolve: (answers: Record<string, string>) => void;
  reject: (error: Error) => void;
}

interface PendingPlanApprovalState {
  resolve: (approved: boolean) => void;
  reject: (error: Error) => void;
}

interface WorkerSession {
  id: string;
  query: Query;
  inputController: InputController;
  conversationId?: string;
  mode: string;
  model: string;
  permissionMode: SdkPermissionMode;
  status: 'idle' | 'streaming' | 'closed';
  createdAt: Date;
  lastActivityAt: Date;
}

/**
 * Controller for managing streaming input to the V1 query() API.
 * Allows sending messages to an active query session.
 */
class InputController {
  private messageQueue: SDKUserMessage[] = [];
  private resolver: ((value: IteratorResult<SDKUserMessage>) => void) | null = null;
  private done = false;

  async *[Symbol.asyncIterator](): AsyncGenerator<SDKUserMessage> {
    while (!this.done) {
      if (this.messageQueue.length > 0) {
        yield this.messageQueue.shift()!;
      } else {
        // Wait for next message
        const result = await new Promise<IteratorResult<SDKUserMessage>>((resolve) => {
          this.resolver = resolve;
        });
        if (result.done) break;
        yield result.value;
      }
    }
  }

  send(message: string): void {
    const userMessage: SDKUserMessage = {
      type: 'user',
      session_id: '',
      message: {
        role: 'user',
        content: [{ type: 'text', text: message }],
      },
      parent_tool_use_id: null,
    };

    if (this.resolver) {
      this.resolver({ value: userMessage, done: false });
      this.resolver = null;
    } else {
      this.messageQueue.push(userMessage);
    }
  }

  close(): void {
    this.done = true;
    if (this.resolver) {
      this.resolver({ value: undefined as any, done: true });
      this.resolver = null;
    }
  }
}

interface SDKUserMessage {
  type: 'user';
  session_id: string;
  message: {
    role: 'user';
    content: Array<{ type: 'text'; text: string }>;
  };
  parent_tool_use_id: string | null;
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
  private pendingQuestions = new Map<string, PendingQuestionState>();
  private pendingPlanApprovals = new Map<string, PendingPlanApprovalState>();

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

    const inputController = new InputController();

    // Create canUseTool callback that handles AskUserQuestion and ExitPlanMode
    const canUseTool = this.createCanUseToolCallback(id);

    // Build session options
    const sessionOptions: Record<string, unknown> = {
      ...common,
      canUseTool,
    };

    if (isPlan) {
      sessionOptions.permissionMode = 'plan';
      sessionOptions.allowedTools = PLAN_MODE_TOOLS;
    } else {
      sessionOptions.permissionMode = 'bypassPermissions';
      sessionOptions.allowDangerouslySkipPermissions = true;
    }

    if (opts.resumeSessionId) {
      sessionOptions.resume = opts.resumeSessionId;
    }

    console.log(`[Worker][SessionManager] create() - attempting permissionMode='${sessionOptions.permissionMode}', allowDangerouslySkipPermissions=${sessionOptions.allowDangerouslySkipPermissions}`);

    // Create async generator that yields the initial message and subsequent messages
    async function* createInputStream(initialPrompt: string, controller: InputController): AsyncGenerator<SDKUserMessage> {
      // Yield initial prompt
      yield {
        type: 'user',
        session_id: '',
        message: {
          role: 'user',
          content: [{ type: 'text', text: initialPrompt }],
        },
        parent_tool_use_id: null,
      };

      // Yield subsequent messages from the controller
      for await (const msg of controller) {
        yield msg;
      }
    }

    const q = query({
      prompt: createInputStream(opts.prompt, inputController),
      options: sessionOptions as Parameters<typeof query>[0]['options'],
    });

    const workerSession: WorkerSession = {
      id,
      query: q,
      inputController,
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

  /**
   * Creates the canUseTool callback for a session.
   * This callback intercepts AskUserQuestion and ExitPlanMode tools to pause execution
   * until the user provides a response.
   */
  private createCanUseToolCallback(sessionId: string) {
    return async (
      toolName: string,
      input: Record<string, unknown>,
    ): Promise<PermissionResult> => {
      console.log(`[Worker][SessionManager] canUseTool - tool='${toolName}', sessionId='${sessionId}'`);

      if (toolName === 'AskUserQuestion') {
        const questionInput = input as unknown as AskUserQuestionInput;

        // Create promise that will be resolved when /answer endpoint is called
        const answersPromise = new Promise<Record<string, string>>((resolve, reject) => {
          this.pendingQuestions.set(sessionId, {
            questions: questionInput.questions,
            resolve,
            reject,
          });
        });

        console.log(`[Worker][SessionManager] canUseTool - waiting for answers on session '${sessionId}'`);

        // Wait for answers (this pauses SDK execution)
        const answers = await answersPromise;

        console.log(`[Worker][SessionManager] canUseTool - received answers for session '${sessionId}'`);

        // Return allow with populated answers
        return {
          behavior: 'allow',
          updatedInput: {
            questions: questionInput.questions,
            answers,
          },
        };
      }

      if (toolName === 'ExitPlanMode') {
        // Create promise that will be resolved when /approve-plan endpoint is called
        const approvalPromise = new Promise<boolean>((resolve, reject) => {
          this.pendingPlanApprovals.set(sessionId, {
            resolve,
            reject,
          });
        });

        console.log(`[Worker][SessionManager] canUseTool - waiting for plan approval on session '${sessionId}'`);

        // Wait for approval (this pauses SDK execution)
        const approved = await approvalPromise;

        console.log(`[Worker][SessionManager] canUseTool - plan ${approved ? 'approved' : 'rejected'} for session '${sessionId}'`);

        if (approved) {
          return {
            behavior: 'allow',
            updatedInput: input,
          };
        } else {
          return {
            behavior: 'deny',
            message: 'User rejected the plan',
          };
        }
      }

      // Allow other tools
      return {
        behavior: 'allow',
        updatedInput: input,
      };
    };
  }

  /**
   * Resolves a pending question for a session.
   * Called by the /answer endpoint when the user provides answers.
   */
  resolvePendingQuestion(sessionId: string, answers: Record<string, string>): boolean {
    const pending = this.pendingQuestions.get(sessionId);
    if (!pending) {
      console.log(`[Worker][SessionManager] resolvePendingQuestion - no pending question for session '${sessionId}'`);
      return false;
    }

    pending.resolve(answers);
    this.pendingQuestions.delete(sessionId);
    console.log(`[Worker][SessionManager] resolvePendingQuestion - resolved for session '${sessionId}'`);
    return true;
  }

  /**
   * Resolves a pending plan approval for a session.
   * Called by the /approve-plan endpoint when the user approves or rejects the plan.
   */
  resolvePendingPlanApproval(sessionId: string, approved: boolean): boolean {
    const pending = this.pendingPlanApprovals.get(sessionId);
    if (!pending) {
      console.log(`[Worker][SessionManager] resolvePendingPlanApproval - no pending approval for session '${sessionId}'`);
      return false;
    }

    pending.resolve(approved);
    this.pendingPlanApprovals.delete(sessionId);
    console.log(`[Worker][SessionManager] resolvePendingPlanApproval - resolved (approved=${approved}) for session '${sessionId}'`);
    return true;
  }

  /**
   * Checks if a session has a pending question.
   */
  hasPendingQuestion(sessionId: string): boolean {
    return this.pendingQuestions.has(sessionId);
  }

  /**
   * Checks if a session has a pending plan approval.
   */
  hasPendingPlanApproval(sessionId: string): boolean {
    return this.pendingPlanApprovals.has(sessionId);
  }

  /**
   * Gets the pending questions for a session.
   */
  getPendingQuestions(sessionId: string): AskUserQuestionInput['questions'] | undefined {
    return this.pendingQuestions.get(sessionId)?.questions;
  }

  async send(sessionId: string, message: string, model?: string, permissionMode?: string): Promise<WorkerSession> {
    console.log(`[Worker][SessionManager] send() - sessionId='${sessionId}', messageLength=${message?.length}, model=${model || 'default'}, permissionMode=${permissionMode || 'unchanged'}`);
    const ws = this.sessions.get(sessionId);
    if (!ws) {
      throw new Error(`Session ${sessionId} not found`);
    }

    if (permissionMode) {
      ws.permissionMode = mapPermissionMode(permissionMode);
      // Update permission mode on the query if possible
      if (ws.query.setPermissionMode) {
        await ws.query.setPermissionMode(ws.permissionMode);
        console.log(`[Worker][SessionManager] send() - setPermissionMode('${ws.permissionMode}') applied`);
      }
    }

    ws.lastActivityAt = new Date();
    ws.inputController.send(message);
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

    // Reject any pending questions or approvals
    const pendingQuestion = this.pendingQuestions.get(sessionId);
    if (pendingQuestion) {
      pendingQuestion.reject(new Error('Session closed'));
      this.pendingQuestions.delete(sessionId);
    }

    const pendingApproval = this.pendingPlanApprovals.get(sessionId);
    if (pendingApproval) {
      pendingApproval.reject(new Error('Session closed'));
      this.pendingPlanApprovals.delete(sessionId);
    }

    ws.inputController.close();
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
