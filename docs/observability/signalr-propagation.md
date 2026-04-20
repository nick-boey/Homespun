# SignalR trace-context propagation

WebSocket transports do not expose per-message HTTP headers, so the browser
cannot use the default fetch/XHR auto-instrumentation to carry a W3C
`traceparent` across a SignalR invocation. Homespun solves this with two
symmetric mechanisms — one for each direction of the pipe.

## Client → server: first-arg convention

```
┌──────────────────────┐     invoke("SendMessage", tp,        ┌──────────────────┐
│  Web tracer          │     sessionId, text)                 │  Server          │
│  (WebTracerProvider) │─────────────────────────────────────►│  TraceparentHub  │
│                      │     arg0 = "00-<trace>-<span>-01"    │  Filter          │
│  traceInvoke(...)    │                                      │                  │
│  - starts client span│                                      │  parses arg0 →   │
│  - tp = inject()     │                                      │  ActivityContext │
│  - prepends tp arg0  │                                      │  starts server   │
└──────────────────────┘                                      │  span parented   │
                                                              │  to client span  │
                                                              └──────────────────┘
```

- **Helper**: `src/Homespun.Web/src/lib/signalr/trace.ts` — `traceInvoke`
  wraps any hub invoke. It starts a `Homespun.signalr.client` span,
  injects the active context into a `traceparent` string, and prepends it
  as arg 0 on the wire.
- **Server filter**:
  `src/Homespun.Server/Features/Observability/TraceparentHubFilter.cs`.
  Every hub method's arg 0 is a string traceparent. The filter parses it,
  starts a `Homespun.Signalr` activity named `SignalR.{Hub}/{Method}` with
  `ActivityKind.Server`, explicit parent, and exception recording. When
  arg 1 is a string it is tagged as `homespun.session.id`.
- **Hub methods**: every method on `ClaudeCodeHub` takes `string traceparent`
  as its first parameter. Method bodies themselves ignore the value — the
  filter is the only layer that reads it.

Missing or malformed traceparent values are not errors: the filter starts
a root span so the hub call still succeeds, and the span appears as an
orphan in Seq. Useful during E2E tests and while migrating old clients.

## Server → client: envelope round-trip

```
┌──────────────────────┐     ReceiveSessionEvent(envelope)    ┌──────────────────┐
│  Server activity     │                                      │  Web             │
│  (any source)        │─────────────────────────────────────►│                  │
│                      │     envelope.Traceparent =           │  handler wraps   │
│  BroadcastSession    │     Activity.Current.Traceparent     │  cb in           │
│  Event sets envelope │                                      │  withExtracted   │
│  .Traceparent before │                                      │  Context(env)    │
│  Clients.All.Send... │                                      │  → span parented │
└──────────────────────┘                                      │  to server span  │
                                                              └──────────────────┘
```

- `SessionEventEnvelope.Traceparent` (nullable `string`) carries the
  current activity's W3C traceparent on every broadcast. Producers set it
  via a helper extension on `Activity`; consumers without an active
  activity ship a `null` and the client starts a fresh root span.
- The client's `withExtractedContext(envelope, fn)` parses the envelope
  traceparent, sets it as the active OTel context for the duration of
  `fn`, and creates child spans naturally under that parent. Any fetch
  kicked off inside `fn` propagates the server's trace into its request
  headers automatically.

## Why a custom filter, not the native source

`Microsoft.AspNetCore.SignalR.Server` emits a hub activity natively, but
it is constructed **before** the invocation's arguments are visible to
user code — there is no hook to attach a parent context pulled off arg 0.
Registering both the native source and the filter would also double-count
every call. The filter is explicitly registered and the native source is
explicitly **not** added to the tracer provider in
`Homespun.ServiceDefaults/Extensions.cs`.

## At a glance

| Direction       | Mechanism                         | Carrier                              |
|-----------------|-----------------------------------|--------------------------------------|
| Client → server | first-method-arg convention       | `string traceparent` (W3C)           |
| Server → client | envelope field                    | `SessionEventEnvelope.Traceparent`   |

Together they produce a single continuous trace across user click →
fetch → hub filter → session service → worker session init → A2A emits.
