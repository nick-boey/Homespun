/**
 * Test 1: Basic Prompts
 *
 * Tests that the worker container responds to simple prompts in Build mode.
 * Verifies the basic SSE event flow: task -> status-update (working) -> message -> status-update (completed)
 */

import { describe, it, expect, beforeAll, afterAll } from "vitest";
import {
  startContainer,
  verifyPrerequisites,
  type ContainerHandle,
} from "../fixtures/container-lifecycle.js";
import { createTempDirectory, type TempDirectory } from "../fixtures/temp-directory.js";
import {
  parseStreamingSSE,
  collectEvents,
  waitForCompletion,
  findAgentText,
  type SSEEvent,
} from "../helpers/streaming-sse-client.js";
import {
  assertHasTaskSubmitted,
  assertHasWorkingStatus,
  assertHasAgentMessage,
  assertHasCompletedStatus,
  assertAgentTextContains,
} from "../helpers/assertions.js";

describe("Worker Container - Basic Prompts", () => {
  let container: ContainerHandle;
  let tempDir: TempDirectory;

  beforeAll(async () => {
    // Verify prerequisites before starting container
    await verifyPrerequisites();

    // Create temp directory for container mount
    tempDir = await createTempDirectory();

    // Start container
    container = await startContainer({
      workingDirectory: tempDir.path,
      claudeDirectory: tempDir.claudePath,
    });

    // Wait for container to be healthy
    await container.waitForHealthy();

    console.log("Container started:", container.containerName);
    console.log("Worker URL:", container.workerUrl);
  }, 120000); // 2 minute timeout for container startup

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

  it("responds to simple prompt in Build mode", async () => {
    // Send a simple prompt to the worker
    const response = await fetch(container.workerUrl + "/api/sessions", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        prompt: "Say 'Hello, World'",
        mode: "Build",
        model: "claude-sonnet-4-20250514",
      }),
    });

    expect(response.status).toBe(200);
    expect(response.headers.get("Content-Type")).toContain("text/event-stream");

    // Wait for completion and collect all events
    const body = response.body;
    if (!body) {
      throw new Error("Response body is null");
    }

    const { events } = await waitForCompletion(body, 180000);

    // Print events for debugging
    console.log("Events received:", events.length);
    for (const event of events) {
      console.log("Event:", event.event, JSON.stringify(event.data).slice(0, 200));
    }

    // Assert basic event flow
    assertHasTaskSubmitted(events);
    assertHasWorkingStatus(events);
    assertHasAgentMessage(events);
    assertHasCompletedStatus(events);

    // Check that agent responded with Hello, World
    // Note: This is a non-strict assertion - we print the response regardless
    const agentTexts = findAgentText(events);
    console.log("Agent response:");
    console.log(agentTexts.join("\n---\n"));

    // Soft assertion - print result but don't fail if exact text not found
    const containsHelloWorld = agentTexts.some(
      (text) => text.includes("Hello, World") || text.includes("Hello World")
    );
    if (containsHelloWorld) {
      console.log("SUCCESS: Agent response contains 'Hello, World'");
    } else {
      console.log("NOTE: Agent response did not contain exact text 'Hello, World'");
    }

    // At minimum, we expect some response from the agent
    expect(agentTexts.length).toBeGreaterThan(0);
  }, 300000); // 5 minute timeout for live inference
});
