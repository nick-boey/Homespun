/**
 * Temp Directory Fixture
 *
 * Creates and manages temporary directories for live integration tests.
 * Provides automatic cleanup after tests complete.
 */

import { mkdtemp, rm, mkdir } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

export interface TempDirectory {
  /** Path to the temporary working directory */
  path: string;
  /** Path to the temporary .claude directory */
  claudePath: string;
  /** Clean up all temporary directories */
  cleanup(): Promise<void>;
}

/**
 * Create a temporary directory for testing.
 * Creates both a working directory and a .claude directory.
 */
export async function createTempDirectory(
  prefix = "homespun-live-test"
): Promise<TempDirectory> {
  const basePath = await mkdtemp(join(tmpdir(), prefix + "-"));
  const claudePath = join(basePath, ".claude");

  // Create .claude subdirectories that the worker expects
  await mkdir(join(claudePath, "todos"), { recursive: true });
  await mkdir(join(claudePath, "debug"), { recursive: true });
  await mkdir(join(claudePath, "projects"), { recursive: true });
  await mkdir(join(claudePath, "statsig"), { recursive: true });
  await mkdir(join(claudePath, "plans"), { recursive: true });

  return {
    path: basePath,
    claudePath,
    async cleanup(): Promise<void> {
      try {
        await rm(basePath, { recursive: true, force: true });
      } catch {
        // Ignore cleanup errors
      }
    },
  };
}
