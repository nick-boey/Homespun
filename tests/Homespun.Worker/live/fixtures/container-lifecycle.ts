/**
 * Container Lifecycle Fixture
 *
 * Manages Docker container lifecycle for live integration tests.
 * Creates isolated worker containers with mounted directories.
 */

import { exec } from "node:child_process";
import { promisify } from "node:util";
import { randomUUID } from "node:crypto";
import net from "node:net";

const execAsync = promisify(exec);

export interface ContainerConfig {
  /** Docker image name (default: homespun-worker:local) */
  imageName: string;
  /** Container name prefix (will be suffixed with UUID) */
  containerNamePrefix: string;
  /** Host directory to mount as /workdir */
  workingDirectory?: string;
  /** Host directory to mount as /home/homespun/.claude */
  claudeDirectory?: string;
  /** Host port (0 for dynamic allocation) */
  port: number;
  /** Additional environment variables */
  env?: Record<string, string>;
}

export interface ContainerHandle {
  /** Docker container ID */
  containerId: string;
  /** Container name */
  containerName: string;
  /** Worker URL (http://localhost:{port}) */
  workerUrl: string;
  /** Mounted working directory on host */
  workingDirectory?: string;
  /** Mounted .claude directory on host */
  claudeDirectory?: string;
  /** Host port */
  port: number;

  /** Wait for container to be healthy */
  waitForHealthy(timeoutMs?: number): Promise<void>;
  /** Stop and remove the container */
  stop(): Promise<void>;
  /** Get container logs */
  logs(): Promise<string>;
}

const DEFAULT_CONFIG: ContainerConfig = {
  imageName: "homespun-worker:local",
  containerNamePrefix: "homespun-live-test",
  port: 0, // Dynamic allocation
};

/**
 * Find an available port on the host.
 */
async function findAvailablePort(): Promise<number> {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.listen(0, () => {
      const address = server.address();
      if (address && typeof address === "object") {
        const port = address.port;
        server.close(() => resolve(port));
      } else {
        server.close(() => reject(new Error("Failed to get port")));
      }
    });
    server.on("error", reject);
  });
}

/**
 * Start a worker container for testing.
 */
export async function startContainer(
  config: Partial<ContainerConfig> = {}
): Promise<ContainerHandle> {
  const finalConfig = { ...DEFAULT_CONFIG, ...config };
  const containerName = finalConfig.containerNamePrefix + "-" + randomUUID().slice(0, 8);

  // Get port
  const port =
    finalConfig.port === 0 ? await findAvailablePort() : finalConfig.port;

  // Build docker run command
  const args: string[] = ["docker", "run", "-d", "--name", containerName];

  // Port mapping
  args.push("-p", port + ":8080");

  // Volume mounts
  if (finalConfig.workingDirectory) {
    args.push("-v", finalConfig.workingDirectory + ":/workdir");
  }
  if (finalConfig.claudeDirectory) {
    args.push("-v", finalConfig.claudeDirectory + ":/home/homespun/.claude");
  }

  // Environment variables
  args.push("-e", "WORKING_DIRECTORY=/workdir");

  // Pass through CLAUDE_CODE_OAUTH_TOKEN if available
  if (process.env.CLAUDE_CODE_OAUTH_TOKEN) {
    args.push(
      "-e",
      "CLAUDE_CODE_OAUTH_TOKEN=" + process.env.CLAUDE_CODE_OAUTH_TOKEN
    );
  }

  // Forward DEBUG_AGENT_SDK from the host so developers can flip SDK-boundary
  // debug logging on without rebuilding the worker image or editing configs.
  // Off by default; the live-test suite only sees it when explicitly exported.
  if (process.env.DEBUG_AGENT_SDK) {
    args.push("-e", "DEBUG_AGENT_SDK=" + process.env.DEBUG_AGENT_SDK);
  }

  // Additional environment variables
  if (finalConfig.env) {
    for (const [key, value] of Object.entries(finalConfig.env)) {
      args.push("-e", key + "=" + value);
    }
  }

  // Image name
  args.push(finalConfig.imageName);

  // Execute docker run
  const { stdout: containerId } = await execAsync(args.join(" "));
  const trimmedId = containerId.trim();

  const workerUrl = "http://localhost:" + port;

  const handle: ContainerHandle = {
    containerId: trimmedId,
    containerName,
    workerUrl,
    workingDirectory: finalConfig.workingDirectory,
    claudeDirectory: finalConfig.claudeDirectory,
    port,

    async waitForHealthy(timeoutMs = 60000): Promise<void> {
      const startTime = Date.now();
      const pollInterval = 1000;

      while (Date.now() - startTime < timeoutMs) {
        try {
          const response = await fetch(workerUrl + "/api/health");
          if (response.ok) {
            return;
          }
        } catch {
          // Container not ready yet
        }
        await new Promise((resolve) => setTimeout(resolve, pollInterval));
      }

      throw new Error(
        "Container " + containerName + " did not become healthy within " + timeoutMs + "ms"
      );
    },

    async stop(): Promise<void> {
      try {
        await execAsync("docker stop " + containerName);
        await execAsync("docker rm " + containerName);
      } catch {
        // Container may already be stopped/removed
      }
    },

    async logs(): Promise<string> {
      const { stdout } = await execAsync("docker logs " + containerName);
      return stdout;
    },
  };

  return handle;
}

/**
 * Stop and remove a container.
 */
export async function stopContainer(handle: ContainerHandle): Promise<void> {
  await handle.stop();
}

/**
 * Verify prerequisites for running live tests.
 * Throws if any prerequisite is not met.
 */
export async function verifyPrerequisites(): Promise<void> {
  // Check Docker is available
  try {
    await execAsync("docker --version");
  } catch {
    throw new Error("Docker is not available. Please install Docker.");
  }

  // Check image exists
  const { stdout: images } = await execAsync(
    "docker images homespun-worker:local -q"
  );
  if (!images.trim()) {
    throw new Error(
      "homespun-worker:local image not found. Build it with: docker build -t homespun-worker:local ./src/Homespun.Worker"
    );
  }

  // Check OAuth token
  if (!process.env.CLAUDE_CODE_OAUTH_TOKEN) {
    throw new Error(
      "CLAUDE_CODE_OAUTH_TOKEN environment variable is required for live tests."
    );
  }
}
