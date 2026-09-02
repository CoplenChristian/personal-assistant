using System.Text;

namespace PersonalAssistant.Harness.Runtime;

public sealed class TmuxTerminalStream : IDisposable
{
    private const long DefaultLogWarningBytes = 25 * 1024 * 1024;
    private const long DefaultLogRotationBytes = 50 * 1024 * 1024;
    private const int DefaultRetainedLogFiles = 5;
    private readonly object syncRoot = new();
    private readonly TmuxSessionManager tmux;
    private readonly TerminalLogWriter logWriter;
    private readonly Dictionary<string, StreamState> sessions = new(StringComparer.Ordinal);
    private bool disposed;

    public TmuxTerminalStream(TmuxSessionManager tmux, string runtimeDirectory, TerminalLogWriter? logWriter = null)
    {
        this.tmux = tmux;
        this.logWriter = logWriter ?? new TerminalLogWriter(
            runtimeDirectory,
            "personal",
            DefaultLogWarningBytes,
            DefaultLogRotationBytes,
            DefaultRetainedLogFiles);
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
                var sinkPath = logWriter.ActiveLogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(sinkPath)!);
                var initialOffset = EnsureSink(sinkPath);
                state = new StreamState(sinkPath, new TerminalOutputHub(), new CancellationTokenSource(), initialOffset, logWriter);
                state.RestartPipe = () => RestartPanePipe(sessionName, sinkPath);
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
            logWriter.Dispose();
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

    private async Task TailSinkAsync(StreamState state, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        try
        {
            state.TailReady.TrySetResult(true);
            var offset = state.InitialOffset;
            while (!cancellationToken.IsCancellationRequested)
            {
                var rotated = false;
                await using (var file = new FileStream(
                    state.SinkPath,
                    FileMode.OpenOrCreate,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    useAsync: true))
                {
                    if (offset > file.Length)
                    {
                        offset = 0;
                    }

                    file.Seek(offset, SeekOrigin.Begin);
                    using var reader = new StreamReader(file, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096);
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                        if (count > 0)
                        {
                            state.Output.Publish(new string(buffer, 0, count));
                            offset = file.Position;
                            var observation = state.LogWriter.Observe();
                            if (observation.Rotated)
                            {
                                state.RestartPipe?.Invoke();
                                offset = 0;
                                rotated = true;
                                break;
                            }

                            continue;
                        }

                        var idleObservation = state.LogWriter.Observe();
                        if (idleObservation.Rotated)
                        {
                            state.RestartPipe?.Invoke();
                            offset = 0;
                            rotated = true;
                            break;
                        }

                        await Task.Delay(50, cancellationToken);
                    }
                }

                if (!rotated)
                {
                    await Task.Delay(50, cancellationToken);
                }
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

    private void RestartPanePipe(string sessionName, string sinkPath)
    {
        try
        {
            tmux.StopPanePipe(sessionName);
            tmux.StartPanePipe(sessionName, sinkPath);
        }
        catch (Exception exception) when (exception is TmuxUnavailableException or TmuxOperationException)
        {
            // The observer will surface a stream error if the pipe cannot be restarted.
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
        long initialOffset,
        TerminalLogWriter logWriter)
    {
        public string SinkPath { get; } = sinkPath;
        public TerminalOutputHub Output { get; } = output;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public long InitialOffset { get; } = initialOffset;
        public TerminalLogWriter LogWriter { get; } = logWriter;
        public TaskCompletionSource<bool> TailReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? TailTask { get; set; }
        public int ObserverCount { get; set; }
        public Action? RestartPipe { get; set; }
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
