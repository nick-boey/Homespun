import {
  query,
  type SDKMessage,
  type Query,
  type PermissionResult,
} from '@anthropic-ai/claude-agent-sdk';
import type { SessionInfo, AskUserQuestionInput, ExitPlanModeInput, UserQuestion, ApprovePlanRequest, LastMessageType } from '../types/index.js';
import { randomUUID } from 'node:crypto';
import { info, error } from '../utils/logger.js';

export type SdkPermissionMode = 'default' | 'acceptEdits' | 'plan' | 'bypassPermissions';

const MODE_MAP: Record<string, SdkPermissionMode> = {
  Plan: 'plan',
  Build: 'bypassPermissions',
};

export function mapMode(value: string | undefined): SdkPermissionMode {
  if (!value) return 'bypassPermissions';
  return MODE_MAP[value] ?? 'bypassPermissions';
}

// --- OutputChannel types ---

export interface ControlEvent {
  type: 'question_pending' | 'plan_pending';
  data: { questions: UserQuestion[] } | { plan: string };
}

export type OutputEvent = SDKMessage | ControlEvent;

export function isControlEvent(event: OutputEvent): event is ControlEvent {
  if (!('type' in event) || !('data' in event)) return false;
  const t = (event as ControlEvent).type;
  return t === 'question_pending' || t === 'plan_pending';
}

/**
 * Single-consumer async queue that merges pushes from multiple producers
 * (the SDK query background task and the canUseTool callback).
 */
export class OutputChannel {
  private queue: OutputEvent[] = [];
  private resolver: ((result: IteratorResult<OutputEvent>) => void) | null = null;
  private done = false;

  push(event: OutputEvent): void {
    if (this.done) return;
    if (this.resolver) {
      const resolve = this.resolver;
      this.resolver = null;
      resolve({ value: event, done: false });
    } else {
      this.queue.push(event);
    }
  }

  complete(): void {
    this.done = true;
    if (this.resolver) {
      const resolve = this.resolver;
      this.resolver = null;
      resolve({ value: undefined as any, done: true });
    }
  }

  async *[Symbol.asyncIterator](): AsyncGenerator<OutputEvent> {
    while (true) {
      if (this.queue.length > 0) {
        yield this.queue.shift()!;
      } else if (this.done) {
        return;
      } else {
        const result = await new Promise<IteratorResult<OutputEvent>>((resolve) => {
          this.resolver = resolve;
        });
        if (result.done) return;
        yield result.value;
      }
    }
  }
}

// --- Session types ---

interface PendingQuestionState {
  questions: AskUserQuestionInput['questions'];
  resolve: (answers: Record<string, string>) => void;
  reject: (error: Error) => void;
}

interface PendingPlanApprovalState {
  plan: string;
  resolve: (result: PermissionResult) => void;
  reject: (error: Error) => void;
}

interface WorkerSession {
  id: string;
  query: Query;
  inputController: InputController;
  outputChannel: OutputChannel;
  conversationId?: string;
  mode: string;
  model: string;
  permissionMode: SdkPermissionMode;
  status: 'idle' | 'streaming' | 'closed';
  createdAt: Date;
  lastActivityAt: Date;
  lastMessageType?: LastMessageType;
  lastMessageSubtype?: string;
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
    includePartialMessages: false,
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
    info(`mode='${opts.mode}', isPlan=${isPlan}, model='${opts.model}'`);
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

    info(`attempting permissionMode='${sessionOptions.permissionMode}', allowDangerouslySkipPermissions=${sessionOptions.allowDangerouslySkipPermissions}`);

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

    const outputChannel = new OutputChannel();

    const workerSession: WorkerSession = {
      id,
      query: q,
      inputController,
      outputChannel,
      conversationId: opts.resumeSessionId,
      mode: opts.mode,
      model: opts.model,
      permissionMode: isPlan ? 'plan' : 'bypassPermissions',
      status: 'streaming',
      createdAt: new Date(),
      lastActivityAt: new Date(),
    };

    this.sessions.set(id, workerSession);
    info(`session created, workerSessionId='${id}'`);

