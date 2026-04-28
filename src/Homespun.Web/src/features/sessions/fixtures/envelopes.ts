import type { AGUIEvent, SessionEventEnvelope } from '@/types/session-events'
import { AGUICustomEventName } from '@/types/session-events'

const SESSION_ID = 'story-session'

function env(seq: number, eventId: string, event: AGUIEvent): SessionEventEnvelope {
  return { seq, sessionId: SESSION_ID, eventId, event }
}

export const simpleTextTurn: SessionEventEnvelope[] = [
  env(1, 'e1', { type: 'RUN_STARTED', threadId: SESSION_ID, runId: 'r1', timestamp: 0 }),
  env(2, 'e2', {
    type: 'TEXT_MESSAGE_START',
    messageId: 'u1',
    role: 'user',
    timestamp: 1,
  }),
  env(3, 'e3', {
    type: 'TEXT_MESSAGE_CONTENT',
    messageId: 'u1',
    delta: 'List the files in this repo.',
    timestamp: 2,
  }),
  env(4, 'e4', { type: 'TEXT_MESSAGE_END', messageId: 'u1', timestamp: 3 }),
  env(5, 'e5', {
    type: 'TEXT_MESSAGE_START',
    messageId: 'a1',
    role: 'assistant',
    timestamp: 4,
  }),
  env(6, 'e6', {
    type: 'TEXT_MESSAGE_CONTENT',
    messageId: 'a1',
    delta: "Sure — here's the file list.",
    timestamp: 5,
  }),
  env(7, 'e7', { type: 'TEXT_MESSAGE_END', messageId: 'a1', timestamp: 6 }),
  env(8, 'e8', { type: 'RUN_FINISHED', threadId: SESSION_ID, runId: 'r1', timestamp: 7 }),
]

export const toolCallLifecycle: SessionEventEnvelope[] = [
  env(1, 'e1', { type: 'RUN_STARTED', threadId: SESSION_ID, runId: 'r1', timestamp: 0 }),
  env(2, 'e2', {
    type: 'TEXT_MESSAGE_START',
    messageId: 'a1',
    role: 'assistant',
    timestamp: 1,
  }),
  env(3, 'e3', {
    type: 'TEXT_MESSAGE_CONTENT',
    messageId: 'a1',
    delta: 'Running a Bash command…',
    timestamp: 2,
  }),
  env(4, 'e4', { type: 'TEXT_MESSAGE_END', messageId: 'a1', timestamp: 3 }),
  env(5, 'e5', {
    type: 'TOOL_CALL_START',
    toolCallId: 'tc1',
    toolCallName: 'Bash',
    parentMessageId: 'a1',
    timestamp: 4,
  }),
  env(6, 'e6', {
    type: 'TOOL_CALL_ARGS',
    toolCallId: 'tc1',
    delta: '{"command":"ls -la"}',
    timestamp: 5,
  }),
  env(7, 'e7', { type: 'TOOL_CALL_END', toolCallId: 'tc1', timestamp: 6 }),
  env(8, 'e8', {
    type: 'TOOL_CALL_RESULT',
    toolCallId: 'tc1',
    content: 'total 4\ndrwxr-xr-x 2 user user 4096 Apr 22 src\n',
    messageId: 'a1',
    role: 'tool',
    timestamp: 7,
  }),
  env(9, 'e9', { type: 'RUN_FINISHED', threadId: SESSION_ID, runId: 'r1', timestamp: 8 }),
]

export const thinkingBlock: SessionEventEnvelope[] = [
  env(1, 'e1', { type: 'RUN_STARTED', threadId: SESSION_ID, runId: 'r1', timestamp: 0 }),
  env(2, 'e2', {
    type: 'CUSTOM',
    name: AGUICustomEventName.Thinking,
    value: {
      text: 'Considering the shape of the problem before answering.',
      parentMessageId: 'a1',
    },
    timestamp: 1,
  }),
  env(3, 'e3', {
    type: 'TEXT_MESSAGE_START',
    messageId: 'a1',
    role: 'assistant',
    timestamp: 2,
  }),
  env(4, 'e4', {
    type: 'TEXT_MESSAGE_CONTENT',
    messageId: 'a1',
    delta: 'Here is my answer.',
    timestamp: 3,
  }),
  env(5, 'e5', { type: 'TEXT_MESSAGE_END', messageId: 'a1', timestamp: 4 }),
  env(6, 'e6', { type: 'RUN_FINISHED', threadId: SESSION_ID, runId: 'r1', timestamp: 5 }),
]

