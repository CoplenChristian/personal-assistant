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
            ["capture-pane", "-p", "-t", "test-pa-personal:0.0", "-S", "-5000"],
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
}
