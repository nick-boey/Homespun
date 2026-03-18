/**
 * Streaming SSE Client
 *
 * Parses Server-Sent Events from live HTTP streams in real-time.
 * Provides utilities for waiting on specific event types.
 */

export interface SSEEvent {
  event: string;
  data: unknown;
}

export interface TaskStatusUpdate {
  status: {
    state: string;
  };
  final?: boolean;
  metadata?: {
    inputType?: string;
    questions?: Array<{
      question: string;
      header: string;
      options: Array<{ label: string; description: string }>;
      multiSelect: boolean;
    }>;
    plan?: string;
  };
}

/**
 * Parse SSE events from a readable stream.
 * Yields events as they arrive.
 */
export async function* parseStreamingSSE(
  readable: ReadableStream<Uint8Array>
): AsyncGenerator<SSEEvent> {
  const reader = readable.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      // Process complete SSE blocks (separated by double newlines)
      const blocks = buffer.split("\n\n");
      // Keep the last incomplete block in the buffer
      buffer = blocks.pop() || "";

      for (const block of blocks) {
        if (!block.trim()) continue;

        const lines = block.split("\n");
        let eventType = "";
        let data = "";

        for (const line of lines) {
          if (line.startsWith("event: ")) {
            eventType = line.slice("event: ".length);
          } else if (line.startsWith("data: ")) {
            data = line.slice("data: ".length);
          }
        }

        if (eventType && data) {
          try {
            yield { event: eventType, data: JSON.parse(data) };
          } catch {
            // Skip malformed JSON
          }
        }
      }
    }

    // Process any remaining data in the buffer
    if (buffer.trim()) {
      const lines = buffer.split("\n");
      let eventType = "";
      let data = "";

      for (const line of lines) {
        if (line.startsWith("event: ")) {
          eventType = line.slice("event: ".length);
        } else if (line.startsWith("data: ")) {
          data = line.slice("data: ".length);
        }
      }

      if (eventType && data) {
        try {
          yield { event: eventType, data: JSON.parse(data) };
        } catch {
          // Skip malformed JSON
        }
      }
    }
  } finally {
    reader.releaseLock();
  }
}

/**
 * Collect all events from a stream until it completes.
 */
export async function collectEvents(
  readable: ReadableStream<Uint8Array>
): Promise<SSEEvent[]> {
  const events: SSEEvent[] = [];
  for await (const event of parseStreamingSSE(readable)) {
    events.push(event);
  }
  return events;
}

/**
 * Wait for an event matching a predicate.
 * Returns the matching event or throws on timeout.
 */
export async function waitForEvent(
  readable: ReadableStream<Uint8Array>,
  predicate: (event: SSEEvent) => boolean,
  timeoutMs = 120000
): Promise<{ event: SSEEvent; events: SSEEvent[] }> {
  const events: SSEEvent[] = [];

  const iterateStream = async (): Promise<{ event: SSEEvent; events: SSEEvent[] }> => {
    for await (const event of parseStreamingSSE(readable)) {
      events.push(event);
      if (predicate(event)) {
        return { event, events };
      }
    }
    throw new Error(
      "Stream ended without matching event. Events received: " +
        JSON.stringify(events.slice(-5))
    );
  };

  const timeout = new Promise<never>((_, reject) =>
    setTimeout(
      () =>
        reject(
          new Error(
            `Timeout after ${timeoutMs}ms waiting for event. Events received: ` +
              JSON.stringify(events.slice(-5))
          )
        ),
      timeoutMs
    )
  );

  return Promise.race([iterateStream(), timeout]);
}

/**
 * Wait for a status-update event with final=true.
 */
export async function waitForCompletion(
  readable: ReadableStream<Uint8Array>,
  timeoutMs = 120000
): Promise<{ event: SSEEvent; events: SSEEvent[] }> {
  return waitForEvent(
    readable,
    (event) => {
      if (event.event !== "status-update") return false;
      const data = event.data as TaskStatusUpdate;
      return data.final === true;
    },
    timeoutMs
  );
}

/**
 * Wait for an input-required status (question or plan pending).
 */
export async function waitForInputRequired(
  readable: ReadableStream<Uint8Array>,
  timeoutMs = 120000
): Promise<{ event: SSEEvent; events: SSEEvent[] }> {
  return waitForEvent(
    readable,
    (event) => {
      if (event.event !== "status-update") return false;
      const data = event.data as TaskStatusUpdate;
      return data.status.state === "input-required";
    },
    timeoutMs
  );
}

/**
 * Extract session ID from events.
 * The session ID is typically in the task event.
 */
export function extractSessionId(events: SSEEvent[]): string | undefined {
  const taskEvent = events.find((e) => e.event === "task");
  if (taskEvent) {
    return (taskEvent.data as { id?: string }).id;
  }
  return undefined;
}

/**
 * Find text content in agent messages.
 */
export function findAgentText(events: SSEEvent[]): string[] {
  const texts: string[] = [];
  for (const event of events) {
    if (event.event !== "message") continue;
    const data = event.data as {
      role?: string;
      parts?: Array<{ kind?: string; text?: string }>;
    };
    if (data.role !== "agent") continue;
    for (const part of data.parts || []) {
      if (part.kind === "text" && part.text) {
        texts.push(part.text);
      }
    }
  }
  return texts;
}
