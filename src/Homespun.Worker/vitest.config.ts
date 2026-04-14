import { defineConfig } from 'vitest/config';
import { resolve } from 'node:path';

const workerRoot = __dirname;
const sdkPath = resolve(workerRoot, 'node_modules/@anthropic-ai/claude-agent-sdk/sdk.mjs');

export default defineConfig({
  resolve: {
    alias: {
      '#src': resolve(workerRoot, 'src'),
      // Force SDK to resolve to same module in both test and source contexts
      '@anthropic-ai/claude-agent-sdk': sdkPath,
    },
  },
  test: {
    // Use `include`/`dir` rather than `root` so Vitest's project root stays at
    // the worker package. Using `root` to point at the tests tree caused v8
    // coverage to scope to tests/ and miss the worker's own src/ entirely.
    dir: '../../tests/Homespun.Worker',
    include: ['../../tests/Homespun.Worker/**/*.{test,spec}.ts'],
    globals: true,
    environment: 'node',
    restoreMocks: true,
    exclude: ['**/live/**', '**/node_modules/**'],
    // Constitution §V coverage. With project root = workerRoot, include/exclude
    // are resolved relative to src/Homespun.Worker and match as expected.
    coverage: {
      provider: 'v8',
      reporter: ['text', 'lcov', 'cobertura', 'json'],
      reportsDirectory: resolve(workerRoot, 'coverage'),
      include: ['src/**/*.ts'],
      exclude: [
        'src/**/*.d.ts',
        'src/**/*.test.ts',
        'src/**/*.spec.ts',
        'node_modules/**',
      ],
      all: true,
    },
  },
});
