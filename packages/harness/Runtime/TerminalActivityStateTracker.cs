using System.Threading.Channels;

namespace PersonalAssistant.Harness.Runtime;

public enum TerminalActivityState
{
    Idle,
    Busy,
    Waiting,
    Error
}

public static class TerminalActivityStateExtensions
{
    public static string ToWireValue(this TerminalActivityState state) => state switch
    {
        TerminalActivityState.Idle => "idle",
        TerminalActivityState.Busy => "busy",
        TerminalActivityState.Waiting => "waiting",
        TerminalActivityState.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}

public sealed class TerminalActivityStateTracker : IDisposable
{
    private const int DefaultObserverCapacity = 16;

    private readonly object syncRoot = new();
    private readonly string logicalAgentId;
    private readonly Dictionary<Guid, Observer> observers = [];
    private TerminalActivityState current = TerminalActivityState.Idle;
    private bool disposed;

    public TerminalActivityStateTracker(string logicalAgentId)
    {
        if (string.IsNullOrWhiteSpace(logicalAgentId))
        {
            throw new TerminalStateException("terminal_agent_invalid", "A terminal state tracker requires a logical agent id.");
        }

        this.logicalAgentId = logicalAgentId;
    }

    public string LogicalAgentId => logicalAgentId;

    public TerminalActivityState Current
    {
        get
        {
            lock (syncRoot)
            {
                return current;
            }
        }
    }

    public TerminalActivityStateSubscription Subscribe(int capacity = DefaultObserverCapacity)
    {
        if (capacity is < 1 or > 256)
        {
            throw new TerminalStateException("terminal_state_observer_invalid", "The terminal state observer capacity is outside the supported range.");
        }

        lock (syncRoot)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(TerminalActivityStateTracker));
            }

            var channel = Channel.CreateBounded<TerminalActivityState>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true,
                AllowSynchronousContinuations = false
            });
            channel.Writer.TryWrite(current);
            var id = Guid.NewGuid();
            observers.Add(id, new Observer(channel));
            return new TerminalActivityStateSubscription(this, id, channel.Reader);
        }
    }

    public void MarkIdle() => SetState(TerminalActivityState.Idle);

    public void MarkBusy() => SetState(TerminalActivityState.Busy);

    public void MarkWaiting() => SetState(TerminalActivityState.Waiting);

    public void MarkError() => SetState(TerminalActivityState.Error);

    public void ResetIfError()
    {
        lock (syncRoot)
        {
            if (disposed || current != TerminalActivityState.Error)
            {
                return;
            }

            SetStateUnderLock(TerminalActivityState.Idle);
        }
    }

    public void ResetForHealthySession(bool hasActiveInput)
    {
        lock (syncRoot)
        {
            if (disposed || hasActiveInput || current is TerminalActivityState.Idle)
            {
                return;
            }

            SetStateUnderLock(TerminalActivityState.Idle);
        }
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
            foreach (var observer in observers.Values)
            {
                observer.Channel.Writer.TryComplete();
            }

            observers.Clear();
        }
    }

    internal void Release(Guid observerId)
    {
        lock (syncRoot)
        {
            if (!observers.Remove(observerId, out var observer))
            {
                return;
            }

            observer.Channel.Writer.TryComplete();
        }
    }

    private void SetState(TerminalActivityState next)
    {
        lock (syncRoot)
        {
            if (disposed || current == next)
            {
                return;
            }

            SetStateUnderLock(next);
        }
    }

    private void SetStateUnderLock(TerminalActivityState next)
    {
        current = next;
        foreach (var pair in observers.ToArray())
        {
            var observer = pair.Value;
            if (observer.Channel.Writer.TryWrite(next))
            {
                continue;
            }

            observers.Remove(pair.Key);
            observer.Channel.Writer.TryComplete(new TerminalStateException(
                "terminal_state_observer_slow",
                "The terminal state observer could not keep up."));
        }
    }

    private sealed record Observer(Channel<TerminalActivityState> Channel);
}

public sealed class TerminalActivityStateSubscription : IDisposable
{
    private readonly TerminalActivityStateTracker owner;
    private readonly Guid observerId;
    private bool disposed;

    internal TerminalActivityStateSubscription(
        TerminalActivityStateTracker owner,
        Guid observerId,
        ChannelReader<TerminalActivityState> reader)
    {
        this.owner = owner;
        this.observerId = observerId;
        Reader = reader;
    }

    public ChannelReader<TerminalActivityState> Reader { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        owner.Release(observerId);
    }
}

public sealed class TerminalStateException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
