using System.Text;

namespace PersonalAssistant.Harness.Runtime;

public sealed class TmuxTerminalStream : IDisposable
{
    private readonly object syncRoot = new();
    private readonly TmuxSessionManager tmux;
    private readonly string streamRoot;
    private readonly Dictionary<string, StreamState> sessions = new(StringComparer.Ordinal);
    private bool disposed;

    public TmuxTerminalStream(TmuxSessionManager tmux, string runtimeDirectory)
    {
        this.tmux = tmux;
        streamRoot = Path.Combine(Path.GetFullPath(runtimeDirectory), "terminal-streams");
        Directory.CreateDirectory(streamRoot);
    }

    public TmuxPaneSnapshot Capture(string sessionName, int scrollbackLines) =>
        tmux.CapturePane(sessionName, scrollbackLines);

    public bool Publish(string sessionName, string data)
    {
        lock (syncRoot)
        {
            return sessions.TryGetValue(sessionName, out var state) && state.Output.Publish(data);
        }
    }

    public TmuxTerminalStreamLease Subscribe(string sessionName)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (!sessions.TryGetValue(sessionName, out var state))
            {
                var sinkPath = Path.Combine(streamRoot, $"{sessionName}.log");
                var initialOffset = EnsureSink(sinkPath);
                state = new StreamState(sinkPath, new TerminalOutputHub(), new CancellationTokenSource(), initialOffset);
                var output = state.Output.SubscribeAfterCurrent();
                try
                {
                    state.TailTask = Task.Run(() => TailSinkAsync(state, state.Cancellation.Token), state.Cancellation.Token);
                    state.TailReady.Task.GetAwaiter().GetResult();
                    tmux.StartPanePipe(sessionName, sinkPath);
                    sessions.Add(sessionName, state);
                    state.ObserverCount = 1;
                    return new TmuxTerminalStreamLease(this, sessionName, state.SinkPath, output);
                }
                catch
                {
                    output.Dispose();
                    StopTail(state);
                    state.Output.Dispose();
                    throw;
                }
            }

            state.ObserverCount++;
            return new TmuxTerminalStreamLease(this, sessionName, state.SinkPath, state.Output.SubscribeAfterCurrent());
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

            foreach (var sessionName in sessions.Keys.ToArray())
            {
                var state = sessions[sessionName];
                TryStopPanePipe(sessionName);
                StopTail(state);
                state.Output.Dispose();
            }

            sessions.Clear();
            disposed = true;
        }
    }

    internal void Release(string sessionName)
    {
        lock (syncRoot)
        {
            if (disposed || !sessions.TryGetValue(sessionName, out var state))
            {
                return;
            }

            state.ObserverCount--;
            if (state.ObserverCount == 0)
            {
                TryStopPanePipe(sessionName);
                StopTail(state);
                state.Output.Dispose();
                sessions.Remove(sessionName);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(TmuxTerminalStream));
        }
    }

    private static async Task TailSinkAsync(StreamState state, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        try
        {
            await using var file = new FileStream(
                state.SinkPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            file.Seek(state.InitialOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(file, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096);
            state.TailReady.TrySetResult(true);
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (count > 0)
                {
                    state.Output.Publish(new string(buffer, 0, count));
                    continue;
                }

                await Task.Delay(50, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            state.TailReady.TrySetCanceled(cancellationToken);
        }
        catch (IOException exception)
        {
            state.TailReady.TrySetException(exception);
        }
    }

    private static long EnsureSink(string sinkPath)
    {
        using var file = new FileStream(
            sinkPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096);
        return file.Length;
    }

    private void TryStopPanePipe(string sessionName)
    {
        try
        {
            tmux.StopPanePipe(sessionName);
        }
        catch (Exception exception) when (exception is TmuxUnavailableException or TmuxOperationException)
        {
            // The session may already be gone; the observer still needs deterministic cleanup.
        }
    }

    private static void StopTail(StreamState state)
    {
        state.Cancellation.Cancel();
        try
        {
            state.TailTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            state.Cancellation.Dispose();
        }
    }

    private sealed class StreamState(
        string sinkPath,
        TerminalOutputHub output,
        CancellationTokenSource cancellation,
        long initialOffset)
    {
        public string SinkPath { get; } = sinkPath;
        public TerminalOutputHub Output { get; } = output;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public long InitialOffset { get; } = initialOffset;
        public TaskCompletionSource<bool> TailReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? TailTask { get; set; }
        public int ObserverCount { get; set; }
    }
}

public sealed class TmuxTerminalStreamLease : IDisposable
{
    private readonly TmuxTerminalStream owner;
    private bool released;

    internal TmuxTerminalStreamLease(
        TmuxTerminalStream owner,
        string sessionName,
        string sinkPath,
        TerminalOutputSubscription output)
    {
        this.owner = owner;
        SessionName = sessionName;
        SinkPath = sinkPath;
        Output = output;
    }

    public string SessionName { get; }
    public string SinkPath { get; }
    public TerminalOutputSubscription Output { get; }

    public void Dispose()
    {
        if (released)
        {
            return;
        }

        released = true;
        Output.Dispose();
        owner.Release(SessionName);
    }
}
