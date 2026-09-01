using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Runtime;
using System.Net.Http.Json;
using Xunit;

namespace PersonalAssistant.Server.Tests;

public sealed class TerminalApiTests
{
    [Fact]
    public async Task Terminal_route_requires_a_websocket_and_returns_problem_details()
    {
        using var factory = new SettingsApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ws/agents/personal/terminal");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("terminal_websocket_required", body.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Websocket_sends_hello_snapshot_then_live_output()
    {
        using var factory = new TerminalApiFactory();
        var socketClient = factory.Server.CreateWebSocketClient();
        using var socket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws/agents/personal/terminal"), CancellationToken.None);

        using var hello = await ReceiveJsonAsync(socket);
        using var snapshot = await ReceiveJsonAsync(socket);
        Assert.Equal("hello", hello.RootElement.GetProperty("type").GetString());
        Assert.Equal("phase-0c-terminal.v1", hello.RootElement.GetProperty("protocol").GetString());
        Assert.Equal("personal", hello.RootElement.GetProperty("agentId").GetString());
        Assert.Equal("snapshot", snapshot.RootElement.GetProperty("type").GetString());
        Assert.True(snapshot.RootElement.GetProperty("hydrationBoundary").GetBoolean());
        Assert.Equal("fixture snapshot\r\n", snapshot.RootElement.GetProperty("data").GetString());

        File.AppendAllText(factory.Executor.SinkPath, "fixture output\r\n");
        using var output = await ReceiveJsonAsync(socket);
        Assert.Equal("output", output.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, output.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal("fixture output\r\n", output.RootElement.GetProperty("data").GetString());

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Websocket_reconnect_gets_a_fresh_snapshot_and_restarts_one_stream_bridge()
    {
        using var factory = new TerminalApiFactory();
        var socketClient = factory.Server.CreateWebSocketClient();
        using (var firstSocket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws/agents/personal/terminal"), CancellationToken.None))
        {
            using var firstHello = await ReceiveJsonAsync(firstSocket);
            using var firstSnapshot = await ReceiveJsonAsync(firstSocket);
            Assert.Equal("hello", firstHello.RootElement.GetProperty("type").GetString());
            Assert.Equal(0, firstSnapshot.RootElement.GetProperty("sequence").GetInt64());
            await firstSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", CancellationToken.None);
        }

        await WaitForAsync(() => factory.Executor.PipeStopCount == 1);
        using var secondSocket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws/agents/personal/terminal"), CancellationToken.None);
        using var secondHello = await ReceiveJsonAsync(secondSocket);
        using var secondSnapshot = await ReceiveJsonAsync(secondSocket);

        Assert.Equal("hello", secondHello.RootElement.GetProperty("type").GetString());
        Assert.Equal("snapshot", secondSnapshot.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, secondSnapshot.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal(2, factory.Executor.CaptureCount);
        Assert.Equal(2, factory.Executor.PipeStartCount);

        await secondSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Websocket_rejects_an_unhealthy_personal_session_before_acceptance()
    {
        using var factory = new TerminalApiFactory(healthy: false);
        var socketClient = factory.Server.CreateWebSocketClient();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            socketClient.ConnectAsync(new Uri("ws://localhost/ws/agents/personal/terminal"), CancellationToken.None));
        Assert.Contains("status code: 409", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, factory.Executor.PipeStartCount);
    }

    [Fact]
    public async Task Terminal_route_rejects_an_explicit_foreign_origin()
    {
        using var factory = new TerminalApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://evil.example");

        using var response = await client.GetAsync("/ws/agents/personal/terminal");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("terminal_origin_rejected", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Websocket_bounds_an_oversized_snapshot_frame()
    {
        using var factory = new TerminalApiFactory(captureOutput: new string('x', TerminalProtocol.MaxPayloadBytes * 2));
        var socketClient = factory.Server.CreateWebSocketClient();
        using var socket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws/agents/personal/terminal"), CancellationToken.None);

        using var hello = await ReceiveJsonAsync(socket);
        using var snapshot = await ReceiveJsonAsync(socket);
        var serializedSnapshotBytes = Encoding.UTF8.GetByteCount(snapshot.RootElement.GetRawText());

        Assert.Equal("snapshot", snapshot.RootElement.GetProperty("type").GetString());
        Assert.True(serializedSnapshotBytes <= TerminalProtocol.MaxPayloadBytes);
        Assert.True(snapshot.RootElement.GetProperty("data").GetString()!.Length < TerminalProtocol.MaxPayloadBytes * 2);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(WebSocket socket)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var message = new MemoryStream();
        var buffer = new byte[16 * 1024];
        WebSocketReceiveResult receive;
        do
        {
            receive = await socket.ReceiveAsync(buffer, cancellation.Token);
            Assert.Equal(WebSocketMessageType.Text, receive.MessageType);
            await message.WriteAsync(buffer.AsMemory(0, receive.Count), cancellation.Token);
        }
        while (!receive.EndOfMessage);

        return JsonDocument.Parse(message.ToArray());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(20, cancellation.Token);
        }
    }
}

public sealed class TerminalApiFactory : WebApplicationFactory<Program>
{
    private readonly string runtimeDirectory = Directory.CreateTempSubdirectory("personal-assistant-terminal-api-").FullName;
    private readonly FakeTerminalExecutor executor;
    private readonly TmuxTerminalStream terminalStream;
    private readonly bool healthy;

    public TerminalApiFactory(bool healthy = true, string? captureOutput = null)
    {
        this.healthy = healthy;
        executor = new FakeTerminalExecutor(runtimeDirectory, captureOutput);
        terminalStream = new TmuxTerminalStream(
            new TmuxSessionManager("test-pa-", executor, new NoopProcessInspector()),
            runtimeDirectory);
    }

    public FakeTerminalExecutor Executor => executor;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var repositoryRoot = FindRepositoryRoot();
        builder.UseEnvironment("Development");
        builder.UseSetting("PA_REPOSITORY_ROOT", repositoryRoot);
        builder.UseSetting("PA_RUNTIME_DIR", runtimeDirectory);
        builder.UseSetting("PA_SERVER_HOST", "127.0.0.1");
        builder.UseSetting("PA_SERVER_PORT", "4317");
        builder.UseSetting("PA_TMUX_PREFIX", "test-pa-");
        builder.UseSetting("PA_VAULT_DIR", Path.Combine(runtimeDirectory, "vault"));
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PA_REPOSITORY_ROOT"] = repositoryRoot,
            ["PA_RUNTIME_DIR"] = runtimeDirectory,
            ["PA_SERVER_HOST"] = "127.0.0.1",
            ["PA_SERVER_PORT"] = "4317",
            ["PA_TMUX_PREFIX"] = "test-pa-",
            ["PA_VAULT_DIR"] = Path.Combine(runtimeDirectory, "vault")
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAgentSessionService>();
            services.AddSingleton<IAgentSessionService>(_ => new FakeAgentSessionService(healthy));
            services.RemoveAll<TmuxTerminalStream>();
            services.AddSingleton(terminalStream);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(runtimeDirectory))
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "policies", "defaults", "runtime.yaml")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to find repository root for terminal API tests.");
    }

    public sealed class FakeTerminalExecutor(string runtimeDirectory, string? captureOutput = null) : ITmuxCommandExecutor
    {
        public string SinkPath { get; } = Path.Combine(runtimeDirectory, "terminal-streams", "test-pa-personal.log");
        private string CaptureOutput { get; } = captureOutput ?? "fixture snapshot\r\n";
        public int CaptureCount => Volatile.Read(ref captureCount);
        public int PipeStartCount => Volatile.Read(ref pipeStartCount);
        public int PipeStopCount => Volatile.Read(ref pipeStopCount);

        private int captureCount;
        private int pipeStartCount;
        private int pipeStopCount;

        public TmuxCommandResult Execute(IReadOnlyList<string> arguments)
        {
            if (arguments[0] == "capture-pane")
            {
                Interlocked.Increment(ref captureCount);
                return new TmuxCommandResult(0, CaptureOutput, string.Empty);
            }

            if (arguments[0] == "pipe-pane")
            {
                if (arguments.Count == 4)
                {
                    Interlocked.Increment(ref pipeStartCount);
                }
                else
                {
                    Interlocked.Increment(ref pipeStopCount);
                }
            }

            return new TmuxCommandResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class FakeAgentSessionService(bool healthy) : IAgentSessionService
    {
        private readonly AgentDefinition definition = new(
            "personal",
            "Personal",
            "claude",
            Directory.GetCurrentDirectory(),
            ["personal"],
            [],
            false,
            null,
            "personal",
            [],
            "test-pa-personal",
            "fixture");

        private readonly PersistedSession session = new(
            "fixture-session",
            "personal",
            "test-pa-personal",
            "claude",
            null,
            healthy ? SessionObservedState.Running : SessionObservedState.Missing,
            null,
            null,
            null,
            null);

        public AgentStatus GetPersonal() => Status();
        public AgentStatus ReconcilePersonal() => Status();
        public AgentStatus StartPersonal() => Status();
        public AgentStatus StopPersonal() => Status();
        public void RecordPersonalConversationReference(string reference) { }

        private AgentStatus Status() => new(
            definition,
            healthy ? AgentDesiredState.Running : AgentDesiredState.Stopped,
            session,
            healthy,
            healthy);
    }

    private sealed class NoopProcessInspector : INativeProcessInspector
    {
        public ProcessObservation Inspect(int processId, string expectedExecutable) => new(true, true);
    }
}
