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
    root: '../../tests/Homespun.Worker',
    globals: true,
    environment: 'node',
    restoreMocks: true,
  },
});
