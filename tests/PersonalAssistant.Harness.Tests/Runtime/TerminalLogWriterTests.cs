using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Runtime;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Runtime;

public sealed class TerminalLogWriterTests
{
    [Fact]
    public void Writes_to_ignored_active_path_and_emits_safe_warning_before_rotation()
    {
        var runtimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-terminal-log-").FullName;
        try
        {
            var events = new List<ActivityEvent>();
            using var writer = new TerminalLogWriter(
                runtimeDirectory,
                "personal",
                warningBytes: 5,
                rotationBytes: 10,
                retainedFiles: 2,
                new RecordingSink(events),
                "personal");

            var warning = writer.Append("12345");
            var rotation = writer.Append("67890");

            Assert.True(warning.WarningReached);
            Assert.True(rotation.Rotated);
            Assert.StartsWith(
                Path.Combine(runtimeDirectory, "agents", "personal", "terminal"),
                writer.ActiveLogPath,
                StringComparison.Ordinal);
            Assert.True(File.Exists(writer.ActiveLogPath));
            Assert.Equal(string.Empty, File.ReadAllText(writer.ActiveLogPath));
            Assert.Equal("1234567890", File.ReadAllText(writer.ActiveLogPath + ".1"));
            Assert.Equal(2, events.Count);
            Assert.Equal("terminal_log_warning", events[0].Operation);
            Assert.Equal("terminal_log_rotation", events[1].Operation);
            Assert.DoesNotContain(writer.ActiveLogPath, events[0].MetadataJson, StringComparison.Ordinal);
            Assert.DoesNotContain("1234567890", events[1].MetadataJson, StringComparison.Ordinal);
            Assert.DoesNotContain("10", events[1].MetadataJson, StringComparison.Ordinal);
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
    public void Retains_only_the_configured_number_of_rotated_files()
    {
        var runtimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-terminal-log-").FullName;
        try
        {
            using var writer = new TerminalLogWriter(runtimeDirectory, "personal", 5, 10, retainedFiles: 2);

            writer.Append("first-----");
            writer.Append("second----");
            writer.Append("third-----");

            Assert.Equal("third-----", File.ReadAllText(writer.ActiveLogPath + ".1"));
            Assert.Equal("second----", File.ReadAllText(writer.ActiveLogPath + ".2"));
            Assert.False(File.Exists(writer.ActiveLogPath + ".3"));
            Assert.Equal(string.Empty, File.ReadAllText(writer.ActiveLogPath));
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
    public void Rejects_unbounded_chunks_and_cleans_up_on_shutdown()
    {
        var runtimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-terminal-log-").FullName;
        try
        {
            var writer = new TerminalLogWriter(runtimeDirectory, "personal", 5, 10, retainedFiles: 2);
            Assert.Throws<TerminalLogException>(() => writer.Append(new string('x', TerminalLogWriter.MaxWriteBytes + 1)));
            writer.Dispose();
            Assert.Throws<ObjectDisposedException>(() => writer.Append("after shutdown"));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(runtimeDirectory, "*", SearchOption.AllDirectories),
                path => path.EndsWith("rotation.tmp", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(runtimeDirectory))
            {
                Directory.Delete(runtimeDirectory, recursive: true);
            }
        }
    }

    private sealed class RecordingSink(List<ActivityEvent> events) : IActivityEventSink
    {
        public void Append(ActivityEvent activityEvent) => events.Add(activityEvent);
    }
}
