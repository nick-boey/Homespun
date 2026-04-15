/**
 * Vitest Configuration for Live Worker Container Tests
 *
 * These tests use real inference against a locally-built `homespun-worker`
 * image and cost money per run. They are invoked manually during development
 * and by the scheduled `worker-live-tests` workflow
 * (`.github/workflows/worker-live-tests.yml`) post-merge / nightly — do NOT
 * include them in the per-PR worker CI job.
 */

import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    root: '../../tests/Homespun.Worker/live',
    globals: true,
    environment: 'node',
    testTimeout: 300000, // 5 minutes per test for live inference
    hookTimeout: 120000, // 2 minutes for setup/teardown (container startup)
    include: ['tests/**/*.live.test.ts'],
    bail: 1, // Stop on first failure
    reporters: ['verbose'],
    pool: 'forks', // Use separate processes for isolation
    poolOptions: {
      forks: {
        singleFork: true, // Run tests sequentially in a single fork
      },
    },
  },
});