    // Background task: forward SDK query messages to the output channel
    (async () => {
      try {
        for await (const msg of q) {
          outputChannel.push(msg);
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        error(`query forwarder error for session '${id}'`, err);
        // Push a synthetic error result so the SSE stream reports the failure
        // instead of ending silently
        outputChannel.push({
          type: 'result',
          subtype: 'error_during_execution',
          session_id: id,
          is_error: true,
          duration_ms: 0,
          duration_api_ms: 0,
          num_turns: 0,
          total_cost_usd: 0,
          usage: { input_tokens: 0, output_tokens: 0, cache_creation_input_tokens: 0, cache_read_input_tokens: 0 },
          result: message,
          errors: [message],
        } as unknown as SDKMessage);
      } finally {
        outputChannel.complete();
      }
    })();

    return workerSession;
  }

  /**
   * Creates the canUseTool callback for a session.
   * Intercepts AskUserQuestion to emit a control event before pausing.
   * Allows ExitPlanMode immediately (server handles plan display/approval).
   */
  private createCanUseToolCallback(sessionId: string) {
    return async (
      toolName: string,
      input: Record<string, unknown>,
    ): Promise<PermissionResult> => {
      info(`canUseTool - tool='${toolName}', sessionId='${sessionId}'`);

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

        // Emit control event BEFORE pausing — this flows to the SSE stream
        // so the server can detect the pending question and show it to the user
        const ws = this.sessions.get(sessionId);
        ws?.outputChannel.push({
          type: 'question_pending',
          data: { questions: questionInput.questions },
        });

        info(`emitted question_pending, waiting for answers on session '${sessionId}'`);

        // Wait for answers (this pauses SDK execution)
        const answers = await answersPromise;

        info(`received answers for session '${sessionId}'`);

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
        const planInput = input as unknown as ExitPlanModeInput;
        const planContent = planInput.plan || '';

        // Create promise that will be resolved when /approve-plan endpoint is called
        const approvalPromise = new Promise<PermissionResult>((resolve, reject) => {
          this.pendingPlanApprovals.set(sessionId, {
            plan: planContent,
            resolve,
            reject,
          });
        });

        // Emit control event BEFORE pausing — this flows to the SSE stream
        // so the server can detect the pending plan and show it to the user
        const ws = this.sessions.get(sessionId);
        ws?.outputChannel.push({
          type: 'plan_pending',
          data: { plan: planContent },
        });

        info(`emitted plan_pending, waiting for approval on session '${sessionId}'`);

        // Wait for approval decision (this pauses SDK execution)
        const result = await approvalPromise;

        info(`received plan approval decision for session '${sessionId}': behavior='${result.behavior}'`);

        return result;
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
      info(`resolvePendingQuestion - no pending question for session '${sessionId}'`);
      return false;
    }

    pending.resolve(answers);
    this.pendingQuestions.delete(sessionId);
    info(`resolvePendingQuestion - resolved for session '${sessionId}'`);
    return true;
  }

  /**
   * Resolves a pending plan approval for a session.
   * Called by the /approve-plan endpoint when the user makes a decision.
   */
  resolvePendingPlanApproval(sessionId: string, approved: boolean, keepContext?: boolean, feedback?: string): boolean {
    const pending = this.pendingPlanApprovals.get(sessionId);
    if (!pending) {
      info(`resolvePendingPlanApproval - no pending approval for session '${sessionId}'`);
      return false;
    }

    let result: PermissionResult;

    if (approved && keepContext) {
      // Approve and continue in same session context
      result = {
        behavior: 'allow',
        updatedInput: { plan: pending.plan },
      };
    } else if (approved && !keepContext) {
      // Approve but signal to interrupt (server will start fresh session with plan)
      result = {
        behavior: 'deny',
        message: 'Plan approved. Interrupting to start fresh implementation session.',
      };
    } else {
      // Reject — agent should revise the plan
      result = {
        behavior: 'deny',
        message: feedback ? `User rejected the plan: ${feedback}` : 'User rejected the plan. Please revise.',
      };
    }

    pending.resolve(result);
    this.pendingPlanApprovals.delete(sessionId);
    info(`resolvePendingPlanApproval - resolved for session '${sessionId}', approved=${approved}, keepContext=${keepContext}`);
    return true;
  }

