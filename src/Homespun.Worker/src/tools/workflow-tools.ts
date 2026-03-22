/**
 * WorkflowComplete tool definition for signaling workflow node completion.
 *
 * This tool allows agents to signal that they have completed their work
 * as part of a workflow execution, providing output data and artifacts.
 */

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
 * Context passed to a session when running as part of a workflow.
 * This enables the WorkflowComplete tool and provides workflow metadata.
 */
export interface WorkflowSessionContext {
  /** The workflow execution ID */
  executionId: string;
  /** The node ID being executed */
  nodeId: string;
  /** The workflow definition ID */
  workflowId: string;
  /** The project path */
  projectPath: string;
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
