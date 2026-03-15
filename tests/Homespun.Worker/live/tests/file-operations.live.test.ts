/**
 * Test 2: File Operations
 *
 * Tests that the worker can read and write files in Build mode.
 * Uses a mounted temp directory to verify file operations work correctly.
 */

import { describe, it, expect, beforeAll, afterAll } from "vitest";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";
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
  type SSEEvent,
} from "../helpers/streaming-sse-client.js";
import {
  assertHasCompletedStatus,
  assertAgentTextContains,
} from "../helpers/assertions.js";

describe("Worker Container - File Operations", () => {
  let container: ContainerHandle;
  let tempDir: TempDirectory;

  beforeAll(async () => {
    // Verify prerequisites
    await verifyPrerequisites();

    // Create temp directory for container mount
    tempDir = await createTempDirectory();

    // Start container with mounted directory
    container = await startContainer({
      workingDirectory: tempDir.path,
      claudeDirectory: tempDir.claudePath,
    });

    await container.waitForHealthy();

    console.log("Container started:", container.containerName);
    console.log("Worker URL:", container.workerUrl);
    console.log("Temp directory:", tempDir.path);
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

  it("writes a file to the mounted directory", async () => {
    // Ask the agent to write a file
    const response = await fetch(container.workerUrl + "/api/sessions", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        prompt: "Write the text 'test' to the file 'test.txt'",
        mode: "Build",
        model: "claude-sonnet-4-20250514",
      }),
    });

    expect(response.status).toBe(200);

    const body = response.body;
    if (!body) {
      throw new Error("Response body is null");
    }

    const { events } = await waitForCompletion(body, 180000);

    // Print events for debugging
    console.log("Events received:", events.length);
    for (const event of events) {
      console.log(
        "Event:",
        event.event,
        JSON.stringify(event.data).slice(0, 200)
      );
    }

    assertHasCompletedStatus(events);

    // Verify the file was created on the host
    const testFilePath = join(tempDir.path, "test.txt");
    const fileContent = await readFile(testFilePath, "utf-8");
    console.log("File content:", fileContent);

    expect(fileContent.trim()).toBe("test");
  }, 300000);

  it("reads a file that was externally modified", async () => {
    // First write to ensure file exists
    const testFilePath = join(tempDir.path, "test.txt");
    await writeFile(testFilePath, "edited", "utf-8");
    console.log("Externally modified file to contain 'edited'");

    // Ask the agent to read the file
    const response = await fetch(container.workerUrl + "/api/sessions", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        prompt: "Read the file 'test.txt' and print out the contents",
        mode: "Build",
        model: "claude-sonnet-4-20250514",
      }),
    });

    expect(response.status).toBe(200);

    const body = response.body;
    if (!body) {
      throw new Error("Response body is null");
    }

    const { events } = await waitForCompletion(body, 180000);

    // Print events for debugging
    console.log("Events received:", events.length);
    for (const event of events) {
      console.log(
        "Event:",
        event.event,
        JSON.stringify(event.data).slice(0, 200)
      );
    }

    assertHasCompletedStatus(events);

    // Check that agent mentioned "edited" in its response
    const agentTexts = findAgentText(events);
    console.log("Agent response:");
    console.log(agentTexts.join("\n---\n"));

    // The agent should mention the content of the file
    const mentionsEdited = agentTexts.some(
      (text) =>
        text.toLowerCase().includes("edited") ||
        text.toLowerCase().includes('"edited"')
    );
    expect(
      mentionsEdited,
      "Agent response should mention 'edited' file content"
    ).toBe(true);
  }, 300000);

  it("continues conversation in same session", async () => {
    // Start a session
    const response1 = await fetch(container.workerUrl + "/api/sessions", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        prompt: "Remember the secret word 'penguin'. Do not write any files.",
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
    assertHasCompletedStatus(events1);

    const sessionId = extractSessionId(events1);
    expect(sessionId, "Should have a session ID").toBeDefined();
    console.log("Session ID:", sessionId);

    // Send a follow-up message
    const response2 = await fetch(
      container.workerUrl + "/api/sessions/" + sessionId + "/message",
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          message: "What was the secret word I mentioned earlier?",
        }),
      }
    );

    expect(response2.status).toBe(200);
    const body2 = response2.body;
    if (!body2) {
      throw new Error("Response body is null");
    }

    const { events: events2 } = await waitForCompletion(body2, 180000);
    assertHasCompletedStatus(events2);

    // Check that agent remembered "penguin"
    const agentTexts = findAgentText(events2);
    console.log("Agent response to follow-up:");
    console.log(agentTexts.join("\n---\n"));

    const mentionsPenguin = agentTexts.some((text) =>
      text.toLowerCase().includes("penguin")
    );
    expect(
      mentionsPenguin,
      "Agent should remember the secret word 'penguin'"
    ).toBe(true);
  }, 600000); // 10 minutes for two-turn conversation
});
