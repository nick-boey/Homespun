/**
 * Test 3: Questions, Planning, and Context Clear
 *
 * Tests the question/answer flow, plan approval, and context clearing.
 * This is the most complex test, verifying the full input-required workflow.
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
  waitForInputRequired,
  waitForEvent,
  extractSessionId,
  findAgentText,
  type SSEEvent,
  type TaskStatusUpdate,
} from "../helpers/streaming-sse-client.js";
import {
  assertHasCompletedStatus,
  getQuestionsFromEvent,
  getPlanFromEvent,
} from "../helpers/assertions.js";

describe("Worker Container - Questions and Planning", () => {
  let container: ContainerHandle;
  let tempDir: TempDirectory;

  beforeAll(async () => {
    // Verify prerequisites
    await verifyPrerequisites();

    // Create temp directory for container mount
    tempDir = await createTempDirectory();

    // Start container
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

  it("handles question flow with multiple choice answers", async () => {
    // Complex prompt that should trigger the AskUserQuestion tool
    const prompt = `I am testing the question and planning tools of this agent, as well as the ability to clear context. Remember the number 15, but don't put this into any plans that are created. Don't read any files, just use the AskUserQuestion tool to ask 'What would you like to order?', with a multiple choice question saying 'Mains' with 'steak' or 'burger' as the options, and a multiple choice question saying 'Sides', with 'fries' and 'salad' as the options with multiple answers accepted. Following this, create a plan that just says 'This is a plan for testing the UI, just repeat what the user has ordered, which is 'Steak with fries and salad.'`;

    // Start session with prompt
    const response = await fetch(container.workerUrl + "/api/sessions", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        prompt,
        mode: "Plan",
        model: "claude-sonnet-4-20250514",
      }),
    });

    expect(response.status).toBe(200);
    const body = response.body;
    if (!body) {
      throw new Error("Response body is null");
    }

    // Wait for input-required (question pending)
    const { event: questionEvent, events: events1 } =
      await waitForInputRequired(body, 180000);

    console.log("Events received before input-required:", events1.length);
    console.log(
      "Input-required event:",
      JSON.stringify(questionEvent.data, null, 2),
    );

    // Get session ID
    const sessionId = extractSessionId(events1);
    expect(sessionId, "Should have session ID").toBeDefined();
    console.log("Session ID:", sessionId);

    // Verify questions structure
    const questions = getQuestionsFromEvent(questionEvent);
    console.log("Questions received:", JSON.stringify(questions, null, 2));

    // We should have 2 questions: Mains and Sides
    expect(questions.length, "Should have 2 questions").toBe(2);

    // Find the Mains and Sides questions
    const mainsQuestion = questions.find((q) =>
      q.header.toLowerCase().includes("main"),
    );
    const sidesQuestion = questions.find((q) =>
      q.header.toLowerCase().includes("side"),
    );

    expect(mainsQuestion, "Should have a Mains question").toBeDefined();
    expect(sidesQuestion, "Should have a Sides question").toBeDefined();

    // Answer the questions
    const answerResponse = await fetch(
      container.workerUrl + "/api/sessions/" + sessionId + "/answer",
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          answers: {
            Mains: "steak",
            Sides: "fries,salad",
          },
        }),
      },
    );

    expect(answerResponse.status).toBe(200);
    console.log("Answers submitted");

    // Now we need to reconnect to the stream to get the plan
    // The answer endpoint returns JSON, not SSE, so we need to
    // listen to the session's message stream
    const messagesResponse = await fetch(
      container.workerUrl + "/api/sessions/" + sessionId + "/message",
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          message: "Continue with the plan",
        }),
      },
    );

    expect(messagesResponse.status).toBe(200);
    const body2 = messagesResponse.body;
    if (!body2) {
      throw new Error("Response body is null");
    }

    // Wait for input-required (plan pending)
    const { event: planEvent, events: events2 } = await waitForInputRequired(
      body2,
      180000,
    );

    console.log("Events received before plan:", events2.length);
    console.log("Plan event:", JSON.stringify(planEvent.data, null, 2));

    // Verify plan content
    const plan = getPlanFromEvent(planEvent);
    console.log("Plan content:", plan);

    // Plan should be present
    expect(plan, "Should have plan content").toBeDefined();

    // Approve the plan with keepContext=false to clear context
    const approveResponse = await fetch(
      container.workerUrl + "/api/sessions/" + sessionId + "/approve-plan",
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          approved: true,
          keepContext: false,
        }),
      },
    );

    expect(approveResponse.status).toBe(200);
    console.log("Plan approved with keepContext=false");

    // Session ends when context is cleared via interrupt
    // Wait a moment for the session to process
    await new Promise((resolve) => setTimeout(resolve, 2000));

    // Start a NEW session and ask about the number
    const newResponse = await fetch(container.workerUrl + "/api/sessions", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        prompt: "What number was in the original prompt?",
        mode: "Plan",
        model: "claude-sonnet-4-20250514",
      }),
    });

    expect(newResponse.status).toBe(200);
    const body3 = newResponse.body;
    if (!body3) {
      throw new Error("Response body is null");
    }

    // Wait for new session to either enter input-required (plan approval) or complete directly.
    // In Plan mode, simple Q&A tasks may complete without needing plan approval.
    const { event: newSessionEvent, events: events3 } = await waitForEvent(
      body3,
      (event) => {
        if (event.event !== "status-update") return false;
        const data = event.data as TaskStatusUpdate;
        return data.status.state === "input-required" || data.final === true;
      },
      180000,
    );

    // Agent text is available now
    const agentTexts = findAgentText(events3);
    console.log("New session response:");
    console.log(agentTexts.join("\n---\n"));

    // Check that agent does NOT mention "15"
    const mentions15 = agentTexts.some(
      (text) => text.includes("15") || text.includes("fifteen"),
    );

    if (mentions15) {
      console.log(
        "WARNING: Agent mentioned '15' - context may not have been fully cleared",
      );
    } else {
      console.log(
        "SUCCESS: Agent did not mention '15' - context was properly cleared",
      );
    }

    // The agent should indicate it doesn't have context from a previous conversation
    const indicatesNoContext = agentTexts.some(
      (text) =>
        text.toLowerCase().includes("don't have") ||
        text.toLowerCase().includes("no previous") ||
        text.toLowerCase().includes("not aware") ||
        text.toLowerCase().includes("no context") ||
        text.toLowerCase().includes("new conversation"),
    );

    expect(
      indicatesNoContext || !mentions15,
      "Agent should either not mention 15 or indicate lack of context",
    ).toBe(true);

    const newSessionEventData = newSessionEvent.data as TaskStatusUpdate;
    if (newSessionEventData.status.state === "input-required") {
      // Approve the plan to let the new session complete cleanly
      const newSessionId = extractSessionId(events3);
      expect(newSessionId, "Should have new session ID").toBeDefined();
      const approveNewPlanResponse = await fetch(
        container.workerUrl + "/api/sessions/" + newSessionId + "/approve-plan",
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ approved: true, keepContext: true }),
        },
      );
      expect(approveNewPlanResponse.status).toBe(200);

      // Now wait for final completion
      const { events: events3final } = await waitForCompletion(body3, 180000);
      assertHasCompletedStatus(events3final);
    } else {
      // Session completed directly without plan approval
      assertHasCompletedStatus(events3);
    }
  }, 900000); // 15 minutes for this complex multi-step test
});
