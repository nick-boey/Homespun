import { describe, it, expect } from 'vitest';
import type { SDKUserMessage } from '@anthropic-ai/claude-agent-sdk';
import { InputQueue } from '#src/services/input-queue.js';

function userMessage(text: string): SDKUserMessage {
  return {
    type: 'user',
    session_id: '',
    parent_tool_use_id: null,
    message: { role: 'user', content: [{ type: 'text', text }] },
  };
}

describe('InputQueue', () => {
  it('task 2.3.1: push-then-iterate yields the buffered message', async () => {
    const q = new InputQueue();
    const msg = userMessage('hello');
    q.push(msg);

    const iter = q[Symbol.asyncIterator]();
    const result = await iter.next();
    expect(result.done).toBe(false);
    expect(result.value).toBe(msg);
  });

  it('task 2.3.2: iterate-then-push resolves the pending next()', async () => {
    const q = new InputQueue();
    const iter = q[Symbol.asyncIterator]();

    const nextPromise = iter.next();
    const msg = userMessage('delayed');

    // Defer the push until after next() has subscribed as the resolver
    await Promise.resolve();
    q.push(msg);

    const result = await nextPromise;
    expect(result.done).toBe(false);
    expect(result.value).toBe(msg);
  });

  it('task 2.3.3: multiple pushes deliver in FIFO order', async () => {
    const q = new InputQueue();
    const m1 = userMessage('first');
    const m2 = userMessage('second');
    const m3 = userMessage('third');
    q.push(m1);
    q.push(m2);
    q.push(m3);

    const iter = q[Symbol.asyncIterator]();
    const r1 = await iter.next();
    const r2 = await iter.next();
    const r3 = await iter.next();

    expect(r1.value).toBe(m1);
    expect(r2.value).toBe(m2);
    expect(r3.value).toBe(m3);
  });

  it('task 2.3.4: close() resolves the in-flight next() with done:true', async () => {
    const q = new InputQueue();
    const iter = q[Symbol.asyncIterator]();
    const nextPromise = iter.next();

    await Promise.resolve();
    q.close();

    const result = await nextPromise;
    expect(result.done).toBe(true);
  });

  it('close() causes subsequent next() to return done:true after buffer drained', async () => {
    const q = new InputQueue();
    const m1 = userMessage('m1');
    q.push(m1);
    q.close();

    const iter = q[Symbol.asyncIterator]();
    const r1 = await iter.next();
    expect(r1.done).toBe(false);
    expect(r1.value).toBe(m1);
    const r2 = await iter.next();
    expect(r2.done).toBe(true);
  });

  it('task 2.3.5: push() after close() is a no-op (does not throw)', async () => {
    const q = new InputQueue();
    q.close();
    expect(() => q.push(userMessage('late'))).not.toThrow();

    const iter = q[Symbol.asyncIterator]();
    const result = await iter.next();
    expect(result.done).toBe(true);
  });
});
