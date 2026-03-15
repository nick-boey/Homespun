/**
 * Test Assertions for A2A Protocol Events
 *
 * Provides assertion helpers for validating SSE events in live tests.
 */

import type { SSEEvent, TaskStatusUpdate } from "./streaming-sse-client.js";

/**
 * Assert that the events contain a task event with submitted state.
 */
export function assertHasTaskSubmitted(events: SSEEvent[]): void {
  const taskEvent = events.find((e) => e.event === "task");
  if (!taskEvent) {
    throw new Error("No task event found in events");
  }
  const data = taskEvent.data as { status?: { state?: string } };
  if (data.status?.state !== "submitted") {
    throw new Error(
      "Task event state is not 'submitted': " + JSON.stringify(data)
    );
  }
}

/**
 * Assert that the events contain a status-update with working state.
 */
export function assertHasWorkingStatus(events: SSEEvent[]): void {
  const workingEvent = events.find((e) => {
    if (e.event !== "status-update") return false;
    const data = e.data as TaskStatusUpdate;
    return data.status.state === "working";
  });
  if (!workingEvent) {
    throw new Error("No working status-update found in events");
  }
}

/**
 * Assert that the events contain at least one agent message.
 */
export function assertHasAgentMessage(events: SSEEvent[]): void {
  const agentMessage = events.find((e) => {
    if (e.event !== "message") return false;
    const data = e.data as { role?: string };
    return data.role === "agent";
  });
  if (!agentMessage) {
    throw new Error("No agent message found in events");
  }
}

/**
 * Assert that the events contain a completed status-update.
 */
export function assertHasCompletedStatus(events: SSEEvent[]): void {
  const completedEvent = events.find((e) => {
    if (e.event !== "status-update") return false;
    const data = e.data as TaskStatusUpdate;
    return data.status.state === "completed" && data.final === true;
  });
  if (!completedEvent) {
    throw new Error("No completed status-update found in events");
  }
}

/**
 * Assert that agent messages contain the specified text.
 * Returns true if found, prints the actual text for debugging.
 */
export function assertAgentTextContains(
  events: SSEEvent[],
  searchText: string
): boolean {
  const agentTexts: string[] = [];

  for (const event of events) {
    if (event.event !== "message") continue;
    const data = event.data as {
      role?: string;
      parts?: Array<{ kind?: string; text?: string }>;
    };
    if (data.role !== "agent") continue;

    for (const part of data.parts || []) {
      if (part.kind === "text" && part.text) {
        agentTexts.push(part.text);
        if (part.text.includes(searchText)) {
          return true;
        }
      }
    }
  }

  console.log("Agent response text (search failed):");
  console.log(agentTexts.join("\n---\n"));
  return false;
}

/**
 * Get the questions from an input-required status event.
 */
export function getQuestionsFromEvent(
  event: SSEEvent
): Array<{
  question: string;
  header: string;
  options: Array<{ label: string; description: string }>;
  multiSelect: boolean;
}> {
  const data = event.data as TaskStatusUpdate;
  return data.metadata?.questions || [];
}

/**
 * Get the plan from an input-required status event.
 */
export function getPlanFromEvent(event: SSEEvent): string | undefined {
  const data = event.data as TaskStatusUpdate;
  return data.metadata?.plan;
}
