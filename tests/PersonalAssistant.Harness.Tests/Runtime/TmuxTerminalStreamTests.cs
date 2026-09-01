using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Runtime;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Runtime;

public sealed class TmuxTerminalStreamTests
{
    [Fact]
    public void Capture_uses_bounded_pane_output_arguments()
    {
        var executor = new FakeTmuxExecutor { CaptureOutput = "backlog\n" };
        var manager = new TmuxSessionManager("test-pa-", executor, new NoopProcessInspector());

        var snapshot = manager.CapturePane("test-pa-personal", 5000);

        Assert.Equal("backlog\n", snapshot.Data);
        Assert.Equal(5000, snapshot.ScrollbackLines);
        Assert.Equal(
            ["capture-pane", "-p", "-J", "-t", "test-pa-personal:0.0", "-S", "-5000"],
            executor.Commands.Single());
    }

    [Fact]
    public void Stream_uses_one_pipe_until_the_final_observer_releases_it()
    {
        var runtimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-terminal-").FullName;
        try
        {
            var executor = new FakeTmuxExecutor();
            var manager = new TmuxSessionManager("test-pa-", executor, new NoopProcessInspector());
            using var stream = new TmuxTerminalStream(manager, runtimeDirectory);
            using var first = stream.Subscribe("test-pa-personal");
            using var second = stream.Subscribe("test-pa-personal");

            Assert.Equal(1, executor.Commands.Count(command => command[0] == "pipe-pane"));
            Assert.Contains("/usr/bin/tee", executor.Commands.Single(command => command[0] == "pipe-pane")[3], StringComparison.Ordinal);
            Assert.Contains("terminal-streams", first.SinkPath, StringComparison.Ordinal);

            first.Dispose();
            Assert.Equal(0, executor.Commands.Count(command => command[0] == "pipe-pane" && command.Count == 3));

            second.Dispose();
            Assert.Equal(1, executor.Commands.Count(command => command[0] == "pipe-pane" && command.Count == 3));
        }
        finally
        {
            if (Directory.Exists(runtimeDirectory))
            {
                Directory.Delete(runtimeDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Pipe_command_quotes_runtime_sink_without_accepting_newlines()
    {
        var command = TmuxPipeCommandBuilder.Build("/tmp/terminal path/it's.log");

        Assert.Equal("exec /usr/bin/tee -a '/tmp/terminal path/it'\\''s.log'", command);
        Assert.Throws<AgentConfigurationException>(() => TmuxPipeCommandBuilder.Build("/tmp/bad\npath"));
    }

    [Fact]
    public void Literal_input_uses_literal_argument_form_and_preserves_control_sequences()
    {
        var executor = new FakeTmuxExecutor();
        var manager = new TmuxSessionManager("test-pa-", executor, new NoopProcessInspector());
        const string data = "paste with spaces\n\u001b[31mred\u001b[0m";

        manager.SendLiteralInput("test-pa-personal", data);

        Assert.Equal(
            ["send-keys", "-t", "test-pa-personal:0.0", "-l", "--", data],
            executor.Commands.Last());
        Assert.DoesNotContain(executor.Commands.Last(), argument => argument.Contains("sh -c", StringComparison.Ordinal));
    }

    [Fact]
    public void Resize_uses_bounded_typed_dimensions()
    {
        var executor = new FakeTmuxExecutor();
        var manager = new TmuxSessionManager("test-pa-", executor, new NoopProcessInspector());

        manager.ResizePane("test-pa-personal", 120, 36);

        Assert.Equal(
            ["resize-pane", "-t", "test-pa-personal:0.0", "-x", "120", "-y", "36"],
            executor.Commands.Last());
        Assert.Throws<AgentConfigurationException>(() => manager.ResizePane("test-pa-personal", 0, 36));
        Assert.Throws<AgentConfigurationException>(() => manager.ResizePane("test-pa-personal", 120, TerminalProtocol.MaxRows + 1));
    }

    [Fact]
    public async Task Stream_publishes_data_appended_after_the_pipe_is_ready()
    {
        var runtimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-terminal-").FullName;
        try
        {
            var executor = new FakeTmuxExecutor();
            var manager = new TmuxSessionManager("test-pa-", executor, new NoopProcessInspector());
            using var stream = new TmuxTerminalStream(manager, runtimeDirectory);
            using var lease = stream.Subscribe("test-pa-personal");

            await File.AppendAllTextAsync(lease.SinkPath, "streamed after setup\n");

            Assert.True(await lease.Output.Reader.WaitToReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(lease.Output.Reader.TryRead(out var message));
            Assert.Equal("streamed after setup\n", message.Data);
            Assert.Equal(1, message.Sequence);
        }
        finally
        {
            if (Directory.Exists(runtimeDirectory))
            {
                Directory.Delete(runtimeDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Async_literal_input_propagates_cancellation_to_the_tmux_boundary()
    {
        var executor = new CancellableTmuxExecutor();
        var manager = new TmuxSessionManager("test-pa-", executor, new NoopProcessInspector());
        using var cancellation = new CancellationTokenSource();

        var input = manager.SendLiteralInputAsync("test-pa-personal", "cancel me", cancellation.Token);
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => input);
        await executor.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class FakeTmuxExecutor : ITmuxCommandExecutor
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];
        public string CaptureOutput { get; init; } = string.Empty;

        public TmuxCommandResult Execute(IReadOnlyList<string> arguments)
        {
            Commands.Add(arguments.ToArray());
            return arguments[0] == "capture-pane"
                ? new TmuxCommandResult(0, CaptureOutput, string.Empty)
                : new TmuxCommandResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class NoopProcessInspector : INativeProcessInspector
    {
        public ProcessObservation Inspect(int processId, string expectedExecutable) => new(true, true);
    }

    private sealed class CancellableTmuxExecutor : ITmuxCommandExecutor, ICancellableTmuxCommandExecutor
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TmuxCommandResult Execute(IReadOnlyList<string> arguments) =>
            new(0, string.Empty, string.Empty);

        public async Task<TmuxCommandResult> ExecuteAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            if (arguments[0] == "has-session")
            {
                return new TmuxCommandResult(0, string.Empty, string.Empty);
            }

            Started.SetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.SetResult(true);
                throw;
            }

            return new TmuxCommandResult(0, string.Empty, string.Empty);
        }
    }
}
