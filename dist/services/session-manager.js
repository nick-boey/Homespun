import { unstable_v2_createSession, unstable_v2_resumeSession, } from '@anthropic-ai/claude-agent-sdk';
import { randomUUID } from 'node:crypto';
const PERMISSION_MODE_MAP = {
    Default: 'default',
    AcceptEdits: 'acceptEdits',
    Plan: 'plan',
    BypassPermissions: 'bypassPermissions',
};
export function mapPermissionMode(value) {
    if (!value)
        return 'bypassPermissions';
    return PERMISSION_MODE_MAP[value] ?? 'bypassPermissions';
}
const PLAN_MODE_TOOLS = [
    'Read', 'Glob', 'Grep', 'WebFetch', 'WebSearch',
    'Task', 'AskUserQuestion', 'ExitPlanMode',
];
function buildCommonOptions(model, systemPrompt, workingDirectory) {
    const cwd = workingDirectory || process.env.WORKING_DIRECTORY || '/workdir';
    const systemPromptOption = systemPrompt
        ? { type: 'preset', preset: 'claude_code', append: systemPrompt }
        : { type: 'preset', preset: 'claude_code' };
    const gitAuthorName = process.env.GIT_AUTHOR_NAME || 'Homespun Bot';
    const gitAuthorEmail = process.env.GIT_AUTHOR_EMAIL || 'homespun@localhost';
    const githubToken = process.env.GITHUB_TOKEN || process.env.GitHub__Token || '';
    return {
        model,
        cwd,
        includePartialMessages: true,
        settingSources: ['user', 'project'],
        systemPrompt: systemPromptOption,
        mcpServers: {
            playwright: {
                type: 'stdio',
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
    sessions = new Map();
    async create(opts) {
        const id = randomUUID();
        const isPlan = opts.mode.toLowerCase() === 'plan';
        console.log(`[Worker][SessionManager] create() - mode='${opts.mode}', isPlan=${isPlan}, model='${opts.model}'`);
        const common = buildCommonOptions(opts.model, opts.systemPrompt, opts.workingDirectory);
        const sessionOptions = {
            ...common,
        };
        if (isPlan) {
            sessionOptions.permissionMode = 'plan';
            sessionOptions.allowedTools = PLAN_MODE_TOOLS;
        }
        else {
            sessionOptions.permissionMode = 'bypassPermissions';
            sessionOptions.allowDangerouslySkipPermissions = true;
        }
        console.log(`[Worker][SessionManager] create() - attempting permissionMode='${sessionOptions.permissionMode}', allowDangerouslySkipPermissions=${sessionOptions.allowDangerouslySkipPermissions}`);
        let session;
        if (opts.resumeSessionId) {
            session = unstable_v2_resumeSession(opts.resumeSessionId, sessionOptions);
        }
        else {
            session = unstable_v2_createSession(sessionOptions);
        }
        const workerSession = {
            id,
            session,
            conversationId: opts.resumeSessionId,
            mode: opts.mode,
            model: opts.model,
            permissionMode: isPlan ? 'plan' : 'bypassPermissions',
            status: 'idle',
            createdAt: new Date(),
            lastActivityAt: new Date(),
        };
        this.sessions.set(id, workerSession);
        console.log(`[Worker][SessionManager] create() - session created, workerSessionId='${id}'`);
        // Send the initial prompt
        await session.send(opts.prompt);
        workerSession.status = 'streaming';
        return workerSession;
    }
    async send(sessionId, message, model, permissionMode) {
        console.log(`[Worker][SessionManager] send() - sessionId='${sessionId}', messageLength=${message?.length}, model=${model || 'default'}, permissionMode=${permissionMode || 'unchanged'}`);
        const ws = this.sessions.get(sessionId);
        if (!ws) {
            throw new Error(`Session ${sessionId} not found`);
        }
        if (permissionMode) {
            ws.permissionMode = mapPermissionMode(permissionMode);
        }
        ws.lastActivityAt = new Date();
        await ws.session.send(message);
        ws.status = 'streaming';
        return ws;
    }
    async *stream(sessionId) {
        const ws = this.sessions.get(sessionId);
        if (!ws) {
            throw new Error(`Session ${sessionId} not found`);
        }
        try {
            let permissionModeSet = false;
            for await (const msg of ws.session.stream()) {
                // Capture the conversation ID from any message
                if (msg.session_id) {
                    ws.conversationId = msg.session_id;
                }
                // After first message (process is running), set the correct permission mode.
                // SessionImpl hardcodes permissionMode="default", so we override at runtime.
                // Uses ws.permissionMode which may have been updated by send() for per-message overrides.
                if (!permissionModeSet) {
                    permissionModeSet = true;
                    const query = ws.session.query;
                    if (query?.setPermissionMode) {
                        await query.setPermissionMode(ws.permissionMode);
                        console.log(`[Worker][SessionManager] stream() - setPermissionMode('${ws.permissionMode}') applied`);
                    }
                }
                if (msg.type === 'system' && msg.subtype === 'init') {
                    console.log(`[Worker][SessionManager] stream() - SDK init: permissionMode='${msg.permissionMode}', model='${msg.model}'`);
                }
                if (msg.type === 'result') {
                    const r = msg;
                    console.log(`[Worker][SessionManager] stream() - result: subtype='${r.subtype}', is_error=${r.is_error}`);
                }
                yield msg;
            }
        }
        finally {
            ws.status = 'idle';
        }
    }
    async close(sessionId) {
        const ws = this.sessions.get(sessionId);
        if (!ws)
            return;
        ws.session.close();
        ws.status = 'closed';
        this.sessions.delete(sessionId);
    }
    get(sessionId) {
        return this.sessions.get(sessionId);
    }
    list() {
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
    async closeAll() {
        for (const [id] of this.sessions) {
            await this.close(id);
        }
    }
}
//# sourceMappingURL=session-manager.js.map