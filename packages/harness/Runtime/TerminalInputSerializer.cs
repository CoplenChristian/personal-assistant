using System.Text;
using System.Threading.Channels;
using PersonalAssistant.Harness.Agents;

namespace PersonalAssistant.Harness.Runtime;

public sealed record TerminalInputRequest(long Sequence, string Data);

public sealed record TerminalInputAcknowledgement(long Sequence);

public sealed class TerminalInputSerializer : IDisposable
{
    public const int DefaultQueueCapacity = 64;

    private readonly object syncRoot = new();
    private readonly string logicalAgentId;
    private readonly Func<TerminalInputRequest, CancellationToken, Task> operation;
    private readonly Channel<WorkItem> queue;
    private readonly CancellationTokenSource shutdown = new();
    private readonly HashSet<WorkItem> pending = [];
    private readonly Task worker;
    private bool disposed;
    private WorkItem? inFlight;

    public TerminalInputSerializer(
        string logicalAgentId,
        Func<TerminalInputRequest, CancellationToken, Task> operation,
        int queueCapacity = DefaultQueueCapacity)
    {
        if (string.IsNullOrWhiteSpace(logicalAgentId))
        {
            throw new TerminalInputException("terminal_agent_invalid", "A terminal input serializer requires a logical agent id.");
        }

        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        if (queueCapacity is < 1 or > 10000)
        {
            throw new TerminalInputException("terminal_input_queue_invalid", "The terminal input queue size is outside the supported range.");
        }

        this.logicalAgentId = logicalAgentId;
        this.operation = operation;
        queue = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
        worker = Task.Run(ProcessQueueAsync);
    }

    public string LogicalAgentId => logicalAgentId;

    public event Action? BecameQuiescent;

    public int QueuedCount
    {
        get
        {
            lock (syncRoot)
            {
                return pending.Count - (inFlight is null ? 0 : 1);
            }
        }
    }

    public bool HasInFlightOperation
    {
        get
        {
            lock (syncRoot)
            {
                return inFlight is not null;
            }
        }
    }

    public Task<TerminalInputAcknowledgement> EnqueueAsync(
        long sequence,
        string data,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(sequence, data);
        WorkItem item;
        lock (syncRoot)
        {
            ThrowIfDisposed();
            item = new WorkItem(new TerminalInputRequest(sequence, data), cancellationToken);
            pending.Add(item);
            if (!queue.Writer.TryWrite(item))
            {
                pending.Remove(item);
                item.Dispose();
                throw new TerminalInputException(
                    "terminal_input_queue_full",
                    "The terminal input queue is full; try again after the native session catches up.");
            }
        }

        return AwaitCompletionAsync(item, cancellationToken);
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            queue.Writer.TryComplete();
            shutdown.Cancel();
            CompletePending(new TerminalInputException(
                "terminal_input_unavailable",
                "The terminal input serializer is shutting down."));
        }

        try
        {
            worker.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            shutdown.Dispose();
        }
    }

    private async Task<TerminalInputAcknowledgement> AwaitCompletionAsync(WorkItem item, CancellationToken cancellationToken)
        => await item.Completion.Task.WaitAsync(cancellationToken);

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(shutdown.Token))
            {
                lock (syncRoot)
                {
                    if (!pending.Contains(item))
                    {
                        item.Dispose();
                        continue;
                    }

                    inFlight = item;
                }

                TerminalInputAcknowledgement? acknowledgement = null;
                TerminalInputException? failure = null;
                try
                {
                    if (item.Cancellation.IsCancellationRequested)
                    {
                        failure = new TerminalInputException(
                            "terminal_input_cancelled",
                            "The queued terminal input was cancelled before delivery.");
                    }
                    else
                    {
                        await operation(item.Request, item.Cancellation.Token);
                        acknowledgement = new TerminalInputAcknowledgement(item.Request.Sequence);
                    }
                }
                catch (OperationCanceledException) when (item.Cancellation.IsCancellationRequested)
                {
                    failure = new TerminalInputException(
                        "terminal_input_cancelled",
                        "The terminal input operation was cancelled before completion.");
                }
                catch (TerminalInputException exception)
                {
                    failure = exception;
                }
                catch (AgentLifecycleException exception)
                {
                    failure = new TerminalInputException(
                        exception.Code,
                        "The native session rejected the terminal input operation.");
                }
                catch (TmuxUnavailableException)
                {
                    failure = new TerminalInputException(
                        "terminal_input_unavailable",
                        "The tmux input boundary is unavailable.");
                }
                catch (Exception)
                {
                    failure = new TerminalInputException(
                        "terminal_input_failed",
                        $"The terminal input operation failed for logical agent {logicalAgentId}.");
                }
                finally
                {
                    var becameQuiescent = false;
                    lock (syncRoot)
                    {
                        pending.Remove(item);
                        if (ReferenceEquals(inFlight, item))
                        {
                            inFlight = null;
                        }

                        becameQuiescent = pending.Count == 0 && inFlight is null;
                    }

                    if (acknowledgement is not null)
                    {
                        item.Completion.TrySetResult(acknowledgement);
                    }
                    else
                    {
                        item.Completion.TrySetException(failure ?? new TerminalInputException(
                            "terminal_input_failed",
                            $"The terminal input operation failed for logical agent {logicalAgentId}."));
                    }

                    item.Dispose();
                    if (becameQuiescent)
                    {
                        BecameQuiescent?.Invoke();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            var becameQuiescent = false;
            lock (syncRoot)
            {
                CompletePending(new TerminalInputException(
                    "terminal_input_unavailable",
                    "The terminal input serializer is no longer available."));
                inFlight = null;
                becameQuiescent = pending.Count == 0;
            }

            if (becameQuiescent)
            {
                BecameQuiescent?.Invoke();
            }
        }
    }

    private void CompletePending(TerminalInputException exception)
    {
        foreach (var item in pending.ToArray())
        {
            item.Cancellation.Cancel();
            item.Completion.TrySetException(exception);
            pending.Remove(item);
            if (!ReferenceEquals(item, inFlight))
            {
                item.Dispose();
            }
        }
    }

    private static void ValidateRequest(long sequence, string data)
    {
        if (sequence < 0)
        {
            throw new TerminalInputException("terminal_input_sequence_invalid", "Terminal input sequence must be non-negative.");
        }

        if (data is null || data.Length == 0)
        {
            throw new TerminalInputException("terminal_input_empty", "Terminal input data cannot be empty.");
        }

        if (Encoding.UTF8.GetByteCount(data) > TerminalProtocol.MaxPayloadBytes)
        {
            throw new TerminalInputException("terminal_input_too_large", "The terminal input frame exceeds the configured limit.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new TerminalInputException("terminal_input_unavailable", "The terminal input serializer is no longer available.");
        }
    }

    private sealed class WorkItem : IDisposable
    {
        private readonly CancellationTokenRegistration callerCancellation;
        private int disposed;

        public WorkItem(TerminalInputRequest request, CancellationToken callerToken)
        {
            Request = request;
            Cancellation = new CancellationTokenSource();
            Completion = new TaskCompletionSource<TerminalInputAcknowledgement>(TaskCreationOptions.RunContinuationsAsynchronously);
            callerCancellation = callerToken.Register(static state => ((WorkItem)state!).Cancellation.Cancel(), this);
        }

        public TerminalInputRequest Request { get; }
        public CancellationTokenSource Cancellation { get; }
        public TaskCompletionSource<TerminalInputAcknowledgement> Completion { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            callerCancellation.Dispose();
            Cancellation.Dispose();
        }
    }
}

public sealed class TerminalInputException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
