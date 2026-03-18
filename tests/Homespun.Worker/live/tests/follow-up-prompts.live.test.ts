/**
 * Test: Follow-Up Prompts
 *
 * Tests that the worker handles follow-up prompts in the same session.
 * Sends an initial prompt, awaits its response, then sends a follow-up
 * prompt and verifies the response. Simpler than the "continues conversation"
 * test which tests memory recall.
 */

import { describe, it, expect, beforeAll, afterAll } from "vitest";
import {
  startContainer,
  verifyPrerequisites,
  type ContainerHandle,
} from "../fixtures/container-lifecycle.js";
import {
  createTempDirectory,
  type TempDirectory,
} from "../fixtures/temp-directory.js";
import {
  waitForCompletion,
  extractSessionId,
  findAgentText,
} from "../helpers/streaming-sse-client.js";
import {
  assertHasTaskSubmitted,
  assertHasWorkingStatus,
  assertHasAgentMessage,
  assertHasCompletedStatus,
} from "../helpers/assertions.js";

describe("Worker Container - Follow-Up Prompts", () => {
  let container: ContainerHandle;
  let tempDir: TempDirectory;

  beforeAll(async () => {
    await verifyPrerequisites();

    tempDir = await createTempDirectory();

    container = await startContainer({
      workingDirectory: tempDir.path,
      claudeDirectory: tempDir.claudePath,
    });

    await container.waitForHealthy();

    console.log("Container started:", container.containerName);
    console.log("Worker URL:", container.workerUrl);
  }, 120000);

  afterAll(async () => {
    if (container) {
      console.log("Container logs:");
      try {
        const logs = await container.logs();
        console.log(logs);
      } catch {
        // Ignore log errors
      }
      await container.stop();
    }
    if (tempDir) {
      await tempDir.cleanup();
    }
  });

  it("handles follow-up prompts in the same session", async () => {
    // First prompt: ask the agent to say "hello, world"
    const response1 = await fetch(container.workerUrl + "/api/sessions", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        prompt: "Say 'hello, world'",
        mode: "Build",
        model: "claude-sonnet-4-20250514",
      }),
    });

    expect(response1.status).toBe(200);
    const body1 = response1.body;
    if (!body1) {
      throw new Error("Response body is null");
    }

    const { events: events1 } = await waitForCompletion(body1, 180000);

    // Print events for debugging
    console.log("First prompt events received:", events1.length);
    for (const event of events1) {
      console.log(
        "Event:",
        event.event,
        JSON.stringify(event.data).slice(0, 200)
      );
    }

    // Assert basic event flow for first prompt
    assertHasTaskSubmitted(events1);
    assertHasWorkingStatus(events1);
    assertHasAgentMessage(events1);
    assertHasCompletedStatus(events1);

    // Log agent response
    const agentTexts1 = findAgentText(events1);
    console.log("First prompt agent response:");
    console.log(agentTexts1.join("\n---\n"));

    // Verify response contains "hello, world" (case-insensitive)
    const containsHelloWorld = agentTexts1.some((text) =>
      text.toLowerCase().includes("hello, world")
    );
    expect(
      containsHelloWorld,
      "Agent response should contain 'hello, world'"
    ).toBe(true);

    // Extract session ID for follow-up
    const sessionId = extractSessionId(events1);
    expect(sessionId, "Should have a session ID").toBeDefined();
    console.log("Session ID:", sessionId);

    // Follow-up prompt: ask the agent to say "goodbye, world"
    // Include mode in the body to match what the server sends — this previously
    // caused a hang because setPermissionMode was called on the completed query.
    const response2 = await fetch(
      container.workerUrl + "/api/sessions/" + sessionId + "/message",
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          message: "Say 'goodbye, world'",
          mode: "Build",
        }),
      }
    );

    expect(response2.status).toBe(200);
    const body2 = response2.body;
    if (!body2) {
      throw new Error("Response body is null");
    }

    const { events: events2 } = await waitForCompletion(body2, 180000);

    // Print events for debugging
    console.log("Follow-up prompt events received:", events2.length);
    for (const event of events2) {
      console.log(
        "Event:",
        event.event,
        JSON.stringify(event.data).slice(0, 200)
      );
    }

    assertHasCompletedStatus(events2);

    // Log agent response
    const agentTexts2 = findAgentText(events2);
    console.log("Follow-up prompt agent response:");
    console.log(agentTexts2.join("\n---\n"));

    // Verify response contains "goodbye, world" (case-insensitive)
    const containsGoodbyeWorld = agentTexts2.some((text) =>
      text.toLowerCase().includes("goodbye, world")
    );
    expect(
      containsGoodbyeWorld,
      "Agent response should contain 'goodbye, world'"
    ).toBe(true);
  }, 600000); // 10 minutes for two-turn conversation
});
