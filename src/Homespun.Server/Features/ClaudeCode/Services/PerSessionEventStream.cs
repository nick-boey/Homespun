using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Default <see cref="IPerSessionEventStream"/> implementation.
///
/// <para>
/// Opens one long-lived HTTP SSE connection per worker session against the worker's
/// <c>GET /api/sessions/{id}/events</c> endpoint, line-parses every frame, and drives
/// <see cref="ISessionEventIngestor.IngestAsync"/> for every A2A-kind event. Parsed
/// <see cref="SdkMessage"/> values (the four remaining control-plane variants) are
/// fanned out to a single active per-turn subscriber through an unbounded
/// <see cref="Channel{T}"/>. When no turn subscriber is currently attached, messages
/// are stored in a bounded pre-subscribe FIFO (cap
/// <see cref="Reader.PreSubscribeBufferCapacity"/>) and drained into the subscriber's
/// channel on attach; on overflow the oldest entry is evicted and a Warning logged.
/// This closes the subscribe-race window where a <c>canUseTool</c> hook or
/// status-update completed could arrive before the server called
/// <see cref="IPerSessionEventStream.SubscribeTurnAsync"/>. The A2A/ingestor path runs
/// unconditionally, so content-bearing events are never at risk.
/// </para>
///
/// <para>
/// The reader is deliberately tolerant: malformed JSON, unknown event kinds, and
/// transient dispatch exceptions are logged at Warning and swallowed so a single bad
/// frame never tears down the stream. The loop only terminates on cancellation,
/// disposal, or when the worker's HTTP response closes. In all three cases any
/// pending turn subscriber's channel writer is completed so the consumer's
/// <c>await foreach</c> exits cleanly.
/// </para>
/// </summary>
public sealed class PerSessionEventStream : IPerSessionEventStream, IAsyncDisposable
{
    private readonly ISessionEventIngestor _ingestor;
    private readonly HttpClient _httpClient;
    private readonly ILogger<PerSessionEventStream> _logger;
    private readonly ConcurrentDictionary<string, Reader> _readers = new();
    private int _disposed;

    public PerSessionEventStream(
        ISessionEventIngestor ingestor,
        HttpClient httpClient,
        ILogger<PerSessionEventStream> logger)
    {
        _ingestor = ingestor;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(
        string homespunSessionId,
        string workerUrl,
        string workerSessionId,
        string? projectId,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(PerSessionEventStream));
        }

