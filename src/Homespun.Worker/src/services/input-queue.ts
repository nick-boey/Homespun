import type { SDKUserMessage } from "@anthropic-ai/claude-agent-sdk";

/**
 * Persistent async input queue for a single worker session.
 *
 * The `@anthropic-ai/claude-agent-sdk` `query({ prompt, ... })` function
 * consumes the supplied `AsyncIterable` via its internal `streamInput`
 * routine, which invokes `transport.endInput()` as soon as the iterable
 * returns. That closes stdin to the CLI subprocess and makes any subsequent
 * `streamInput`, `setPermissionMode`, or `setModel` call fail with
 * `ProcessTransport is not ready for writing`.
 *
 * `InputQueue` works around this by implementing an `AsyncIterable` whose
 * iterator **never returns on its own** — it awaits indefinitely between
 * pushes and only yields `{ done: true }` after `close()` is called. That
 * lets a single `query()` run for the full session lifetime; follow-up
 * messages are delivered via `push(...)` into the same iterable instead of
 * via `q.streamInput(...)`.
 *
 * ## Invariants
 *
 * - **Single-consumer**. Only one `for await` (or equivalent) is expected to
 *   iterate an instance. Concurrent iterators share the same buffer and
 *   resolver slot; behavior with more than one consumer is undefined.
 * - **Iterator does not return until `close()`**. Without `close()` the
 *   consumer's `await next()` suspends forever, which is exactly what the SDK
 *   needs to keep the CLI alive between turns.
 * - **`push()` after `close()` is a no-op**. It does not throw, matches the
 *   spike-validated behavior, and simplifies teardown races.
 */
export class InputQueue implements AsyncIterable<SDKUserMessage> {
  private buffer: SDKUserMessage[] = [];
  private resolver: ((result: IteratorResult<SDKUserMessage>) => void) | null =
    null;
  private closed = false;

  /**
   * Push a message into the queue. If a consumer is currently awaiting
   * `next()`, deliver it directly; otherwise buffer in FIFO order. No-op when
   * the queue has been closed.
   */
  push(msg: SDKUserMessage): void {
    if (this.closed) return;
    if (this.resolver) {
      const resolve = this.resolver;
      this.resolver = null;
      resolve({ value: msg, done: false });
    } else {
      this.buffer.push(msg);
    }
  }

  /**
   * Close the queue. Any in-flight `next()` resolves with `{ done: true }`.
   * Subsequent `next()` calls return `{ done: true }` once the buffer is
   * drained. Idempotent.
   */
  close(): void {
    if (this.closed) return;
    this.closed = true;
    if (this.resolver) {
      const resolve = this.resolver;
      this.resolver = null;
      resolve({ value: undefined as unknown as SDKUserMessage, done: true });
    }
  }

  async *[Symbol.asyncIterator](): AsyncGenerator<SDKUserMessage> {
    while (true) {
      if (this.buffer.length > 0) {
        yield this.buffer.shift()!;
        continue;
      }
      if (this.closed) return;
      const next = await new Promise<IteratorResult<SDKUserMessage>>(
        (resolve) => {
          this.resolver = resolve;
        },
      );
      if (next.done) return;
      yield next.value;
    }
  }
}