export const multiBlockTurn: SessionEventEnvelope[] = [
  ...thinkingBlock.slice(0, -1),
  env(6, 'e6', {
    type: 'TOOL_CALL_START',
    toolCallId: 'tc-mb',
    toolCallName: 'Read',
    parentMessageId: 'a1',
    timestamp: 6,
  }),
  env(7, 'e7', {
    type: 'TOOL_CALL_ARGS',
    toolCallId: 'tc-mb',
    delta: '{"path":"README.md"}',
    timestamp: 7,
  }),
  env(8, 'e8', { type: 'TOOL_CALL_END', toolCallId: 'tc-mb', timestamp: 8 }),
  env(9, 'e9', {
    type: 'TOOL_CALL_RESULT',
    toolCallId: 'tc-mb',
    content: '# Homespun\n\nHomespun is a web application…',
    messageId: 'a1',
    role: 'tool',
    timestamp: 9,
  }),
  env(10, 'e10', { type: 'RUN_FINISHED', threadId: SESSION_ID, runId: 'r1', timestamp: 10 }),
]

// ---------------- Interactive tool calls (ask_user_question, propose_plan) ----------------
//
// Questions and plans used to be modelled as modal CustomEvents (question.pending,
// plan.pending). Per the questions-plans-as-tools change, they are now canonical
// TOOL_CALL_* sequences. The fixtures come in two flavours per tool:
//   - *Pending — start/args/end only, no TOOL_CALL_RESULT. The Toolkit renders the
//     interactive affordance.
//   - *Answered / *Approved / *Rejected — same three plus a TOOL_CALL_RESULT
//     carrying the user's committed decision. The Toolkit renders in receipt mode.

const ASK_QUESTION_ARGS_JSON = JSON.stringify({
  id: 'q1',
  toolUseId: 'q1',
  questions: [
    {
      question: 'Should we proceed with deleting the scroll-to-bottom component?',
      header: 'Confirm delete',
      options: [
        { label: 'Yes, delete it', description: 'Remove the component and its tests' },
        { label: 'Keep it for now', description: 'Defer the cleanup to a later change' },
      ],
      multiSelect: false,
    },
  ],
})

const PROPOSE_PLAN_ARGS_JSON = JSON.stringify({
  planContent:
    '## Plan\n\n1. Update the `ChatSurface` wrapper\n2. Port tool renderers\n3. Delete old components',
  planFilePath: 'openspec/changes/chat-assistant-ui/plan.md',
})

export const askUserQuestionPending: SessionEventEnvelope[] = [
  env(1, 'e1', { type: 'RUN_STARTED', threadId: SESSION_ID, runId: 'r1', timestamp: 0 }),
  env(2, 'e2', {
    type: 'TOOL_CALL_START',
    toolCallId: 'tc-askq',
    toolCallName: 'ask_user_question',
    timestamp: 1,
  }),
  env(3, 'e3', {
    type: 'TOOL_CALL_ARGS',
    toolCallId: 'tc-askq',
    delta: ASK_QUESTION_ARGS_JSON,
    timestamp: 2,
  }),
  env(4, 'e4', { type: 'TOOL_CALL_END', toolCallId: 'tc-askq', timestamp: 3 }),
]

export const askUserQuestionAnswered: SessionEventEnvelope[] = [
  ...askUserQuestionPending,
  env(5, 'e5', {
    type: 'TOOL_CALL_RESULT',
    toolCallId: 'tc-askq',
    content: JSON.stringify({
      'Should we proceed with deleting the scroll-to-bottom component?': 'Yes, delete it',
    }),
    role: 'tool',
    timestamp: 4,
  }),
]

export const proposePlanPending: SessionEventEnvelope[] = [
  env(1, 'e1', { type: 'RUN_STARTED', threadId: SESSION_ID, runId: 'r1', timestamp: 0 }),
  env(2, 'e2', {
    type: 'TOOL_CALL_START',
    toolCallId: 'tc-plan',
    toolCallName: 'propose_plan',
    timestamp: 1,
  }),
  env(3, 'e3', {
    type: 'TOOL_CALL_ARGS',
    toolCallId: 'tc-plan',
    delta: PROPOSE_PLAN_ARGS_JSON,
    timestamp: 2,
  }),
  env(4, 'e4', { type: 'TOOL_CALL_END', toolCallId: 'tc-plan', timestamp: 3 }),
]