        // Idempotent: a second call for the same homespunSessionId returns the running reader.
        _readers.GetOrAdd(homespunSessionId, id =>
        {
            var reader = new Reader(
                this,
                id,
                workerUrl,
                workerSessionId,
                projectId);
            reader.Start();
            return reader;
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SdkMessage> SubscribeTurnAsync(
        string homespunSessionId,
        CancellationToken cancellationToken)
    {
        if (!_readers.TryGetValue(homespunSessionId, out var reader))
        {
            throw new InvalidOperationException(
                $"No reader running for session {homespunSessionId}; call StartAsync first.");
        }

        var channel = reader.BeginTurn();
        return ReadTurnAsync(reader, channel, cancellationToken);
    }

    private static async IAsyncEnumerable<SdkMessage> ReadTurnAsync(
        Reader reader,
        Channel<SdkMessage> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return msg;
                if (msg is SdkResultMessage)
                {
                    // End the turn as soon as the result arrives; post-result events continue
                    // to flow through the reader to the ingestor for broadcast.
                    yield break;
                }
            }
        }
        finally
        {
            reader.EndTurn(channel);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(string homespunSessionId)
    {
        if (_readers.TryRemove(homespunSessionId, out var reader))
        {
            await reader.DisposeAsync();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var key in _readers.Keys.ToArray())
        {
            if (_readers.TryRemove(key, out var reader))
            {
                try
                {
                    await reader.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing reader for session {SessionId}", key);
                }
            }
        }
    }

    /// <summary>
    /// Per-session background-read state. Owns the CTS, the background task, and the
    /// optional channel for the current turn subscriber. Turn state mutations are
    /// serialized with <c>_turnLock</c>; the read loop takes the lock only when it
    /// needs to fan a parsed <see cref="SdkMessage"/> out to the subscriber or when
    /// it completes the writer at shutdown.
    /// </summary>
    private sealed class Reader : IAsyncDisposable
    {
        /// <summary>
        /// Cap on messages held in the pre-subscribe FIFO. 256 comfortably covers a
        /// typical burst of control-plane messages (system/question/plan/result) that
        /// can race the subscribe window while preventing unbounded growth on idle
        /// sessions where a subscriber never attaches.
        /// </summary>
        internal const int PreSubscribeBufferCapacity = 256;

        private readonly PerSessionEventStream _owner;
        private readonly string _homespunSessionId;
        private readonly string _workerUrl;
        private readonly string _workerSessionId;
        private readonly string? _projectId;
        private readonly CancellationTokenSource _cts = new();
        private readonly object _turnLock = new();
        private readonly Queue<SdkMessage> _preSubscribeBuffer = new();
        private Channel<SdkMessage>? _turnChannel;
        private Task? _readTask;
        private int _droppedPreSubscribeCount;

        public Reader(
            PerSessionEventStream owner,
            string homespunSessionId,
            string workerUrl,
            string workerSessionId,
            string? projectId)
        {
            _owner = owner;
            _homespunSessionId = homespunSessionId;
            _workerUrl = workerUrl;
            _workerSessionId = workerSessionId;
            _projectId = projectId;
        }

        public void Start()
        {
            // Detached — ownership is the CTS + the task handle captured below. We explicitly
            // do NOT flow the caller's CancellationToken into the reader's lifetime; the
            // reader lives until Stop/Dispose is invoked on this service.
            _readTask = Task.Run(ReadLoopAsync);
        }

        public Channel<SdkMessage> BeginTurn()
        {
            lock (_turnLock)
            {
                if (_turnChannel is not null)
                {
                    throw new InvalidOperationException(
                        $"A turn subscription is already active for session {_homespunSessionId}; only one turn at a time is supported.");
                }

                // Unbounded: post-result background events ride the same reader but never
                // reach the turn subscriber (yield break on SdkResultMessage), so head-of-line
                // blocking is not a concern. A bounded channel would risk dropping control
                // messages (system/question/plan) under bursty Claude SDK behaviour.
                var channel = Channel.CreateUnbounded<SdkMessage>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false,
                });

                // Drain any messages the reader buffered before a subscriber attached.
                // TryWrite is synchronous on unbounded channels and always succeeds, so
                // this is safe to do under the lock. Preserving the FIFO order means the
                // subscriber sees the same sequence the reader observed.
                while (_preSubscribeBuffer.TryDequeue(out var buffered))
                {
                    if (!channel.Writer.TryWrite(buffered))
                    {
                        // Unreachable for unbounded channels, but defensive.
                        _owner._logger.LogWarning(
                            "PerSessionEventStream {SessionId} failed to drain buffered message into new turn channel",
                            _homespunSessionId);
                    }
                }

                _turnChannel = channel;
                return channel;
            }
        }

        public void EndTurn(Channel<SdkMessage> channel)
        {
            lock (_turnLock)
            {
                if (ReferenceEquals(_turnChannel, channel))
                {
                    _turnChannel = null;
                    channel.Writer.TryComplete();
                }
            }
        }

        private async Task ReadLoopAsync()
        {
            var ct = _cts.Token;
            var url = $"{_workerUrl.TrimEnd('/')}/api/sessions/{_workerSessionId}/events";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

                using var response = await _owner._httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!response.IsSuccessStatusCode)
                {
                    _owner._logger.LogWarning(
                        "Worker event stream for session {SessionId} returned {StatusCode}",
                        _homespunSessionId, response.StatusCode);
                    return;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                string? currentEventType = null;
                var dataBuffer = new StringBuilder();

                while (!ct.IsCancellationRequested)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync(ct);
                    }
                    catch (HttpIOException ex) when (ex.HttpRequestError == HttpRequestError.ResponseEnded)
                    {
                        _owner._logger.LogDebug(ex,
                            "Worker event stream for session {SessionId} ended", _homespunSessionId);
                        break;
                    }

                    if (line is null) break;

                    if (line.StartsWith("event: ", StringComparison.Ordinal))
                    {
                        currentEventType = line[7..];
                    }
                    else if (line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        dataBuffer.Append(line[6..]);
                    }
                    else if (string.IsNullOrEmpty(line))
                    {
                        if (!string.IsNullOrEmpty(currentEventType) && dataBuffer.Length > 0)
                        {
                            await DispatchAsync(currentEventType, dataBuffer.ToString(), ct);
                        }

                        currentEventType = null;
                        dataBuffer.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on Stop/Dispose.
            }
            catch (Exception ex)
            {
                _owner._logger.LogWarning(ex,
                    "Worker event stream for session {SessionId} ended with unexpected error",
                    _homespunSessionId);
            }
            finally
            {
                // Complete any subscriber so the consumer's await-foreach exits cleanly.
                lock (_turnLock)
                {
                    _turnChannel?.Writer.TryComplete();
                    _turnChannel = null;
                }
            }
        }

