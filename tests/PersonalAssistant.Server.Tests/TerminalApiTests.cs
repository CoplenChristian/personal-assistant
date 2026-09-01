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
    public async Task Websocket_sends_hello_screen_then_a_coalesced_screen_update()
    {
        using var factory = new TerminalApiFactory();
        var socketClient = factory.Server.CreateWebSocketClient();
        using var socket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws/agents/personal/terminal"), CancellationToken.None);

        using var hello = await ReceiveJsonAsync(socket);
        using var screen = await ReceiveJsonAsync(socket);
        Assert.Equal("hello", hello.RootElement.GetProperty("type").GetString());
        Assert.Equal("phase-0c-terminal.standardized.v1", hello.RootElement.GetProperty("protocol").GetString());
        Assert.Equal("personal", hello.RootElement.GetProperty("agentId").GetString());
        Assert.Equal("screen", screen.RootElement.GetProperty("type").GetString());
        Assert.True(screen.RootElement.GetProperty("hydrationBoundary").GetBoolean());
        Assert.Equal("fixture snapshot", screen.RootElement.GetProperty("data").GetString());
        using var initialState = await ReceiveJsonAsync(socket);
        Assert.Equal("state", initialState.RootElement.GetProperty("type").GetString());
        Assert.Equal("idle", initialState.RootElement.GetProperty("state").GetString());

        File.AppendAllText(factory.Executor.SinkPath, "fixture output\r\n");
        using var update = await ReceiveJsonAsync(socket);
        Assert.Equal("screen", update.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, update.RootElement.GetProperty("sequence").GetInt64());
        Assert.False(update.RootElement.GetProperty("hydrationBoundary").GetBoolean());
        Assert.Equal("fixture snapshot", update.RootElement.GetProperty("data").GetString());

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
            using var firstScreen = await ReceiveJsonAsync(firstSocket);
            using var firstState = await ReceiveJsonAsync(firstSocket);
            Assert.Equal("hello", firstHello.RootElement.GetProperty("type").GetString());
            Assert.Equal("screen", firstScreen.RootElement.GetProperty("type").GetString());
            Assert.Equal(0, firstScreen.RootElement.GetProperty("sequence").GetInt64());
            Assert.Equal("idle", firstState.RootElement.GetProperty("state").GetString());
            await firstSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", CancellationToken.None);
        }

        await WaitForAsync(() => factory.Executor.PipeStopCount == 1);
        using var secondSocket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws/agents/personal/terminal"), CancellationToken.None);
        using var secondHello = await ReceiveJsonAsync(secondSocket);
        using var secondScreen = await ReceiveJsonAsync(secondSocket);
        using var secondState = await ReceiveJsonAsync(secondSocket);

        Assert.Equal("hello", secondHello.RootElement.GetProperty("type").GetString());
        Assert.Equal("screen", secondScreen.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, secondScreen.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal("idle", secondState.RootElement.GetProperty("state").GetString());
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
    public async Task Websocket_bounds_an_oversized_screen_frame()
    {
        using var factory = new TerminalApiFactory(captureOutput: new string('x', TerminalProtocol.MaxPayloadBytes * 2));
        var socketClient = factory.Server.CreateWebSocketClient();
        using var socket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws/agents/personal/terminal"), CancellationToken.None);

        using var hello = await ReceiveJsonAsync(socket);
        using var screen = await ReceiveJsonAsync(socket);
        var serializedScreenBytes = Encoding.UTF8.GetByteCount(screen.RootElement.GetRawText());

        Assert.Equal("screen", screen.RootElement.GetProperty("type").GetString());
        Assert.True(serializedScreenBytes <= TerminalProtocol.MaxPayloadBytes);
        Assert.True(screen.RootElement.GetProperty("data").GetString()!.Length < TerminalProtocol.MaxPayloadBytes * 2);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Websocket_delivers_input_ack_and_state_transitions_without_echoing_input()
    {
        using var factory = new TerminalApiFactory();
        var socketClient = factory.Server.CreateWebSocketClient();
        using var socket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws/agents/personal/terminal"), CancellationToken.None);
        await ReceiveInitialFramesAsync(socket);

        var input = JsonSerializer.Serialize(new { type = "input", sequence = 7, data = "echo private text\r\n" });
        await socket.SendAsync(Encoding.UTF8.GetBytes(input), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        var responseTypes = new List<string>();
        var responseStates = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            using var response = await ReceiveJsonAsync(socket);
            responseTypes.Add(response.RootElement.GetProperty("type").GetString()!);
            if (response.RootElement.GetProperty("type").GetString() == "state")
            {
                responseStates.Add(response.RootElement.GetProperty("state").GetString()!);
            }

            Assert.DoesNotContain("echo private text", response.RootElement.GetRawText(), StringComparison.Ordinal);
        }

        Assert.Contains("inputAck", responseTypes);
        Assert.Contains("busy", responseStates);
        Assert.Contains("idle", responseStates);
        Assert.Single(factory.DeliveredInputs);
        Assert.Equal("echo private text\r\n", factory.DeliveredInputs[0].Data);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Websocket_resizes_the_healthy_pane_with_typed_dimensions()
    {
        using var factory = new TerminalApiFactory();
        var socketClient = factory.Server.CreateWebSocketClient();
        using var socket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws/agents/personal/terminal"), CancellationToken.None);
        await ReceiveInitialFramesAsync(socket);

        var resize = JsonSerializer.Serialize(new { type = "resize", columns = 120, rows = 36 });
        await socket.SendAsync(Encoding.UTF8.GetBytes(resize), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        await WaitForAsync(() => factory.Executor.Commands.Any(command => command[0] == "resize-pane"));

        var resizeCommand = factory.Executor.Commands.Last(command => command[0] == "resize-pane");
        Assert.Equal(["resize-pane", "-t", "test-pa-personal:0.0", "-x", "120", "-y", "36"], resizeCommand);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Websocket_reports_resize_unavailability_as_a_stable_error_and_state()
    {
        using var factory = new TerminalApiFactory(resizeUnavailable: true);
        var socketClient = factory.Server.CreateWebSocketClient();
        using var socket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws/agents/personal/terminal"), CancellationToken.None);
        await ReceiveInitialFramesAsync(socket);

        var resize = JsonSerializer.Serialize(new { type = "resize", columns = 120, rows = 36 });
        await socket.SendAsync(Encoding.UTF8.GetBytes(resize), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        var responseTypes = new List<string>();
        var responseCodes = new List<string>();
        for (var index = 0; index < 2; index++)
        {
            using var response = await ReceiveJsonAsync(socket);
            responseTypes.Add(response.RootElement.GetProperty("type").GetString()!);
            if (response.RootElement.GetProperty("type").GetString() == "error")
            {
                responseCodes.Add(response.RootElement.GetProperty("code").GetString()!);
            }
        }

        Assert.Contains("state", responseTypes);
        Assert.Contains("error", responseTypes);
        Assert.Contains("terminal_resize_unavailable", responseCodes);
        Assert.Equal(TerminalActivityState.Error.ToString().ToLowerInvariant(), factory.TerminalState.Current.ToWireValue());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Websocket_reconnect_after_protocol_error_starts_healthy_and_idle()
    {
        using var factory = new TerminalApiFactory();
        var socketClient = factory.Server.CreateWebSocketClient();
        using (var firstSocket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws/agents/personal/terminal"), CancellationToken.None))
        {
            await ReceiveInitialFramesAsync(firstSocket);
            var invalidInput = JsonSerializer.Serialize(new { type = "input", sequence = -1, data = "invalid" });
            await firstSocket.SendAsync(Encoding.UTF8.GetBytes(invalidInput), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            var observed = new List<string>();
            for (var index = 0; index < 2; index++)
            {
                using var response = await ReceiveJsonAsync(firstSocket);
                observed.Add(response.RootElement.GetProperty("type").GetString()!);
            }

            Assert.Contains("state", observed);
            Assert.Contains("error", observed);
            await firstSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reset state", CancellationToken.None);
        }

        await WaitForAsync(() => factory.Executor.PipeStopCount == 1);
        using var secondSocket = await socketClient.ConnectAsync(new Uri("ws://localhost/ws/agents/personal/terminal"), CancellationToken.None);
        await ReceiveInitialFramesAsync(secondSocket);
        Assert.Equal("idle", factory.TerminalState.Current.ToWireValue());
        await secondSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    private static async Task ReceiveInitialFramesAsync(WebSocket socket)
    {
        using var hello = await ReceiveJsonAsync(socket);
        using var snapshot = await ReceiveJsonAsync(socket);
        using var state = await ReceiveJsonAsync(socket);
        Assert.Equal("hello", hello.RootElement.GetProperty("type").GetString());
        Assert.Equal("screen", snapshot.RootElement.GetProperty("type").GetString());
        Assert.Equal("idle", state.RootElement.GetProperty("state").GetString());
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
    private readonly TmuxSessionManager tmux;
    private readonly TmuxTerminalStream terminalStream;
    private readonly TerminalInputSerializer terminalInput;
    private readonly TerminalActivityStateTracker terminalState;
    private readonly bool healthy;

    public List<TerminalInputRequest> DeliveredInputs { get; } = [];

    public TerminalApiFactory(bool healthy = true, string? captureOutput = null, bool resizeUnavailable = false)
    {
        this.healthy = healthy;
        executor = new FakeTerminalExecutor(runtimeDirectory, captureOutput, resizeUnavailable);
        tmux = new TmuxSessionManager("test-pa-", executor, new NoopProcessInspector());
        terminalStream = new TmuxTerminalStream(tmux, runtimeDirectory);
        terminalInput = new TerminalInputSerializer(
            "personal",
            (request, _) =>
            {
                DeliveredInputs.Add(request);
                return Task.CompletedTask;
            });
        terminalState = new TerminalActivityStateTracker("personal");
    }

    public FakeTerminalExecutor Executor => executor;
    public TerminalActivityStateTracker TerminalState => terminalState;

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
            services.RemoveAll<TmuxSessionManager>();
            services.AddSingleton(tmux);
            services.RemoveAll<TmuxTerminalStream>();
            services.AddSingleton(terminalStream);
            services.RemoveAll<TerminalInputSerializer>();
            services.AddSingleton(terminalInput);
            services.RemoveAll<TerminalActivityStateTracker>();
            services.AddSingleton(terminalState);
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

    public sealed class FakeTerminalExecutor(string runtimeDirectory, string? captureOutput = null, bool resizeUnavailable = false) : ITmuxCommandExecutor
    {
        public string SinkPath { get; } = Path.Combine(runtimeDirectory, "terminal-streams", "test-pa-personal.log");
        private string CaptureOutput { get; } = captureOutput ?? "fixture snapshot\r\n";
        private bool ResizeUnavailable { get; } = resizeUnavailable;
        public List<IReadOnlyList<string>> Commands { get; } = [];
        public int CaptureCount => Volatile.Read(ref captureCount);
        public int PipeStartCount => Volatile.Read(ref pipeStartCount);
        public int PipeStopCount => Volatile.Read(ref pipeStopCount);

        private int captureCount;
        private int pipeStartCount;
        private int pipeStopCount;

        public TmuxCommandResult Execute(IReadOnlyList<string> arguments)
        {
            Commands.Add(arguments.ToArray());
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

            if (arguments[0] == "resize-pane" && ResizeUnavailable)
            {
                throw new TmuxUnavailableException("test tmux unavailable");
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
