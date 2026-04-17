import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import {
  sessionEventLog,
  SessionEventHop,
  truncatePreview,
  extractA2ACorrelation,
  extractMessagePreview,
  getContentPreviewChars,
} from "./logger.js";

describe("truncatePreview", () => {
  it("returns undefined when chars is 0", () => {
    expect(truncatePreview("hello", 0)).toBeUndefined();
  });

  it("returns undefined for null or undefined input", () => {
    expect(truncatePreview(null, 10)).toBeUndefined();
    expect(truncatePreview(undefined, 10)).toBeUndefined();
  });

  it("returns original text when shorter than chars", () => {
    expect(truncatePreview("ab", 5)).toBe("ab");
  });

  it("returns original text when equal to chars", () => {
    expect(truncatePreview("abcde", 5)).toBe("abcde");
  });

  it("truncates with ellipsis when longer", () => {
    expect(truncatePreview("abcdefgh", 3)).toBe("abc\u2026");
  });

  it("handles unicode characters", () => {
    expect(truncatePreview("héllo", 5)).toBe("héllo");
    expect(truncatePreview("héllo world", 5)).toBe("héllo\u2026");
  });
});

describe("extractA2ACorrelation", () => {
  it("extracts messageId and taskId from a message event", () => {
    const data = {
      kind: "message",
      messageId: "M1",
      taskId: "T1",
      contextId: "S1",
      role: "agent",
      parts: [{ kind: "text", text: "hi" }],
    };
    const fields = extractA2ACorrelation("message", data);
    expect(fields.SessionId).toBe("S1");
    expect(fields.TaskId).toBe("T1");
    expect(fields.MessageId).toBe("M1");
    expect(fields.A2AKind).toBe("message");
  });

  it("extracts taskId (from id) for a task event", () => {
    const data = {
      kind: "task",
      id: "T1",
      contextId: "S1",
      status: { state: "submitted" },
    };
    const fields = extractA2ACorrelation("task", data);
    expect(fields.TaskId).toBe("T1");
    expect(fields.SessionId).toBe("S1");
    expect(fields.MessageId).toBeUndefined();
  });

  it("extracts statusTimestamp from a status-update event", () => {
    const data = {
      kind: "status-update",
      taskId: "T1",
      contextId: "S1",
      status: { state: "working", timestamp: "2026-04-17T10:00:00.000Z" },
    };
    const fields = extractA2ACorrelation("status-update", data);
    expect(fields.StatusTimestamp).toBe("2026-04-17T10:00:00.000Z");
    expect(fields.MessageId).toBeUndefined();
  });

  it("extracts artifactId from an artifact-update event", () => {
    const data = {
      kind: "artifact-update",
      taskId: "T1",
      contextId: "S1",
      artifact: { artifactId: "A1" },
    };
    const fields = extractA2ACorrelation("artifact-update", data);
    expect(fields.ArtifactId).toBe("A1");
  });
});

describe("extractMessagePreview", () => {
  it("returns the first text part's text", () => {
    const data = {
      parts: [
        { kind: "data", data: {} },
        { kind: "text", text: "hello world" },
      ],
    };
    expect(extractMessagePreview(data)).toBe("hello world");
  });

  it("returns undefined when no text parts exist", () => {
    expect(extractMessagePreview({ parts: [{ kind: "data", data: {} }] })).toBeUndefined();
  });

  it("returns undefined for non-object input", () => {
    expect(extractMessagePreview(null)).toBeUndefined();
    expect(extractMessagePreview(undefined)).toBeUndefined();
  });
});

describe("getContentPreviewChars", () => {
  const originalEnv = process.env.CONTENT_PREVIEW_CHARS;
  const originalNodeEnv = process.env.NODE_ENV;

  afterEach(() => {
    process.env.CONTENT_PREVIEW_CHARS = originalEnv;
    process.env.NODE_ENV = originalNodeEnv;
  });

  it("defaults to 80 in development", () => {
    delete process.env.CONTENT_PREVIEW_CHARS;
    process.env.NODE_ENV = "development";
    expect(getContentPreviewChars()).toBe(80);
  });

  it("defaults to 0 in production", () => {
    delete process.env.CONTENT_PREVIEW_CHARS;
    process.env.NODE_ENV = "production";
    expect(getContentPreviewChars()).toBe(0);
  });

  it("respects explicit 0", () => {
    process.env.CONTENT_PREVIEW_CHARS = "0";
    expect(getContentPreviewChars()).toBe(0);
  });

  it("respects explicit positive value", () => {
    process.env.CONTENT_PREVIEW_CHARS = "120";
    expect(getContentPreviewChars()).toBe(120);
  });
});

describe("sessionEventLog", () => {
  let logSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    logSpy = vi.spyOn(console, "log").mockImplementation(() => {});
  });

  afterEach(() => {
    logSpy.mockRestore();
  });

  it("emits a flat JSON entry with required fields", () => {
    sessionEventLog(SessionEventHop.WorkerA2AEmit, {
      SessionId: "S1",
      TaskId: "T1",
      MessageId: "M1",
      A2AKind: "message",
    });
    expect(logSpy).toHaveBeenCalledOnce();
    const emitted = JSON.parse(logSpy.mock.calls[0][0] as string);
    expect(emitted.Hop).toBe("worker.a2a.emit");
    expect(emitted.SessionId).toBe("S1");
    expect(emitted.MessageId).toBe("M1");
    expect(emitted.A2AKind).toBe("message");
    expect(emitted.SourceContext).toBe("Worker");
    expect(emitted.Component).toBe("Worker");
    expect(emitted.Level).toBe("Information");
  });

  it("omits undefined fields", () => {
    sessionEventLog(SessionEventHop.WorkerA2AEmit, {
      SessionId: "S1",
    });
    const emitted = JSON.parse(logSpy.mock.calls[0][0] as string);
    expect(emitted.MessageId).toBeUndefined();
    expect(emitted.TaskId).toBeUndefined();
    expect(emitted.ContentPreview).toBeUndefined();
  });
});
