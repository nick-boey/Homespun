import { describe, it, expect, vi, afterEach } from 'vitest';
import {
  a2aEmitDebug,
  gateContentPreview,
  isFullMessagesDebugEnabled,
} from '#src/utils/otel-logger.js';
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

describe('a2aEmitDebug (full-body A2A emit-boundary log)', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it('is a no-op when neither umbrella flag nor DEBUG_AGENT_SDK is set', () => {
    const { emit } = installLoggerSpy();
    vi.stubEnv('HOMESPUN_DEBUG_FULL_MESSAGES', '');
    vi.stubEnv('DEBUG_AGENT_SDK', '');
    a2aEmitDebug('session-1', 'message', { foo: 'bar' }, 1);
    expect(emit).not.toHaveBeenCalled();
  });

  it('emits an INFO log with rendered body/kind/seq when HOMESPUN_DEBUG_FULL_MESSAGES=true', () => {
    const { emit } = installLoggerSpy();
    vi.stubEnv('HOMESPUN_DEBUG_FULL_MESSAGES', 'true');
    a2aEmitDebug('session-1', 'message', { foo: 'bar' }, 42);

    expect(emit).toHaveBeenCalledOnce();
    const call = emit.mock.calls[0][0] as EmitCall;
    expect(call.severityNumber).toBe(SeverityNumber.INFO);
    expect(String(call.body)).toContain('a2a.emit');
    expect(String(call.body)).toContain('kind=message');
    expect(String(call.body)).toContain('seq=42');
    expect(String(call.body)).toContain('"foo":"bar"');
    expect(call.attributes?.['homespun.session.id']).toBe('session-1');
    expect(call.attributes?.['homespun.a2a.kind']).toBe('message');
    expect(call.attributes?.['homespun.seq']).toBe(42);
  });

  it('emits under DEBUG_AGENT_SDK=true (back-compat)', () => {
    const { emit } = installLoggerSpy();
    vi.stubEnv('DEBUG_AGENT_SDK', 'true');
    a2aEmitDebug('session-1', 'task', { id: 't-1' });
    expect(emit).toHaveBeenCalledOnce();
  });

  it('omits seq attribute when seq is undefined', () => {
    const { emit } = installLoggerSpy();
    vi.stubEnv('HOMESPUN_DEBUG_FULL_MESSAGES', 'true');
    a2aEmitDebug('session-2', 'task', { ok: true });
    expect(emit).toHaveBeenCalledOnce();
    const call = emit.mock.calls[0][0] as EmitCall;
    expect(call.attributes?.['homespun.seq']).toBeUndefined();
  });

  it('falls back to String(msg) for non-serializable payloads', () => {
    const { emit } = installLoggerSpy();
    vi.stubEnv('HOMESPUN_DEBUG_FULL_MESSAGES', 'true');
    const cyclic: Record<string, unknown> = {};
    cyclic.self = cyclic;
    expect(() => a2aEmitDebug('s', 'message', cyclic, 1)).not.toThrow();
    expect(emit).toHaveBeenCalledOnce();
  });
});

describe('isFullMessagesDebugEnabled', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it('returns true when HOMESPUN_DEBUG_FULL_MESSAGES=true', () => {
    vi.stubEnv('HOMESPUN_DEBUG_FULL_MESSAGES', 'true');
    vi.stubEnv('DEBUG_AGENT_SDK', '');
    expect(isFullMessagesDebugEnabled()).toBe(true);
  });

  it('returns true when DEBUG_AGENT_SDK=true (back-compat)', () => {
    vi.stubEnv('HOMESPUN_DEBUG_FULL_MESSAGES', '');
    vi.stubEnv('DEBUG_AGENT_SDK', 'true');
    expect(isFullMessagesDebugEnabled()).toBe(true);
  });

  it('returns false when both are unset or non-"true"', () => {
    vi.stubEnv('HOMESPUN_DEBUG_FULL_MESSAGES', '');
    vi.stubEnv('DEBUG_AGENT_SDK', '');
    expect(isFullMessagesDebugEnabled()).toBe(false);
    vi.stubEnv('HOMESPUN_DEBUG_FULL_MESSAGES', '1');
    vi.stubEnv('DEBUG_AGENT_SDK', 'yes');
    expect(isFullMessagesDebugEnabled()).toBe(false);
  });
});

describe('gateContentPreview -1 sentinel', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it('returns the full text unchanged when CONTENT_PREVIEW_CHARS=-1', () => {
    vi.stubEnv('CONTENT_PREVIEW_CHARS', '-1');
    const longText = 'a'.repeat(5000);
    expect(gateContentPreview(longText)).toBe(longText);
  });

  it('returns undefined for null/undefined text even under -1', () => {
    vi.stubEnv('CONTENT_PREVIEW_CHARS', '-1');
    expect(gateContentPreview(null)).toBeUndefined();
    expect(gateContentPreview(undefined)).toBeUndefined();
  });

  it('truncates when CONTENT_PREVIEW_CHARS is positive', () => {
    vi.stubEnv('CONTENT_PREVIEW_CHARS', '5');
    expect(gateContentPreview('abcdefgh')).toBe('abcde…');
  });

  it('returns undefined when CONTENT_PREVIEW_CHARS=0', () => {
    vi.stubEnv('CONTENT_PREVIEW_CHARS', '0');
    expect(gateContentPreview('anything')).toBeUndefined();
  });
});
