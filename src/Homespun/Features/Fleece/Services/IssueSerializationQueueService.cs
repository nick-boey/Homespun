using System.Threading.Channels;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Background service that processes queued issue write operations asynchronously.
/// Implements a write-through cache pattern where writes are queued and persisted
/// to disk in the background, unblocking the UI thread.
/// </summary>
public sealed class IssueSerializationQueueService : BackgroundService, IIssueSerializationQueue
{
    private readonly Channel<IssueWriteOperation> _channel;
    private readonly ILogger<IssueSerializationQueueService> _logger;
    private int _pendingCount;
    private volatile bool _isProcessing;

    /// <summary>
    /// Maximum number of retry attempts for a failed write operation.
    /// </summary>
    private const int MaxRetries = 3;

    /// <summary>
    /// Base delay for exponential backoff on retries.
    /// </summary>
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Maximum time to wait for the queue to drain during shutdown.
    /// </summary>
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(10);

    public IssueSerializationQueueService(ILogger<IssueSerializationQueueService> logger)
    {
        _logger = logger;

        // Unbounded channel - we don't want to block the UI thread
        _channel = Channel.CreateUnbounded<IssueWriteOperation>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public int PendingCount => _pendingCount;

    /// <inheritdoc />
    public bool IsProcessing => _isProcessing;

    /// <inheritdoc />
    public async ValueTask EnqueueAsync(IssueWriteOperation operation, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(operation, ct);
        Interlocked.Increment(ref _pendingCount);

        _logger.LogDebug(
            "Enqueued {OperationType} for issue '{IssueId}' in project '{ProjectPath}'. Queue depth: {QueueDepth}",
            operation.Type,
            operation.IssueId,
            operation.ProjectPath,
            _pendingCount);
    }

    /// <summary>
    /// Main processing loop. Reads operations from the channel and executes them.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Issue serialization queue service started");

        try
        {
            await foreach (var operation in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessOperationAsync(operation, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown - drain remaining operations
            _logger.LogInformation(
                "Shutdown requested. Draining {PendingCount} remaining operations...",
                _pendingCount);

            await DrainQueueAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Issue serialization queue service encountered a fatal error");
            throw;
        }

        _logger.LogInformation("Issue serialization queue service stopped");
    }

    /// <summary>
    /// Processes a single write operation with retry logic.
    /// </summary>
    private async Task ProcessOperationAsync(IssueWriteOperation operation, CancellationToken ct)
    {
        _isProcessing = true;

        try
        {
            for (var attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    await operation.WriteAction(ct);

                    _logger.LogDebug(
                        "Persisted {OperationType} for issue '{IssueId}' in project '{ProjectPath}'",
                        operation.Type,
                        operation.IssueId,
                        operation.ProjectPath);

                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // Don't retry on cancellation
                }
                catch (Exception ex) when (attempt < MaxRetries)
                {
                    var delay = BaseRetryDelay * Math.Pow(2, attempt);

                    _logger.LogWarning(
                        ex,
                        "Failed to persist {OperationType} for issue '{IssueId}' (attempt {Attempt}/{MaxRetries}). Retrying in {DelayMs}ms...",
                        operation.Type,
                        operation.IssueId,
                        attempt + 1,
                        MaxRetries,
                        delay.TotalMilliseconds);

                    await Task.Delay(delay, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to persist {OperationType} for issue '{IssueId}' after {MaxRetries} attempts. Operation dropped.",
                        operation.Type,
                        operation.IssueId,
                        MaxRetries);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _pendingCount);
            _isProcessing = _pendingCount > 0;
        }
    }

    /// <summary>
    /// Drains remaining operations from the queue during graceful shutdown.
    /// </summary>
    private async Task DrainQueueAsync()
    {
        using var drainCts = new CancellationTokenSource(ShutdownDrainTimeout);

        try
        {
            while (_channel.Reader.TryRead(out var operation))
            {
                await ProcessOperationAsync(operation, drainCts.Token);
            }

            _logger.LogInformation("Queue drained successfully");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Queue drain timed out after {TimeoutSeconds}s. {PendingCount} operations may have been lost.",
                ShutdownDrainTimeout.TotalSeconds,
                _pendingCount);
        }
    }
}
