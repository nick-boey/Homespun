export function formatSSE(event, data) {
    return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}
/**
 * Streams raw SDK messages as SSE-formatted strings.
 * Each SDK message is emitted with its `type` field as the SSE event name.
 * The C# consumer handles all content block assembly and question parsing.
 */
export async function* streamSessionEvents(sessionManager, sessionId) {
    const ws = sessionManager.get(sessionId);
    if (!ws) {
        yield formatSSE('error', {
            sessionId,
            message: `Session ${sessionId} not found`,
            code: 'SESSION_NOT_FOUND',
            isRecoverable: false,
        });
        return;
    }
    // Emit session started (lifecycle event, not an SDK message)
    yield formatSSE('session_started', {
        sessionId,
        conversationId: ws.conversationId,
    });
    try {
        for await (const msg of sessionManager.stream(sessionId)) {
            if (msg.type === 'system') {
                console.log(`[Worker][SSE] system message: subtype='${msg.subtype}', permissionMode='${msg.permissionMode || 'N/A'}'`);
            }
            if (msg.type === 'result') {
                console.log(`[Worker][SSE] result: subtype='${msg.subtype}'`);
            }
            yield formatSSE(msg.type, msg);
            if (msg.type === 'result') {
                return;
            }
        }
    }
    catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        yield formatSSE('error', {
            sessionId,
            message,
            code: 'AGENT_ERROR',
            isRecoverable: false,
        });
    }
}
//# sourceMappingURL=sse-writer.js.map