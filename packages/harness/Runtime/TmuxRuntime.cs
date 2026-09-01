using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PersonalAssistant.Harness.Agents;

namespace PersonalAssistant.Harness.Runtime;

public sealed record TmuxCommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface ITmuxCommandExecutor
{
    TmuxCommandResult Execute(IReadOnlyList<string> arguments);
}

public interface ICancellableTmuxCommandExecutor
{
    Task<TmuxCommandResult> ExecuteAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public interface INativeProcessInspector
{
    ProcessObservation Inspect(int processId, string expectedExecutable);
}

public sealed record ProcessObservation(bool IsAlive, bool IsExpectedRuntime);

public sealed record TmuxHealth(
    bool SessionDetected,
    bool RuntimeHealthy,
    SessionObservedState ObservedState,
    string? Error,
    bool RepairEligible = false);

public sealed record TmuxPaneSnapshot(string Data, int ScrollbackLines);

public sealed class ProcessTmuxCommandExecutor(string executable = "tmux") : ITmuxCommandExecutor, ICancellableTmuxCommandExecutor
{
    public TmuxCommandResult Execute(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new TmuxUnavailableException("Unable to start the tmux executable.");
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new TmuxCommandResult(process.ExitCode, standardOutput, standardError);
        }
        catch (Win32Exception exception)
        {
            throw new TmuxUnavailableException($"The tmux executable is unavailable: {exception.Message}");
        }
    }

    public async Task<TmuxCommandResult> ExecuteAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new TmuxUnavailableException("Unable to start the tmux executable.");
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(standardOutput, standardError);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await WaitForExitAfterCancellationAsync(process);
                throw;
            }

            return new TmuxCommandResult(process.ExitCode, await standardOutput, await standardError);
        }
        catch (Win32Exception exception)
        {
            throw new TmuxUnavailableException($"The tmux executable is unavailable: {exception.Message}");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task WaitForExitAfterCancellationAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
        }
    }
}