        private async Task DispatchAsync(string eventKind, string rawData, CancellationToken ct)
        {
            // Ingest A2A-kind events regardless of whether a turn subscriber is attached —
            // that is how post-result background events reach the client after a turn has
            // ended.
            if (A2AMessageParser.IsA2AEventKind(eventKind))
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawData);
                    var payload = doc.RootElement.Clone();
                    var effectiveProjectId = string.IsNullOrEmpty(_projectId) ? "unknown" : _projectId;
                    await _owner._ingestor.IngestAsync(
                        effectiveProjectId, _homespunSessionId, eventKind, payload, ct);
                }
                catch (Exception ex)
                {
                    _owner._logger.LogWarning(ex,
                        "Failed to ingest {EventKind} for session {SessionId}; continuing",
                        eventKind, _homespunSessionId);
                }
            }

            // Translate to a control-plane SdkMessage for the current turn subscriber, if any.
            SdkMessage? sdkMessage;
            try
            {
                sdkMessage = ParseToSdkMessage(eventKind, rawData);
            }
            catch (Exception ex)
            {
                _owner._logger.LogWarning(ex,
                    "Failed to parse {EventKind} to SdkMessage for session {SessionId}",
                    eventKind, _homespunSessionId);
                return;
            }

            if (sdkMessage is null) return;

            Channel<SdkMessage>? channel;
            lock (_turnLock)
            {
                channel = _turnChannel;
                if (channel is null)
                {
                    // No subscriber yet — park the message in the pre-subscribe FIFO so
                    // it is replayed when SubscribeTurnAsync attaches. Oldest-first
                    // eviction on overflow: the A2A/ingestor path has already persisted
                    // content-bearing events, so overflow only drops control-plane
                    // signals. Warn loudly.
                    if (_preSubscribeBuffer.Count >= PreSubscribeBufferCapacity)
                    {
                        _preSubscribeBuffer.Dequeue();
                        _droppedPreSubscribeCount++;
                        _owner._logger.LogWarning(
                            "PerSessionEventStream {SessionId} pre-subscribe buffer overflow — dropped oldest control message (cumulative dropped: {DroppedCount})",
                            _homespunSessionId, _droppedPreSubscribeCount);
                    }
                    _preSubscribeBuffer.Enqueue(sdkMessage);
                    return;
                }
            }

            try
            {
                await channel.Writer.WriteAsync(sdkMessage, ct);
            }
            catch (ChannelClosedException)
            {
                // Subscriber completed during a race — drop the message.
            }
            catch (OperationCanceledException)
            {
                // Shutdown path.
            }
        }

        /// <summary>
        /// Mirrors <c>DockerAgentExecutionService.ParseSseEvent</c>'s kind-to-SdkMessage
        /// mapping for the four legacy control events plus the A2A-native path through
        /// <see cref="A2AMessageParser.ConvertToSdkMessage"/>. Unknown kinds return null.
        /// </summary>
        private SdkMessage? ParseToSdkMessage(string eventKind, string rawData)
        {
            if (A2AMessageParser.IsA2AEventKind(eventKind))
            {
                var parsed = A2AMessageParser.ParseSseEvent(eventKind, rawData);
                return parsed is null
                    ? null
                    : A2AMessageParser.ConvertToSdkMessage(parsed, _homespunSessionId);
            }

            switch (eventKind)
            {
                case "session_started":
                {
                    using var doc = JsonDocument.Parse(rawData);
                    var workerSessionId = doc.RootElement.TryGetProperty("sessionId", out var sid)
                        ? sid.GetString()
                        : null;
                    return new SdkSystemMessage(
                        workerSessionId ?? _homespunSessionId,
                        null,
                        "session_started",
                        null,
                        null);
                }
                case "question_pending":
                    return new SdkQuestionPendingMessage(_homespunSessionId, rawData);
                case "plan_pending":
                    return new SdkPlanPendingMessage(_homespunSessionId, rawData);
                case "error":
                    _owner._logger.LogWarning(
                        "Received error event from worker for session {SessionId}: {Data}",
                        _homespunSessionId, rawData);
                    return null;
                default:
                    return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed — nothing to cancel.
            }

            if (_readTask is not null)
            {
                try
                {
                    await _readTask;
                }
                catch
                {
                    // Swallowed — the read loop already logs its own failures.
                }
            }

            lock (_turnLock)
            {
                _turnChannel?.Writer.TryComplete();
                _turnChannel = null;
            }

            _cts.Dispose();
        }
    }
}
