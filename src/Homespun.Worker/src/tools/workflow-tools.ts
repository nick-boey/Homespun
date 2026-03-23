/**
 * Workflow tool definitions for signaling workflow step completion/failure.
 *
 * This module provides two tools:
 * - workflow_signal: The primary tool for agents to report step outcomes back to the workflow engine
 * - WorkflowComplete: Legacy tool kept for backwards compatibility
 */

/**
 * Status of a workflow signal.
 */
export type WorkflowSignalStatus = "success" | "fail";

/**
 * Input schema for the workflow_signal tool.
 */
export interface WorkflowSignalInput {
  /** Step outcome */
  status: WorkflowSignalStatus;
  /** Output data for workflow context */
  data?: Record<string, unknown>;
  /** Summary of what was done */
  message?: string;
}

/**
 * Context passed to a session when running as part of a workflow.
 * This enables the workflow_signal tool and provides workflow metadata.
 */
export interface WorkflowSessionContext {
  /** The workflow execution ID */
  executionId: string;
  /** The step ID being executed */
  stepId: string;
  /** The workflow definition ID */
  workflowId: string;
  /** The project path */
  projectPath: string;
}

/**
 * Tool name constant for the workflow_signal tool.
 */
export const WORKFLOW_SIGNAL_TOOL = "workflow_signal";

/**
 * MCP tool definition for workflow_signal.
 * This follows the MCP tool schema format.
 */
export const workflowSignalToolDefinition = {
  name: WORKFLOW_SIGNAL_TOOL,
  description: "Signal workflow step completion with result data",
  input_schema: {
    type: "object" as const,
    properties: {
      status: {
        type: "string" as const,
        enum: ["success", "fail"],
        description: "Step outcome",
      },
      data: {
        type: "object" as const,
        description: "Output data for workflow context",
      },
      message: {
        type: "string" as const,
        description: "Summary of what was done",
      },
    },
    required: ["status"],
  },
};

/**
 * Validates that the input matches the workflow_signal schema.
 */
export function isValidWorkflowSignalInput(
  input: unknown,
): input is WorkflowSignalInput {
  if (typeof input !== "object" || input === null) {
    return false;
  }

  const obj = input as Record<string, unknown>;

  // Check required field
  if (typeof obj.status !== "string") {
    return false;
  }

  if (!["success", "fail"].includes(obj.status)) {
    return false;
  }

  // Check optional data field
  if (obj.data !== undefined && (typeof obj.data !== "object" || obj.data === null)) {
    return false;
  }

  // Check optional message field
  if (obj.message !== undefined && typeof obj.message !== "string") {
    return false;
  }

  return true;
}

// --- Legacy WorkflowComplete tool (kept for backwards compatibility) ---

/**
 * Status of the workflow node completion.
 */
export type WorkflowCompleteStatus = "success" | "partial" | "needs_review";

/**
 * An artifact produced by the workflow node.
 */
export interface WorkflowArtifact {
  /** Name of the artifact */
  name: string;
  /** Path to the artifact (file path, URL, etc.) */
  path: string;
  /** Type of artifact (e.g., "file", "url", "commit", "pr") */
  type: string;
}

/**
 * Input schema for the WorkflowComplete tool.
 */
export interface WorkflowCompleteInput {
  /** Completion status of the workflow node */
  status: WorkflowCompleteStatus;
  /** Typed output data from the workflow node */
  outputs: Record<string, unknown>;
  /** Human-readable summary of what was accomplished */
  summary: string;
  /** Optional list of artifacts produced */
  artifacts?: WorkflowArtifact[];
}

/**
 * Tool name constant for the WorkflowComplete tool.
 */
export const WORKFLOW_COMPLETE_TOOL = "WorkflowComplete";

/**
 * MCP tool definition for WorkflowComplete.
 * This follows the MCP tool schema format.
 */
export const workflowCompleteToolDefinition = {
  name: WORKFLOW_COMPLETE_TOOL,
  description: "Signal workflow node completion with output data",
  input_schema: {
    type: "object" as const,
    properties: {
      status: {
        type: "string" as const,
        enum: ["success", "partial", "needs_review"],
        description:
          "Completion status: success (fully done), partial (some work done), needs_review (requires human review)",
      },
      outputs: {
        type: "object" as const,
        description: "Typed output data from this workflow node",
      },
      summary: {
        type: "string" as const,
        description: "Human-readable summary of what was accomplished",
      },
      artifacts: {
        type: "array" as const,
        description: "Optional list of artifacts produced (files, commits, PRs, etc.)",
        items: {
          type: "object" as const,
          properties: {
            name: {
              type: "string" as const,
              description: "Name of the artifact",
            },
            path: {
              type: "string" as const,
              description: "Path to the artifact (file path, URL, etc.)",
            },
            type: {
              type: "string" as const,
              description: 'Type of artifact (e.g., "file", "url", "commit", "pr")',
            },
          },
          required: ["name", "path", "type"],
        },
      },
    },
    required: ["status", "outputs", "summary"],
  },
};

/**
 * Validates that the input matches the WorkflowComplete schema.
 */
export function isValidWorkflowCompleteInput(
  input: unknown,
): input is WorkflowCompleteInput {
  if (typeof input !== "object" || input === null) {
    return false;
  }

  const obj = input as Record<string, unknown>;

  // Check required fields
  if (typeof obj.status !== "string") {
    return false;
  }

  if (!["success", "partial", "needs_review"].includes(obj.status)) {
    return false;
  }

  if (typeof obj.outputs !== "object" || obj.outputs === null) {
    return false;
  }

  if (typeof obj.summary !== "string") {
    return false;
  }

  // Check optional artifacts array
  if (obj.artifacts !== undefined) {
    if (!Array.isArray(obj.artifacts)) {
      return false;
    }

    for (const artifact of obj.artifacts) {
      if (typeof artifact !== "object" || artifact === null) {
        return false;
      }

      const art = artifact as Record<string, unknown>;
      if (
        typeof art.name !== "string" ||
        typeof art.path !== "string" ||
        typeof art.type !== "string"
      ) {
        return false;
      }
    }
  }

  return true;
}