public sealed class SystemNativeProcessInspector : INativeProcessInspector
{
    private static readonly Regex ProcessFields = new(@"^\s*(?<pid>[0-9]+)\s+(?<ppid>[0-9]+)\s+(?<comm>\S+)\s+(?<command>.*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ProcessObservation Inspect(int processId, string expectedExecutable)
    {
        var processes = ReadProcesses();
        if (processes.Count == 0)
        {
            return new ProcessObservation(false, false);
        }

        var byParent = processes.ToLookup(process => process.ParentProcessId);
        var queue = new Queue<int>([processId]);
        var visited = new HashSet<int>();
        var paneProcessFound = false;
        while (queue.Count > 0)
        {
            var currentProcessId = queue.Dequeue();
            if (!visited.Add(currentProcessId))
            {
                continue;
            }

            var process = processes.FirstOrDefault(item => item.ProcessId == currentProcessId);
            if (process is not null)
            {
                paneProcessFound = true;
                if (IsExpected(process, expectedExecutable))
                {
                    return new ProcessObservation(true, true);
                }
            }

            foreach (var child in byParent[currentProcessId])
            {
                queue.Enqueue(child.ProcessId);
            }
        }

        return new ProcessObservation(paneProcessFound, false);
    }

    private static IReadOnlyList<NativeProcess> ReadProcesses()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ps",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-axo");
        startInfo.ArgumentList.Add("pid=,ppid=,comm=,command=");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return [];
            }

            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => ProcessFields.Match(line))
                .Where(match => match.Success
                    && int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                    && int.TryParse(match.Groups["ppid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                .Select(match => new NativeProcess(
                    int.Parse(match.Groups["pid"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["ppid"].Value, CultureInfo.InvariantCulture),
                    match.Groups["comm"].Value,
                    match.Groups["command"].Value))
                .ToArray();
        }
        catch (Win32Exception)
        {
            return [];
        }
    }

    private static bool IsExpected(NativeProcess process, string expectedExecutable)
    {
        return IsExpectedExecutable(process.CommandName, process.CommandLine, expectedExecutable);
    }

    public static bool IsExpectedExecutable(string commandName, string commandLine, string expectedExecutable)
    {
        var expectedName = Path.GetFileName(expectedExecutable);
        var commandLineExecutable = commandLine.TrimStart().Split([' ', '\t'], 2)[0].Trim('"', '\'');
        return new[] { commandName, commandLineExecutable }
            .Select(Path.GetFileName)
            .Any(candidate => string.Equals(candidate, expectedName, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record NativeProcess(int ProcessId, int ParentProcessId, string CommandName, string CommandLine);
}

public sealed class TmuxSessionManager
{
    private readonly string prefix;
    private readonly ITmuxCommandExecutor executor;
    private readonly INativeProcessInspector processInspector;

    public TmuxSessionManager(
        string prefix,
        ITmuxCommandExecutor? executor = null,
        INativeProcessInspector? processInspector = null)
    {
        this.prefix = prefix;
        this.executor = executor ?? new ProcessTmuxCommandExecutor();
        this.processInspector = processInspector ?? new SystemNativeProcessInspector();
        if (prefix.Length == 0 || prefix.Any(char.IsWhiteSpace) || prefix.Any(character => character is '/' or '\\' or ':'))
        {
            throw new AgentConfigurationException("The tmux prefix is not session-safe.");
        }
    }

    public bool HasSession(string name)
    {
        ValidateSessionName(name);
        return executor.Execute(["has-session", "-t", name]).ExitCode == 0;
    }

    public void EnsureSession(string name, string workingDirectory)
    {
        ValidateSessionName(name);
        ValidateWorkingDirectory(workingDirectory);
        if (HasSession(name))
        {
            return;
        }

        var result = executor.Execute(["new-session", "-d", "-s", name, "-c", workingDirectory, "/bin/sh"]);
        EnsureSuccess(result, "Unable to create the tmux session.");
    }

    public void LaunchProcess(string name, string workingDirectory, string executable, IReadOnlyList<string> arguments)
    {
        ValidateSessionName(name);
        ValidateWorkingDirectory(workingDirectory);
        ValidateCommandPart(executable);
        foreach (var argument in arguments)
        {
            ValidateCommandPart(argument);
        }

        if (!HasSession(name))
        {
            throw new TmuxOperationException("agent_session_missing", "The tmux session does not exist.");
        }

        var command = new List<string> { "respawn-pane", "-k", "-t", $"{name}:0.0", "-c", workingDirectory, "--", executable };
        command.AddRange(arguments);
        var result = executor.Execute(command);
        EnsureSuccess(result, "Unable to launch the native runtime in tmux.");
    }

    public TmuxPaneSnapshot CapturePane(string name, int scrollbackLines)
    {
        ValidateSessionName(name);
        if (scrollbackLines is < 1 or > 100000)
        {
            throw new AgentConfigurationException("The terminal scrollback bound is outside the supported range.");
        }

        var result = executor.Execute([
            "capture-pane",
            "-p",
            "-J",
            "-t",
            $"{name}:0.0",
            "-S",
            $"-{scrollbackLines.ToString(CultureInfo.InvariantCulture)}"
        ]);
        EnsureSuccess(result, "Unable to capture the tmux pane.");
        return new TmuxPaneSnapshot(result.StandardOutput, scrollbackLines);
    }

    public void StartPanePipe(string name, string sinkPath)
    {
        ValidateSessionName(name);
        var normalizedSinkPath = ValidateSinkPath(sinkPath);
        var result = executor.Execute([
            "pipe-pane",
            "-t",
            $"{name}:0.0",
            TmuxPipeCommandBuilder.Build(normalizedSinkPath)
        ]);
        EnsureSuccess(result, "Unable to start the tmux output pipe.");
    }

    public void StopPanePipe(string name)
    {
        ValidateSessionName(name);
        var result = executor.Execute(["pipe-pane", "-t", $"{name}:0.0"]);
        EnsureSuccess(result, "Unable to stop the tmux output pipe.");
    }

    public void SendLiteralInput(string name, string data)
    {
        ValidateSessionName(name);
        ValidateLiteralInput(data);
        if (!HasSession(name))
        {
            throw new TmuxOperationException("agent_session_missing", "The tmux session does not exist.");
        }

        var result = executor.Execute(["send-keys", "-t", $"{name}:0.0", "-l", "--", data]);
        EnsureSuccess(result, "Unable to deliver literal input to the tmux pane.");
    }

    public async Task SendLiteralInputAsync(string name, string data, CancellationToken cancellationToken = default)
    {
        ValidateSessionName(name);
        ValidateLiteralInput(data);
        var session = await ExecuteAsync(["has-session", "-t", name], cancellationToken);
        if (session.ExitCode != 0)
        {
            throw new TmuxOperationException("agent_session_missing", "The tmux session does not exist.");
        }

        var result = await ExecuteAsync(["send-keys", "-t", $"{name}:0.0", "-l", "--", data], cancellationToken);
        EnsureSuccess(result, "Unable to deliver literal input to the tmux pane.");
    }

    public void ResizePane(string name, int columns, int rows)
    {
        ValidateSessionName(name);
        ValidateDimensions(columns, rows);
        if (!HasSession(name))
        {
            throw new TmuxOperationException("agent_session_missing", "The tmux session does not exist.");
        }

        var result = executor.Execute([
            "resize-pane",
            "-t",
            $"{name}:0.0",
            "-x",
            columns.ToString(CultureInfo.InvariantCulture),
            "-y",
            rows.ToString(CultureInfo.InvariantCulture)
        ]);
        EnsureSuccess(result, "Unable to resize the tmux pane.");
    }

    public async Task ResizePaneAsync(string name, int columns, int rows, CancellationToken cancellationToken = default)
    {
        ValidateSessionName(name);
        ValidateDimensions(columns, rows);
        var session = await ExecuteAsync(["has-session", "-t", name], cancellationToken);
        if (session.ExitCode != 0)
        {
            throw new TmuxOperationException("agent_session_missing", "The tmux session does not exist.");
        }

        var result = await ExecuteAsync([
            "resize-pane",
            "-t",
            $"{name}:0.0",
            "-x",
            columns.ToString(CultureInfo.InvariantCulture),
            "-y",
            rows.ToString(CultureInfo.InvariantCulture)
        ], cancellationToken);
        EnsureSuccess(result, "Unable to resize the tmux pane.");
    }

    public void StopSession(string name)
    {
        ValidateSessionName(name);
        if (!HasSession(name))
        {
            return;
        }

        var result = executor.Execute(["kill-session", "-t", name]);
        EnsureSuccess(result, "Unable to stop the tmux session.");
    }

    public TmuxHealth GetHealth(string name, string expectedRuntime)
    {
        ValidateSessionName(name);
        try
        {
            if (!HasSession(name))
            {
                return new TmuxHealth(false, false, SessionObservedState.Missing, null, true);
            }

            var result = executor.Execute([
                "list-panes",
                "-t",
                $"{name}:0.0",
                "-F",
                "#{pane_pid}\t#{pane_dead}\t#{pane_start_command}\t#{pane_current_command}"
            ]);
            if (result.ExitCode != 0)
            {
                return new TmuxHealth(true, false, SessionObservedState.Error, "Unable to inspect the tmux pane.");
            }

            var line = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (line is null)
            {
                return new TmuxHealth(true, false, SessionObservedState.Exited, "The tmux pane is missing.", true);
            }

            var fields = line.Split('\t', 4);
            if (fields.Length < 4
                || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var panePid)
                || panePid <= 0
                || !TryParsePaneDead(fields[1], out var paneDead))
            {
                return new TmuxHealth(true, false, SessionObservedState.Error, "The tmux pane returned an invalid process id.");
            }

            var paneStartCommand = fields[2].Trim();
            var currentCommand = fields[3].Trim();
            if (paneDead)
            {
                return new TmuxHealth(true, false, SessionObservedState.Exited, "The tmux pane is marked dead.", true);
            }

            if (string.IsNullOrWhiteSpace(paneStartCommand))
            {
                return new TmuxHealth(true, false, SessionObservedState.Error, "The live tmux pane owner could not be verified.");
            }

            var paneOwnerIsExpected = IsExpectedCommand(paneStartCommand, expectedRuntime);
            if (paneOwnerIsExpected)
            {
                // Process-title inspection is supplemental. Claude Code may deliberately change
                // its OS-level process title after launch, so a mismatch cannot invalidate tmux
                // provenance recorded in pane_start_command.
                _ = processInspector.Inspect(panePid, expectedRuntime);
                return new TmuxHealth(true, true, SessionObservedState.Running, null);
            }

            return new TmuxHealth(true, false, SessionObservedState.Exited, $"The pane was started by an unexpected command ({currentCommand}).", true);
        }
        catch (TmuxUnavailableException exception)
        {
            return new TmuxHealth(false, false, SessionObservedState.Error, exception.Message);
        }
    }

    public IReadOnlyList<string> ListManagedSessions()
    {
        var result = executor.Execute(["list-sessions", "-F", "#{session_name}"]);
        EnsureSuccess(result, "Unable to list tmux sessions.");
        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(session => session.Trim())
            .Where(session => session.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();
    }

    private static void ValidateWorkingDirectory(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            throw new AgentConfigurationException("The tmux working directory must exist.");
        }
    }

    private static void ValidateCommandPart(string value)
    {
        if (value is null || value.Contains('\0') || value.Contains('\r') || value.Contains('\n'))
        {
            throw new AgentConfigurationException("A tmux command argument contains an invalid control character.");
        }
    }

    private static string ValidateSinkPath(string sinkPath)
    {
        if (!Path.IsPathRooted(sinkPath) || sinkPath.Contains('\0') || sinkPath.Contains('\r') || sinkPath.Contains('\n'))
        {
            throw new AgentConfigurationException("The tmux output sink must be an absolute safe path.");
        }

        return Path.GetFullPath(sinkPath);
    }

    private static void ValidateLiteralInput(string data)
    {
        if (string.IsNullOrEmpty(data) || data.Contains('\0'))
        {
            throw new AgentConfigurationException("Literal terminal input must be non-empty and must not contain a NUL character.");
        }

        if (Encoding.UTF8.GetByteCount(data) > TerminalProtocol.MaxPayloadBytes)
        {
            throw new AgentConfigurationException("Literal terminal input exceeds the supported payload limit.");
        }
    }

    private static void ValidateDimensions(int columns, int rows)
    {
        if (columns is < 1 or > TerminalProtocol.MaxColumns || rows is < 1 or > TerminalProtocol.MaxRows)
        {
            throw new AgentConfigurationException("Terminal dimensions are outside the supported bounds.");
        }
    }

    private async Task<TmuxCommandResult> ExecuteAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (executor is ICancellableTmuxCommandExecutor cancellable)
        {
            return await cancellable.ExecuteAsync(arguments, cancellationToken);
        }

        return await Task.Run(() => executor.Execute(arguments), cancellationToken);
    }

    private void ValidateSessionName(string name)
    {
        AgentRegistry.ValidateSessionName(name);
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new AgentConfigurationException("The tmux session is outside the configured harness prefix.");
        }

        AgentRegistry.ValidateIdentity(name[prefix.Length..]);
    }

    private static bool IsExpectedCommand(string command, string expectedExecutable)
    {
        return SystemNativeProcessInspector.IsExpectedExecutable(string.Empty, command, expectedExecutable);
    }

    private static bool TryParsePaneDead(string value, out bool paneDead)
    {
        switch (value.Trim())
        {
            case "0":
                paneDead = false;
                return true;
            case "1":
                paneDead = true;
                return true;
            default:
                paneDead = false;
                return false;
        }
    }

    private static void EnsureSuccess(TmuxCommandResult result, string message)
    {
        if (result.ExitCode != 0)
        {
            throw new TmuxOperationException("agent_runtime_unavailable", message);
        }
    }
}

public static class TmuxPipeCommandBuilder
{
    public static string Build(string sinkPath)
    {
        if (!Path.IsPathRooted(sinkPath) || sinkPath.Contains('\0') || sinkPath.Contains('\r') || sinkPath.Contains('\n'))
        {
            throw new AgentConfigurationException("The tmux output sink must be an absolute safe path.");
        }

        var quotedPath = sinkPath.Replace("'", "'\\''", StringComparison.Ordinal);
        return $"exec /usr/bin/tee -a '{quotedPath}'";
    }
}

public sealed class TmuxUnavailableException(string message) : Exception(message);

public sealed class TmuxOperationException(string code, string message) : AgentLifecycleException(code, message);
