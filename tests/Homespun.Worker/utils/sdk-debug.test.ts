import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { sdkDebug, isSdkDebugEnabled } from '#src/utils/logger.js';

describe('sdkDebug', () => {
  let logSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    logSpy = vi.spyOn(console, 'log').mockImplementation(() => undefined);
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    logSpy.mockRestore();
  });

  it('emits no output when DEBUG_AGENT_SDK is unset', () => {
    vi.stubEnv('DEBUG_AGENT_SDK', '');
    sdkDebug('tx', { hello: 'world' });
    expect(logSpy).not.toHaveBeenCalled();
  });

  it('emits no output when DEBUG_AGENT_SDK is any value other than "true"', () => {
    vi.stubEnv('DEBUG_AGENT_SDK', '1');
    sdkDebug('rx', { x: 1 });
    vi.stubEnv('DEBUG_AGENT_SDK', 'false');
    sdkDebug('rx', { x: 1 });
    vi.stubEnv('DEBUG_AGENT_SDK', 'TRUE');
    sdkDebug('rx', { x: 1 });
    expect(logSpy).not.toHaveBeenCalled();
  });

  it('emits a structured JSON line for tx payloads when enabled', () => {
    vi.stubEnv('DEBUG_AGENT_SDK', 'true');
    sdkDebug('tx', { op: 'setPermissionMode', mode: 'bypassPermissions' });

    expect(logSpy).toHaveBeenCalledOnce();
    const written = logSpy.mock.calls[0][0] as string;
    const parsed = JSON.parse(written);
    expect(parsed.Level).toBe('Debug');
    expect(parsed.Message).toContain('[SDK tx]');
    expect(parsed.Message).toContain('"op":"setPermissionMode"');
    expect(parsed.Message).toContain('"mode":"bypassPermissions"');
  });

  it('emits for rx direction', () => {
    vi.stubEnv('DEBUG_AGENT_SDK', 'true');
    sdkDebug('rx', { type: 'assistant' });

    expect(logSpy).toHaveBeenCalledOnce();
    const written = logSpy.mock.calls[0][0] as string;
    const parsed = JSON.parse(written);
    expect(parsed.Message).toContain('[SDK rx]');
    expect(parsed.Message).toContain('"type":"assistant"');
  });

  it('falls back to String(msg) for non-serializable payloads', () => {
    vi.stubEnv('DEBUG_AGENT_SDK', 'true');
    const cyclic: Record<string, unknown> = {};
    cyclic.self = cyclic;
    expect(() => sdkDebug('tx', cyclic)).not.toThrow();
    expect(logSpy).toHaveBeenCalledOnce();
  });

  it('isSdkDebugEnabled reflects the current env', () => {
    vi.stubEnv('DEBUG_AGENT_SDK', 'true');
    expect(isSdkDebugEnabled()).toBe(true);
    vi.stubEnv('DEBUG_AGENT_SDK', 'false');
    expect(isSdkDebugEnabled()).toBe(false);
  });
});
