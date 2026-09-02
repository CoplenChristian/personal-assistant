using System.Text;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Agents;

namespace PersonalAssistant.Harness.Runtime;

public sealed record TerminalLogObservation(
    long LengthBytes,
    bool WarningReached,
    bool Rotated);

public sealed class TerminalLogWriter : IDisposable
{
    public const int MaxWriteBytes = 64 * 1024;

    private readonly object syncRoot = new();
    private readonly string logDirectory;
    private readonly string agentId;
    private readonly string? realm;
    private readonly long warningBytes;
    private readonly long rotationBytes;
    private readonly int retainedFiles;
    private readonly IActivityEventSink? activitySink;
    private bool warningEmitted;
    private bool disposed;

    public TerminalLogWriter(
        string runtimeDirectory,
        string agentId,
        long warningBytes,
        long rotationBytes,
        int retainedFiles,
        IActivityEventSink? activitySink = null,
        string? realm = null)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            throw new ArgumentException("The runtime directory is required.", nameof(runtimeDirectory));
        }

        AgentRegistry.ValidateIdentity(agentId);
        if (warningBytes <= 0 || rotationBytes <= warningBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(rotationBytes), "Terminal log rotation must be greater than its warning threshold.");
        }

        if (retainedFiles is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedFiles), "Terminal log retention must be between one and one hundred files.");
        }

        this.agentId = agentId;
        this.realm = realm;
        this.warningBytes = warningBytes;
        this.rotationBytes = rotationBytes;
        this.retainedFiles = retainedFiles;
        this.activitySink = activitySink;
        logDirectory = Path.Combine(Path.GetFullPath(runtimeDirectory), "agents", agentId, "terminal");
        ActiveLogPath = Path.Combine(logDirectory, "active.log");
    }

    public string ActiveLogPath { get; }

    public TerminalLogObservation Append(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bytes = Encoding.UTF8.GetBytes(data);
        if (bytes.Length > MaxWriteBytes)
        {
            throw new TerminalLogException("terminal_log_write_too_large", "The terminal log write exceeds the bounded chunk size.");
        }

        lock (syncRoot)
        {
            ThrowIfDisposed();
            Directory.CreateDirectory(logDirectory);
            using var stream = new FileStream(
                ActiveLogPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                options: FileOptions.WriteThrough);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
            return ObserveLocked();
        }
    }

    public TerminalLogObservation Observe()
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            return ObserveLocked();
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            disposed = true;
        }
    }

    private TerminalLogObservation ObserveLocked()
    {
        Directory.CreateDirectory(logDirectory);
        var length = File.Exists(ActiveLogPath) ? new FileInfo(ActiveLogPath).Length : 0;
        var warningReached = false;
        var rotated = false;
        if (!warningEmitted && length >= warningBytes)
        {
            warningEmitted = true;
            warningReached = true;
            activitySink?.Append(ActivityEvent.TerminalLogWarning(agentId, realm));
        }

        if (length >= rotationBytes)
        {
            RotateLocked();
            rotated = true;
            length = File.Exists(ActiveLogPath) ? new FileInfo(ActiveLogPath).Length : 0;
        }

        return new TerminalLogObservation(length, warningReached, rotated);
    }

    private void RotateLocked()
    {
        var oldestPath = RotatedPath(retainedFiles);
        DeleteIfPresent(oldestPath);
        for (var index = retainedFiles - 1; index >= 1; index--)
        {
            var source = RotatedPath(index);
            if (File.Exists(source))
            {
                File.Move(source, RotatedPath(index + 1), overwrite: true);
            }
        }

        if (File.Exists(ActiveLogPath))
        {
            File.Move(ActiveLogPath, RotatedPath(1), overwrite: true);
        }

        var temporaryPath = ActiveLogPath + ".rotation.tmp";
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.WriteThrough))
            {
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, ActiveLogPath, overwrite: true);
        }
        finally
        {
            DeleteIfPresent(temporaryPath);
        }

        warningEmitted = false;
        activitySink?.Append(ActivityEvent.TerminalLogRotated(agentId, realm));
    }

    private string RotatedPath(int index) => $"{ActiveLogPath}.{index}";

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalLogWriter));
        }
    }
}

public sealed class TerminalLogException(string code, string message) : AgentLifecycleException(code, message);
