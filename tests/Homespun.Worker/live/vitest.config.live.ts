/**
 * Vitest Configuration for Live Worker Container Tests
 *
 * These tests use real inference and should be run manually during development.
 * They are NOT intended to run in CI.
 */

import { defineConfig } from "vitest/config";
import { resolve } from "node:path";

export default defineConfig({
  test: {
    root: resolve(__dirname),
    globals: true,
    environment: "node",
    testTimeout: 300000, // 5 minutes per test for live inference
    hookTimeout: 120000, // 2 minutes for setup/teardown (container startup)
    include: ["tests/**/*.live.test.ts"],
    bail: 1, // Stop on first failure
    reporters: ["verbose"],
    pool: "forks", // Use separate processes for isolation
    poolOptions: {
      forks: {
        singleFork: true, // Run tests sequentially in a single fork
      },
    },
  },
});