  /**
   * Checks if a session has a pending plan approval.
   */
  hasPendingPlanApproval(sessionId: string): boolean {
    return this.pendingPlanApprovals.has(sessionId);
  }

  /**
   * Checks if a session has a pending question.
   */
  hasPendingQuestion(sessionId: string): boolean {
    return this.pendingQuestions.has(sessionId);
  }

  /**
   * Gets the pending questions for a session.
   */
  getPendingQuestions(sessionId: string): AskUserQuestionInput['questions'] | undefined {
    return this.pendingQuestions.get(sessionId)?.questions;
  }

  async send(sessionId: string, message: string, model?: string, mode?: string): Promise<WorkerSession> {
    info(`send() - sessionId='${sessionId}', messageLength=${message?.length}, model=${model || 'default'}, mode=${mode || 'unchanged'}`);
    const ws = this.sessions.get(sessionId);
    if (!ws) {
      throw new Error(`Session ${sessionId} not found`);
    }

    // Track model changes when a different model is specified
    if (model) {
      ws.model = model;
      info(`model updated to '${model}'`);
    }

    if (mode) {
      ws.permissionMode = mapMode(mode);
      // Update the mode string to reflect the mode change
      ws.mode = ws.permissionMode === 'plan' ? 'Plan' : 'Build';
      info(`mode updated to '${ws.mode}', permissionMode='${ws.permissionMode}'`);
      // Update permission mode on the query if possible
      if (ws.query.setPermissionMode) {
        await ws.query.setPermissionMode(ws.permissionMode);
        info(`setPermissionMode('${ws.permissionMode}') applied`);
      }
    }

    ws.lastActivityAt = new Date();
    ws.inputController.send(message);
    ws.status = 'streaming';

    return ws;
  }

  async *stream(sessionId: string): AsyncGenerator<OutputEvent> {
    const ws = this.sessions.get(sessionId);
    if (!ws) {
      throw new Error(`Session ${sessionId} not found`);
    }

    try {
      for await (const event of ws.outputChannel) {
        if (isControlEvent(event)) {
          info(`stream() - control event: type='${event.type}'`);
          // Track control event as last message type
          ws.lastMessageType = event.type;
          ws.lastMessageSubtype = undefined;
          ws.lastActivityAt = new Date();
          yield event;
          continue;
        }

        // SDK message — capture conversation ID and track message type
        const msg = event;
        if (msg.session_id) {
          ws.conversationId = msg.session_id;
        }

        // Track SDK message type
        ws.lastMessageType = msg.type as LastMessageType;
        ws.lastMessageSubtype = (msg as any).subtype;
        ws.lastActivityAt = new Date();

        if (msg.type === 'system' && (msg as any).subtype === 'init') {
          info(`SDK init: permissionMode='${(msg as any).permissionMode}', model='${(msg as any).model}'`);
        }
        if (msg.type === 'result') {
          const r = msg as any;
          info(`result: subtype='${r.subtype}', is_error=${r.is_error}`);
        }
        yield event;
      }
    } finally {
      ws.status = 'idle';
    }
  }

  async close(sessionId: string): Promise<void> {
    const ws = this.sessions.get(sessionId);
    if (!ws) return;

    // Reject any pending questions
    const pendingQuestion = this.pendingQuestions.get(sessionId);
    if (pendingQuestion) {
      pendingQuestion.reject(new Error('Session closed'));
      this.pendingQuestions.delete(sessionId);
    }

    // Reject any pending plan approvals
    const pendingPlan = this.pendingPlanApprovals.get(sessionId);
    if (pendingPlan) {
      pendingPlan.reject(new Error('Session closed'));
      this.pendingPlanApprovals.delete(sessionId);
    }

    ws.outputChannel.complete();
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
      permissionMode: ws.permissionMode,
      status: ws.status,
      createdAt: ws.createdAt.toISOString(),
      lastActivityAt: ws.lastActivityAt.toISOString(),
      lastMessageType: ws.lastMessageType,
      lastMessageSubtype: ws.lastMessageSubtype,
    }));
  }

  async closeAll(): Promise<void> {
    for (const [id] of this.sessions) {
      await this.close(id);
    }
  }
}