export const proposePlanApproved: SessionEventEnvelope[] = [
  ...proposePlanPending,
  env(5, 'e5', {
    type: 'TOOL_CALL_RESULT',
    toolCallId: 'tc-plan',
    content: JSON.stringify({ approved: true, keepContext: true, feedback: null }),
    role: 'tool',
    timestamp: 4,
  }),
]

export const proposePlanRejected: SessionEventEnvelope[] = [
  ...proposePlanPending,
  env(5, 'e5', {
    type: 'TOOL_CALL_RESULT',
    toolCallId: 'tc-plan',
    content: JSON.stringify({
      approved: false,
      keepContext: false,
      feedback: 'Revise step 2 — port renderers to the Toolkit API first',
    }),
    role: 'tool',
    timestamp: 4,
  }),
]

export const unknownToolCall: SessionEventEnvelope[] = [
  env(1, 'e1', { type: 'RUN_STARTED', threadId: SESSION_ID, runId: 'r1', timestamp: 0 }),
  env(2, 'e2', {
    type: 'TOOL_CALL_START',
    toolCallId: 'tc-search',
    toolCallName: 'ToolSearch',
    timestamp: 1,
  }),
  env(3, 'e3', {
    type: 'TOOL_CALL_ARGS',
    toolCallId: 'tc-search',
    delta: JSON.stringify({ query: 'select:Read', max_results: 1 }),
    timestamp: 2,
  }),
  env(4, 'e4', { type: 'TOOL_CALL_END', toolCallId: 'tc-search', timestamp: 3 }),
  env(5, 'e5', {
    type: 'TOOL_CALL_RESULT',
    toolCallId: 'tc-search',
    content: '[{"type":"tool_reference","tool_name":"Read"}]',
    role: 'tool',
    timestamp: 4,
  }),
  env(6, 'e6', { type: 'RUN_FINISHED', threadId: SESSION_ID, runId: 'r1', timestamp: 5 }),
]

export const runError: SessionEventEnvelope[] = [
  env(1, 'e1', { type: 'RUN_STARTED', threadId: SESSION_ID, runId: 'r1', timestamp: 0 }),
  env(2, 'e2', {
    type: 'TEXT_MESSAGE_START',
    messageId: 'a1',
    role: 'assistant',
    timestamp: 1,
  }),
  env(3, 'e3', {
    type: 'TEXT_MESSAGE_CONTENT',
    messageId: 'a1',
    delta: 'Starting work…',
    timestamp: 2,
  }),
  env(4, 'e4', { type: 'TEXT_MESSAGE_END', messageId: 'a1', timestamp: 3 }),
  env(5, 'e5', {
    type: 'RUN_ERROR',
    message: 'Worker timed out while executing Bash(sleep 999)',
    code: 'WORKER_TIMEOUT',
    timestamp: 4,
  }),
]

/**
 * Reasoning-only turn that is still streaming — no TEXT/TOOL parts arrive.
 * Used to exercise the `Reasoning` group's "expanded while it is the only/last
 * streaming part" behaviour.
 */
export const reasoningStreaming: SessionEventEnvelope[] = [
  env(1, 'e1', { type: 'RUN_STARTED', threadId: SESSION_ID, runId: 'r1', timestamp: 0 }),
  env(2, 'e2', {
    type: 'CUSTOM',
    name: AGUICustomEventName.Thinking,
    value: {
      text: 'Considering the shape of the problem before answering — still working on the plan…',
      parentMessageId: 'a1',
    },
    timestamp: 1,
  }),
  // No RUN_FINISHED — message is still running.
]

/**
 * Assistant turn with four consecutive tool calls (Bash → Read → Grep → Write)
 * followed by a closing text part. Exercises the `ToolGroup` collapsible.
 */
