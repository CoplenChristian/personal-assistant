using System.Threading.Channels;

namespace PersonalAssistant.Harness.Runtime;

public sealed record TerminalOutputMessage(long Sequence, string Data);

public sealed class TerminalOutputHub : IDisposable
{
    public const int DefaultObserverCapacity = 128;

    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, Observer> observers = [];
    private long nextSequence;
    private bool closed;

    public int ObserverCount
    {
        get
        {
            lock (syncRoot)
            {
                return observers.Count;
            }
        }
    }

    public TerminalOutputSubscription Subscribe(int capacity = DefaultObserverCapacity)
        => SubscribeCore(capacity, minimumSequence: 0);

    public TerminalOutputSubscription SubscribeAfterCurrent(int capacity = DefaultObserverCapacity)
    {
        ValidateCapacity(capacity);

        lock (syncRoot)
        {
            if (closed)
            {
                throw new ObjectDisposedException(nameof(TerminalOutputHub));
            }

            return AddObserver(capacity, nextSequence);
        }
    }

    private TerminalOutputSubscription SubscribeCore(int capacity, long minimumSequence)
    {
        ValidateCapacity(capacity);

        lock (syncRoot)
        {
            if (closed)
            {
                throw new ObjectDisposedException(nameof(TerminalOutputHub));
            }

            return AddObserver(capacity, minimumSequence);
        }
    }

    private TerminalOutputSubscription AddObserver(int capacity, long minimumSequence)
    {
        var channel = Channel.CreateBounded<TerminalOutputMessage>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
        var id = Guid.NewGuid();
        observers.Add(id, new Observer(channel, minimumSequence));
        return new TerminalOutputSubscription(this, id, channel.Reader);
    }

    private static void ValidateCapacity(int capacity)
    {
        if (capacity is < 1 or > 10000)
        {
            throw new TerminalProtocolException("observer_capacity_invalid", "The terminal observer buffer size is outside the supported range.");
        }
    }

    public bool Publish(string data)
    {
        TerminalOutputMessage message;
        Observer[] currentObservers;
        lock (syncRoot)
        {
            if (closed)
            {
                return false;
            }

            message = new TerminalOutputMessage(++nextSequence, data);
            currentObservers = observers.Values
                .Where(observer => message.Sequence > observer.MinimumSequence)
                .ToArray();
        }

        var acceptedByAll = true;
        foreach (var observer in currentObservers)
        {
            if (observer.Channel.Writer.TryWrite(message))
            {
                continue;
            }

            acceptedByAll = false;
            RemoveSlowObserver(observer, new TerminalStreamException(
                "terminal_client_slow",
                "The terminal observer could not keep up with the output stream."));
        }

        return acceptedByAll;
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (closed)
            {
                return;
            }

            closed = true;
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

    private void RemoveSlowObserver(Observer observer, Exception reason)
    {
        lock (syncRoot)
        {
            var pair = observers.FirstOrDefault(pair => ReferenceEquals(pair.Value, observer));
            if (pair.Value is null)
            {
                return;
            }

            observers.Remove(pair.Key);
            observer.Channel.Writer.TryComplete(reason);
        }
    }

    private sealed record Observer(Channel<TerminalOutputMessage> Channel, long MinimumSequence);
}

public sealed class TerminalOutputSubscription : IDisposable
{
    private readonly TerminalOutputHub owner;
    private readonly Guid observerId;
    private bool disposed;

    internal TerminalOutputSubscription(
        TerminalOutputHub owner,
        Guid observerId,
        ChannelReader<TerminalOutputMessage> reader)
    {
        this.owner = owner;
        this.observerId = observerId;
        Reader = reader;
    }

    public ChannelReader<TerminalOutputMessage> Reader { get; }

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

public sealed class TerminalStreamException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
