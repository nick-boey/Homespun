import { describe, it, expect } from 'vitest';
import {
  OutputChannel,
  type OutputEvent,
} from '#src/services/session-manager.js';
import { createAssistantMessage } from '../helpers/test-fixtures.js';

describe('OutputChannel hardening', () => {
  it('task 3.3.1: event pushed between iterator re-entries is delivered to next consumer', async () => {
    const channel = new OutputChannel();

    const first = createAssistantMessage();
    channel.push(first);

    {
      const iter = channel[Symbol.asyncIterator]();
      const r = await iter.next();
      expect(r.value).toBe(first);
      // Simulate consumer aborting - return ends the generator
      await iter.return?.(undefined);
    }

    // Producer pushes while no consumer is active.
    const between = createAssistantMessage();
    channel.push(between);

    // Consumer 2 starts iterating.
    const iter2 = channel[Symbol.asyncIterator]();
    const r2 = await iter2.next();
    expect(r2.value).toBe(between);
  });

  it('task 3.3.2: stale resolver is invalidated when a new iterator starts so events land in buffer', async () => {
    const channel = new OutputChannel();

    // Consumer 1 starts and awaits — resolver is installed.
    const iter = channel[Symbol.asyncIterator]();
    const orphaned = iter.next();
    await Promise.resolve();

    // Consumer 1 is abandoned without explicit .return() (e.g. the surrounding
    // async fn was GC'd or the SSE consumer went away). A naive channel would
    // still invoke the orphaned resolver on the next push, silently dropping
    // the event for future consumers.

    // A new consumer starts on the same channel before any push. The new
    // iterator must invalidate the orphaned resolver so the next push() is
    // delivered to this consumer (either via buffer or a fresh resolver)
    // rather than to the orphan.
    const iter2 = channel[Symbol.asyncIterator]();

    // Producer pushes an event. The new iterator must see it even though the
    // orphaned resolver from iter1 was still present at push time.
    const msg = createAssistantMessage();
    // Kick off iter2 so the generator body runs and invalidates the stale
    // resolver before the push.
    const iter2NextPromise = iter2.next();
    await Promise.resolve();
    channel.push(msg);

    const r = await iter2NextPromise;
    expect(r.value).toBe(msg);

    // The orphaned next() from iter1 should resolve with done:true.
    const orphanedResult = await orphaned;
    expect(orphanedResult.done).toBe(true);
  });

  it('task 3.3.3: complete() terminates an in-flight await next()', async () => {
    const channel = new OutputChannel();
    const iter = channel[Symbol.asyncIterator]();
    const pending = iter.next();

    await Promise.resolve();
    channel.complete();

    const result = await pending;
    expect(result.done).toBe(true);
  });

  it('buffered events are delivered in FIFO order across consumer re-entries', async () => {
    const channel = new OutputChannel();
    const a = createAssistantMessage();
    const b = createAssistantMessage();
    const c = createAssistantMessage();

    channel.push(a);
    channel.push(b);
    channel.push(c);

    const collected: OutputEvent[] = [];
    const iter = channel[Symbol.asyncIterator]();
    collected.push((await iter.next()).value!);
    await iter.return?.(undefined);

    const iter2 = channel[Symbol.asyncIterator]();
    collected.push((await iter2.next()).value!);
    collected.push((await iter2.next()).value!);

    expect(collected).toEqual([a, b, c]);
  });
});
