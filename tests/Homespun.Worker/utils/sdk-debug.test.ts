import { describe, it, expect, vi, afterEach } from 'vitest';
import { sdkDebug, isSdkDebugEnabled } from '#src/utils/otel-logger.js';
import { logs, SeverityNumber } from '@opentelemetry/api-logs';

type EmitCall = {
  severityNumber?: number;
  body?: unknown;
  attributes?: Record<string, unknown>;
};

function installLoggerSpy(): { emit: ReturnType<typeof vi.fn> } {
  const emit = vi.fn();
  const logger = { emit } as unknown as ReturnType<typeof logs.getLogger>;
  vi.spyOn(logs, 'getLogger').mockReturnValue(logger);
  return { emit };
}

describe('sdkDebug (OTel-backed)', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it('emits no log record when DEBUG_AGENT_SDK is unset', () => {
    const { emit } = installLoggerSpy();
    vi.stubEnv('DEBUG_AGENT_SDK', '');
    sdkDebug('tx', { hello: 'world' });
    expect(emit).not.toHaveBeenCalled();
  });

  it('emits no log record when DEBUG_AGENT_SDK is anything other than "true"', () => {
    const { emit } = installLoggerSpy();
    vi.stubEnv('DEBUG_AGENT_SDK', '1');
    sdkDebug('rx', { x: 1 });
    vi.stubEnv('DEBUG_AGENT_SDK', 'false');
    sdkDebug('rx', { x: 1 });
    vi.stubEnv('DEBUG_AGENT_SDK', 'TRUE');
    sdkDebug('rx', { x: 1 });
    expect(emit).not.toHaveBeenCalled();
  });

  it('emits a DEBUG-severity log record for tx payloads when enabled', () => {
    const { emit } = installLoggerSpy();
    vi.stubEnv('DEBUG_AGENT_SDK', 'true');
    sdkDebug('tx', { op: 'setPermissionMode', mode: 'bypassPermissions' });

    expect(emit).toHaveBeenCalledOnce();
    const call = emit.mock.calls[0][0] as EmitCall;
    expect(call.severityNumber).toBe(SeverityNumber.DEBUG);
    expect(String(call.body)).toContain('[SDK tx]');
    expect(String(call.body)).toContain('"op":"setPermissionMode"');
    expect(call.attributes?.['sdk.direction']).toBe('tx');
  });

  it('emits for rx direction', () => {
    const { emit } = installLoggerSpy();
    vi.stubEnv('DEBUG_AGENT_SDK', 'true');
    sdkDebug('rx', { type: 'assistant' });

    expect(emit).toHaveBeenCalledOnce();
    const call = emit.mock.calls[0][0] as EmitCall;
    expect(String(call.body)).toContain('[SDK rx]');
    expect(String(call.body)).toContain('"type":"assistant"');
    expect(call.attributes?.['sdk.direction']).toBe('rx');
  });

  it('falls back to String(msg) for non-serializable payloads', () => {
    const { emit } = installLoggerSpy();
    vi.stubEnv('DEBUG_AGENT_SDK', 'true');
    const cyclic: Record<string, unknown> = {};
    cyclic.self = cyclic;
    expect(() => sdkDebug('tx', cyclic)).not.toThrow();
    expect(emit).toHaveBeenCalledOnce();
  });

  it('isSdkDebugEnabled reflects the current env', () => {
    vi.stubEnv('DEBUG_AGENT_SDK', 'true');
    expect(isSdkDebugEnabled()).toBe(true);
    vi.stubEnv('DEBUG_AGENT_SDK', 'false');
    expect(isSdkDebugEnabled()).toBe(false);
  });
});
