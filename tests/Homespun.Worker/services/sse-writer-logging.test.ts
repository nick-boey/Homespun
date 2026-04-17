import { vi, beforeEach, describe, it, expect } from 'vitest';
import { streamSessionEvents } from '#src/services/sse-writer.js';
import type { SessionManager } from '#src/services/session-manager.js';
import { collectAsyncGenerator } from '../helpers/async-helpers.js';
import { createAssistantMessage, createResultMessage } from '../helpers/test-fixtures.js';

// Mock the logger module
vi.mock('#src/utils/logger.js', () => ({
  info: vi.fn(),
  error: vi.fn(),
  warn: vi.fn(),
  debug: vi.fn(),
  sessionEventLog: vi.fn(),
  SessionEventHop: { WorkerA2AEmit: 'worker.a2a.emit' },
  extractA2ACorrelation: vi.fn((kind: string) => ({ SessionId: '', A2AKind: kind })),
  extractMessagePreview: vi.fn(() => undefined),
  getContentPreviewChars: vi.fn(() => 0),
  truncatePreview: vi.fn((t: string | undefined) => t),
}));

import { info, debug } from '#src/utils/logger.js';

describe('SSE Writer Logging', () => {
  let mockSessionManager: any;

  beforeEach(() => {
    vi.clearAllMocks();

    // Create a mock session manager
    mockSessionManager = {
      get: vi.fn(),
      stream: vi.fn(),
    };
  });

  describe('A2A state transition logging', () => {
    it('logs initial state transition from submitted to working', async () => {
      // Arrange
      const mockSession = {
        id: 'test-session-123',
        conversationId: 'conv-123',
        outputChannel: {
          [Symbol.asyncIterator]: async function* () {
            yield createAssistantMessage('Hello');
            yield createResultMessage();
          },
        },
      };

      mockSessionManager.get.mockReturnValue(mockSession);
      mockSessionManager.stream.mockImplementation(async function* () {
        yield createAssistantMessage('Hello');
        yield createResultMessage();
      });

      // Act
      const events = await collectAsyncGenerator(streamSessionEvents(mockSessionManager, 'test-session-123'));

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('A2A state transition: submitted → working (sessionId: test-session-123)')
      );
    });

    it('logs state transition from working to input-required for questions', async () => {
      // Arrange
      const mockSession = {
        id: 'test-session-123',
        conversationId: 'conv-123',
        outputChannel: {
          [Symbol.asyncIterator]: async function* () {
            yield { type: 'question_pending', data: { questions: [] } };
            yield createResultMessage();
          },
        },
      };

      mockSessionManager.get.mockReturnValue(mockSession);
      mockSessionManager.stream.mockImplementation(async function* () {
        yield { type: 'question_pending', data: { questions: [] } };
        yield createResultMessage();
      });

      // Act
      const events = await collectAsyncGenerator(streamSessionEvents(mockSessionManager, 'test-session-123'));

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('A2A state transition: working → input-required (sessionId: test-session-123)')
      );
    });

    it('logs state transition from working to input-required for plan approval', async () => {
      // Arrange
      const mockSession = {
        id: 'test-session-123',
        conversationId: 'conv-123',
        outputChannel: {
          [Symbol.asyncIterator]: async function* () {
            yield { type: 'plan_pending', data: { plan: 'My plan' } };
            yield createResultMessage();
          },
        },
      };

      mockSessionManager.get.mockReturnValue(mockSession);
      mockSessionManager.stream.mockImplementation(async function* () {
        yield { type: 'plan_pending', data: { plan: 'My plan' } };
        yield createResultMessage();
      });

      // Act
      const events = await collectAsyncGenerator(streamSessionEvents(mockSessionManager, 'test-session-123'));

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('A2A state transition: working → input-required (sessionId: test-session-123)')
      );
    });

    it('logs state transition from working to completed', async () => {
      // Arrange
      const mockSession = {
        id: 'test-session-123',
        conversationId: 'conv-123',
        outputChannel: {
          [Symbol.asyncIterator]: async function* () {
            yield createAssistantMessage('Done');
            yield createResultMessage();
          },
        },
      };

      mockSessionManager.get.mockReturnValue(mockSession);
      mockSessionManager.stream.mockImplementation(async function* () {
        yield createAssistantMessage('Done');
        yield createResultMessage();
      });

      // Act
      const events = await collectAsyncGenerator(streamSessionEvents(mockSessionManager, 'test-session-123'));

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('A2A state transition: working → completed (sessionId: test-session-123)')
      );
    });

    it('logs state transition from working to failed on error', async () => {
      // Arrange
      const errorResult = createResultMessage();
      (errorResult as any).is_error = true;
      (errorResult as any).errors = ['Test error'];

      const mockSession = {
        id: 'test-session-123',
        conversationId: 'conv-123',
        outputChannel: {
          [Symbol.asyncIterator]: async function* () {
            yield createAssistantMessage('Error occurred');
            yield errorResult;
          },
        },
      };

      mockSessionManager.get.mockReturnValue(mockSession);
      mockSessionManager.stream.mockImplementation(async function* () {
        yield createAssistantMessage('Error occurred');
        yield errorResult;
      });

      // Act
      const events = await collectAsyncGenerator(streamSessionEvents(mockSessionManager, 'test-session-123'));

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('A2A state transition: working → failed (sessionId: test-session-123)')
      );
    });
  });

  describe('Tool execution logging', () => {
    it('logs tool name with parameter details at debug level', async () => {
      // Arrange
      const toolStreamEvent = {
        type: 'stream_event',
        session_id: 'test-session-123',
        event: {
          type: 'tool_invocation',
          name: 'Read',
          input: { file_path: '/test.txt', limit: 100 },
        },
      };

      const mockSession = {
        id: 'test-session-123',
        conversationId: 'conv-123',
        outputChannel: {
          [Symbol.asyncIterator]: async function* () {
            yield toolStreamEvent;
            yield createResultMessage();
          },
        },
      };

      mockSessionManager.get.mockReturnValue(mockSession);
      mockSessionManager.stream.mockImplementation(async function* () {
        yield toolStreamEvent;
        yield createResultMessage();
      });

      // Act
      const events = await collectAsyncGenerator(streamSessionEvents(mockSessionManager, 'test-session-123'));

      // Assert
      expect(debug).toHaveBeenCalledWith(
        expect.stringContaining('Tool: Read file_path="/test.txt" limit=100 (sessionId: test-session-123)')
      );
    });

    it('logs unknown tool name when not provided', async () => {
      // Arrange
      const toolStreamEvent = {
        type: 'stream_event',
        session_id: 'test-session-123',
        event: {
          type: 'tool_invocation',
          // name is missing
          input: {},
        },
      };

      const mockSession = {
        id: 'test-session-123',
        conversationId: 'conv-123',
        outputChannel: {
          [Symbol.asyncIterator]: async function* () {
            yield toolStreamEvent;
            yield createResultMessage();
          },
        },
      };

      mockSessionManager.get.mockReturnValue(mockSession);
      mockSessionManager.stream.mockImplementation(async function* () {
        yield toolStreamEvent;
        yield createResultMessage();
      });

      // Act
      const events = await collectAsyncGenerator(streamSessionEvents(mockSessionManager, 'test-session-123'));

      // Assert
      expect(debug).toHaveBeenCalledWith(
        expect.stringContaining('Tool: unknown')
      );
    });

    it('truncates long string parameters to 50 chars', async () => {
      // Arrange
      const longPath = '/a/very/long/path/that/exceeds/fifty/characters/in/length/file.txt';
      const toolStreamEvent = {
        type: 'stream_event',
        session_id: 'test-session-123',
        event: {
          type: 'tool_invocation',
          name: 'Read',
          input: { file_path: longPath },
        },
      };

      const mockSession = {
        id: 'test-session-123',
        conversationId: 'conv-123',
        outputChannel: {
          [Symbol.asyncIterator]: async function* () {
            yield toolStreamEvent;
            yield createResultMessage();
          },
        },
      };

      mockSessionManager.get.mockReturnValue(mockSession);
      mockSessionManager.stream.mockImplementation(async function* () {
        yield toolStreamEvent;
        yield createResultMessage();
      });

      // Act
      const events = await collectAsyncGenerator(streamSessionEvents(mockSessionManager, 'test-session-123'));

      // Assert - the long path should be truncated with '...'
      // First 50 chars of longPath = '/a/very/long/path/that/exceeds/fifty/characters/in'
      expect(debug).toHaveBeenCalledWith(
        expect.stringContaining('file_path="/a/very/long/path/that/exceeds/fifty/characters/in...')
      );
    });

    it('shows arrays as [Array(N)]', async () => {
      // Arrange
      const toolStreamEvent = {
        type: 'stream_event',
        session_id: 'test-session-123',
        event: {
          type: 'tool_invocation',
          name: 'Edit',
          input: { items: ['a', 'b', 'c'] },
        },
      };

      const mockSession = {
        id: 'test-session-123',
        conversationId: 'conv-123',
        outputChannel: {
          [Symbol.asyncIterator]: async function* () {
            yield toolStreamEvent;
            yield createResultMessage();
          },
        },
      };

      mockSessionManager.get.mockReturnValue(mockSession);
      mockSessionManager.stream.mockImplementation(async function* () {
        yield toolStreamEvent;
        yield createResultMessage();
      });

      // Act
      const events = await collectAsyncGenerator(streamSessionEvents(mockSessionManager, 'test-session-123'));

      // Assert
      expect(debug).toHaveBeenCalledWith(
        expect.stringContaining('items=[Array(3)]')
      );
    });

    it('shows objects as [Object]', async () => {
      // Arrange
      const toolStreamEvent = {
        type: 'stream_event',
        session_id: 'test-session-123',
        event: {
          type: 'tool_invocation',
          name: 'Edit',
          input: { config: { nested: true } },
        },
      };

      const mockSession = {
        id: 'test-session-123',
        conversationId: 'conv-123',
        outputChannel: {
          [Symbol.asyncIterator]: async function* () {
            yield toolStreamEvent;
            yield createResultMessage();
          },
        },
      };

      mockSessionManager.get.mockReturnValue(mockSession);
      mockSessionManager.stream.mockImplementation(async function* () {
        yield toolStreamEvent;
        yield createResultMessage();
      });

      // Act
      const events = await collectAsyncGenerator(streamSessionEvents(mockSessionManager, 'test-session-123'));

      // Assert
      expect(debug).toHaveBeenCalledWith(
        expect.stringContaining('config=[Object]')
      );
    });

    it('includes boolean parameters', async () => {
      // Arrange
      const toolStreamEvent = {
        type: 'stream_event',
        session_id: 'test-session-123',
        event: {
          type: 'tool_invocation',
          name: 'Edit',
          input: { replace_all: true },
        },
      };

      const mockSession = {
        id: 'test-session-123',
        conversationId: 'conv-123',
        outputChannel: {
          [Symbol.asyncIterator]: async function* () {
            yield toolStreamEvent;
            yield createResultMessage();
          },
        },
      };

      mockSessionManager.get.mockReturnValue(mockSession);
      mockSessionManager.stream.mockImplementation(async function* () {
        yield toolStreamEvent;
        yield createResultMessage();
      });

      // Act
      const events = await collectAsyncGenerator(streamSessionEvents(mockSessionManager, 'test-session-123'));

      // Assert
      expect(debug).toHaveBeenCalledWith(
        expect.stringContaining('replace_all=true')
      );
    });
  });

  describe('Assistant message content logging', () => {
    it('logs assistant message content summary at debug level', async () => {
      // Arrange
      const assistantMessage = createAssistantMessage({
        message: {
          role: 'assistant',
          content: [{ type: 'text', text: 'Hello, I will help you with this task.' }],
        },
      });

      const mockSession = {
        id: 'test-session-123',
        conversationId: 'conv-123',
        outputChannel: {
          [Symbol.asyncIterator]: async function* () {
            yield assistantMessage;
            yield createResultMessage();
          },
        },
      };

      mockSessionManager.get.mockReturnValue(mockSession);
      mockSessionManager.stream.mockImplementation(async function* () {
        yield assistantMessage;
        yield createResultMessage();
      });

      // Act
      const events = await collectAsyncGenerator(streamSessionEvents(mockSessionManager, 'test-session-123'));

      // Assert
      expect(debug).toHaveBeenCalledWith(
        expect.stringContaining('Assistant: Hello, I will help you with this task.')
      );
    });

    it('truncates long assistant messages to 200 chars', async () => {
      // Arrange
      const longText = 'A'.repeat(250);
      const assistantMessage = createAssistantMessage({
        message: {
          role: 'assistant',
          content: [{ type: 'text', text: longText }],
        },
      });

      const mockSession = {
        id: 'test-session-123',
        conversationId: 'conv-123',
        outputChannel: {
          [Symbol.asyncIterator]: async function* () {
            yield assistantMessage;
            yield createResultMessage();
          },
        },
      };

      mockSessionManager.get.mockReturnValue(mockSession);
      mockSessionManager.stream.mockImplementation(async function* () {
        yield assistantMessage;
        yield createResultMessage();
      });

      // Act
      const events = await collectAsyncGenerator(streamSessionEvents(mockSessionManager, 'test-session-123'));

      // Assert - should be truncated at 200 chars with '...'
      expect(debug).toHaveBeenCalledWith(
        expect.stringContaining('Assistant: ' + 'A'.repeat(200) + '...')
      );
    });
  });
});