export const multiToolGroup: SessionEventEnvelope[] = [
  env(1, 'e1', { type: 'RUN_STARTED', threadId: SESSION_ID, runId: 'r1', timestamp: 0 }),
  // Bash
  env(2, 'e2', {
    type: 'TOOL_CALL_START',
    toolCallId: 'tc-bash',
    toolCallName: 'Bash',
    parentMessageId: 'a1',
    timestamp: 1,
  }),
  env(3, 'e3', {
    type: 'TOOL_CALL_ARGS',
    toolCallId: 'tc-bash',
    delta: '{"command":"ls -la"}',
    timestamp: 2,
  }),
  env(4, 'e4', { type: 'TOOL_CALL_END', toolCallId: 'tc-bash', timestamp: 3 }),
  env(5, 'e5', {
    type: 'TOOL_CALL_RESULT',
    toolCallId: 'tc-bash',
    content: 'total 4\ndrwxr-xr-x 2 user user 4096 Apr 22 src\n',
    messageId: 'a1',
    role: 'tool',
    timestamp: 4,
  }),
  // Read
  env(6, 'e6', {
    type: 'TOOL_CALL_START',
    toolCallId: 'tc-read',
    toolCallName: 'Read',
    parentMessageId: 'a1',
    timestamp: 5,
  }),
  env(7, 'e7', {
    type: 'TOOL_CALL_ARGS',
    toolCallId: 'tc-read',
    delta: '{"file_path":"README.md"}',
    timestamp: 6,
  }),
  env(8, 'e8', { type: 'TOOL_CALL_END', toolCallId: 'tc-read', timestamp: 7 }),
  env(9, 'e9', {
    type: 'TOOL_CALL_RESULT',
    toolCallId: 'tc-read',
    content: '# Project',
    messageId: 'a1',
    role: 'tool',
    timestamp: 8,
  }),
  // Grep
  env(10, 'e10', {
    type: 'TOOL_CALL_START',
    toolCallId: 'tc-grep',
    toolCallName: 'Grep',
    parentMessageId: 'a1',
    timestamp: 9,
  }),
  env(11, 'e11', {
    type: 'TOOL_CALL_ARGS',
    toolCallId: 'tc-grep',
    delta: '{"pattern":"TODO","path":"src"}',
    timestamp: 10,
  }),
  env(12, 'e12', { type: 'TOOL_CALL_END', toolCallId: 'tc-grep', timestamp: 11 }),
  env(13, 'e13', {
    type: 'TOOL_CALL_RESULT',
    toolCallId: 'tc-grep',
    content: 'src/foo.ts:12: // TODO: revisit',
    messageId: 'a1',
    role: 'tool',
    timestamp: 12,
  }),
  // Write
  env(14, 'e14', {
    type: 'TOOL_CALL_START',
    toolCallId: 'tc-write',
    toolCallName: 'Write',
    parentMessageId: 'a1',
    timestamp: 13,
  }),
  env(15, 'e15', {
    type: 'TOOL_CALL_ARGS',
    toolCallId: 'tc-write',
    delta: '{"file_path":"NOTES.md"}',
    timestamp: 14,
  }),
  env(16, 'e16', { type: 'TOOL_CALL_END', toolCallId: 'tc-write', timestamp: 15 }),
  env(17, 'e17', {
    type: 'TOOL_CALL_RESULT',
    toolCallId: 'tc-write',
    content: 'File written successfully',
    messageId: 'a1',
    role: 'tool',
    timestamp: 16,
  }),
  // Closing text
  env(18, 'e18', {
    type: 'TEXT_MESSAGE_START',
    messageId: 'a1',
    role: 'assistant',
    timestamp: 17,
  }),
  env(19, 'e19', {
    type: 'TEXT_MESSAGE_CONTENT',
    messageId: 'a1',
    delta: 'Investigation complete — see notes above.',
    timestamp: 18,
  }),
  env(20, 'e20', { type: 'TEXT_MESSAGE_END', messageId: 'a1', timestamp: 19 }),
  env(21, 'e21', { type: 'RUN_FINISHED', threadId: SESSION_ID, runId: 'r1', timestamp: 20 }),
]

export const streamingInterrupted: SessionEventEnvelope[] = [
  env(1, 'e1', { type: 'RUN_STARTED', threadId: SESSION_ID, runId: 'r1', timestamp: 0 }),
  env(2, 'e2', {
    type: 'TEXT_MESSAGE_START',
    messageId: 'a1',
    role: 'assistant',
    timestamp: 1,
  }),
  env(3, 'e3', {
    type: 'TEXT_MESSAGE_CONTENT',
    messageId: 'a1',
    delta: 'This response was cut short…',
    timestamp: 2,
  }),
  // No TEXT_MESSAGE_END on purpose — stream is interrupted
  env(4, 'e4', {
    type: 'RUN_ERROR',
    message: 'Stream interrupted by client cancel',
    code: 'USER_CANCELED',
    timestamp: 3,
  }),
]

export const envelopeFixtures = {
  simpleTextTurn,
  toolCallLifecycle,
  thinkingBlock,
  multiBlockTurn,
  reasoningStreaming,
  multiToolGroup,
  askUserQuestionPending,
  askUserQuestionAnswered,
  proposePlanPending,
  proposePlanApproved,
  proposePlanRejected,
  unknownToolCall,
  runError,
  streamingInterrupted,
} as const

export type EnvelopeFixtureName = keyof typeof envelopeFixtures
