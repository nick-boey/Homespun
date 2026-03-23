import { describe, it, expect } from "vitest";
import {
  WORKFLOW_COMPLETE_TOOL,
  workflowCompleteToolDefinition,
  isValidWorkflowCompleteInput,
  type WorkflowCompleteInput,
  WORKFLOW_SIGNAL_TOOL,
  workflowSignalToolDefinition,
  isValidWorkflowSignalInput,
  type WorkflowSignalInput,
  type WorkflowSessionContext,
} from "./workflow-tools.js";

describe("Workflow Tools", () => {
  describe("workflow_signal tool", () => {
    describe("WORKFLOW_SIGNAL_TOOL constant", () => {
      it("has the correct tool name", () => {
        expect(WORKFLOW_SIGNAL_TOOL).toBe("workflow_signal");
      });
    });

    describe("workflowSignalToolDefinition", () => {
      it("has the correct name", () => {
        expect(workflowSignalToolDefinition.name).toBe("workflow_signal");
      });

      it("has a description", () => {
        expect(workflowSignalToolDefinition.description).toBe(
          "Signal workflow step completion with result data",
        );
      });

      it("has only status as required", () => {
        const required = workflowSignalToolDefinition.input_schema.required;
        expect(required).toEqual(["status"]);
      });

      it("has status enum with success and fail", () => {
        const statusProp =
          workflowSignalToolDefinition.input_schema.properties.status;
        expect(statusProp.enum).toEqual(["success", "fail"]);
      });

      it("has data and message as optional properties", () => {
        const props = workflowSignalToolDefinition.input_schema.properties;
        expect(props.data).toBeDefined();
        expect(props.message).toBeDefined();
      });
    });

    describe("isValidWorkflowSignalInput", () => {
      it("returns true for success with all fields", () => {
        const input: WorkflowSignalInput = {
          status: "success",
          data: { result: "done" },
          message: "Step completed",
        };
        expect(isValidWorkflowSignalInput(input)).toBe(true);
      });

      it("returns true for fail status", () => {
        const input: WorkflowSignalInput = {
          status: "fail",
          message: "Something went wrong",
        };
        expect(isValidWorkflowSignalInput(input)).toBe(true);
      });

      it("returns true for status only (minimal)", () => {
        const input = { status: "success" };
        expect(isValidWorkflowSignalInput(input)).toBe(true);
      });

      it("returns false for null input", () => {
        expect(isValidWorkflowSignalInput(null)).toBe(false);
      });

      it("returns false for non-object input", () => {
        expect(isValidWorkflowSignalInput("string")).toBe(false);
        expect(isValidWorkflowSignalInput(123)).toBe(false);
        expect(isValidWorkflowSignalInput(undefined)).toBe(false);
      });

      it("returns false for missing status", () => {
        const input = { data: {}, message: "test" };
        expect(isValidWorkflowSignalInput(input)).toBe(false);
      });

      it("returns false for invalid status value", () => {
        const input = { status: "partial" };
        expect(isValidWorkflowSignalInput(input)).toBe(false);
      });

      it("returns false for non-object data", () => {
        const input = { status: "success", data: "string" };
        expect(isValidWorkflowSignalInput(input)).toBe(false);
      });

      it("returns false for null data", () => {
        const input = { status: "success", data: null };
        expect(isValidWorkflowSignalInput(input)).toBe(false);
      });

      it("returns false for non-string message", () => {
        const input = { status: "success", message: 123 };
        expect(isValidWorkflowSignalInput(input)).toBe(false);
      });
    });
  });

  describe("WorkflowComplete tool (legacy)", () => {
    describe("WORKFLOW_COMPLETE_TOOL constant", () => {
      it("has the correct tool name", () => {
        expect(WORKFLOW_COMPLETE_TOOL).toBe("WorkflowComplete");
      });
    });

    describe("workflowCompleteToolDefinition", () => {
      it("has the correct name", () => {
        expect(workflowCompleteToolDefinition.name).toBe("WorkflowComplete");
      });

      it("has a description", () => {
        expect(workflowCompleteToolDefinition.description).toBe(
          "Signal workflow node completion with output data",
        );
      });

      it("has required properties in input_schema", () => {
        const required = workflowCompleteToolDefinition.input_schema.required;
        expect(required).toContain("status");
        expect(required).toContain("outputs");
        expect(required).toContain("summary");
      });

      it("has status enum with correct values", () => {
        const statusProp =
          workflowCompleteToolDefinition.input_schema.properties.status;
        expect(statusProp.enum).toEqual(["success", "partial", "needs_review"]);
      });

      it("has artifacts as optional array", () => {
        const artifactsProp =
          workflowCompleteToolDefinition.input_schema.properties.artifacts;
        expect(artifactsProp.type).toBe("array");
        expect(artifactsProp.items.type).toBe("object");
      });
    });

    describe("isValidWorkflowCompleteInput", () => {
      it("returns true for valid input with all required fields", () => {
        const input: WorkflowCompleteInput = {
          status: "success",
          outputs: { prNumber: 123 },
          summary: "Created PR #123",
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(true);
      });

      it("returns true for valid input with artifacts", () => {
        const input: WorkflowCompleteInput = {
          status: "success",
          outputs: { prNumber: 123 },
          summary: "Created PR #123",
          artifacts: [
            { name: "Pull Request", path: "https://github.com/org/repo/pull/123", type: "pr" },
          ],
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(true);
      });

      it("returns true for partial status", () => {
        const input: WorkflowCompleteInput = {
          status: "partial",
          outputs: { filesModified: 3 },
          summary: "Modified 3 files but tests are failing",
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(true);
      });

      it("returns true for needs_review status", () => {
        const input: WorkflowCompleteInput = {
          status: "needs_review",
          outputs: { changes: ["major refactor"] },
          summary: "Large refactor requires human review",
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(true);
      });

      it("returns false for null input", () => {
        expect(isValidWorkflowCompleteInput(null)).toBe(false);
      });

      it("returns false for non-object input", () => {
        expect(isValidWorkflowCompleteInput("string")).toBe(false);
        expect(isValidWorkflowCompleteInput(123)).toBe(false);
        expect(isValidWorkflowCompleteInput(undefined)).toBe(false);
      });

      it("returns false for missing status", () => {
        const input = {
          outputs: {},
          summary: "test",
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(false);
      });

      it("returns false for invalid status value", () => {
        const input = {
          status: "invalid",
          outputs: {},
          summary: "test",
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(false);
      });

      it("returns false for missing outputs", () => {
        const input = {
          status: "success",
          summary: "test",
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(false);
      });

      it("returns false for null outputs", () => {
        const input = {
          status: "success",
          outputs: null,
          summary: "test",
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(false);
      });

      it("returns false for missing summary", () => {
        const input = {
          status: "success",
          outputs: {},
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(false);
      });

      it("returns false for non-string summary", () => {
        const input = {
          status: "success",
          outputs: {},
          summary: 123,
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(false);
      });

      it("returns false for non-array artifacts", () => {
        const input = {
          status: "success",
          outputs: {},
          summary: "test",
          artifacts: "not-array",
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(false);
      });

      it("returns false for artifact missing name", () => {
        const input = {
          status: "success",
          outputs: {},
          summary: "test",
          artifacts: [{ path: "/test", type: "file" }],
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(false);
      });

      it("returns false for artifact missing path", () => {
        const input = {
          status: "success",
          outputs: {},
          summary: "test",
          artifacts: [{ name: "test", type: "file" }],
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(false);
      });

      it("returns false for artifact missing type", () => {
        const input = {
          status: "success",
          outputs: {},
          summary: "test",
          artifacts: [{ name: "test", path: "/test" }],
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(false);
      });

      it("returns true for empty artifacts array", () => {
        const input: WorkflowCompleteInput = {
          status: "success",
          outputs: {},
          summary: "test",
          artifacts: [],
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(true);
      });

      it("returns true for multiple valid artifacts", () => {
        const input: WorkflowCompleteInput = {
          status: "success",
          outputs: {},
          summary: "test",
          artifacts: [
            { name: "PR", path: "https://github.com/pr/1", type: "pr" },
            { name: "Commit", path: "abc123", type: "commit" },
            { name: "File", path: "/src/test.ts", type: "file" },
          ],
        };
        expect(isValidWorkflowCompleteInput(input)).toBe(true);
      });
    });
  });

  describe("WorkflowSessionContext type", () => {
    it("has correct structure with stepId", () => {
      const context: WorkflowSessionContext = {
        executionId: "exec-123",
        stepId: "step-456",
        workflowId: "workflow-789",
        projectPath: "/path/to/project",
      };

      expect(context.executionId).toBe("exec-123");
      expect(context.stepId).toBe("step-456");
      expect(context.workflowId).toBe("workflow-789");
      expect(context.projectPath).toBe("/path/to/project");
    });
  });
});
