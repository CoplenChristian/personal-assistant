using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Runtime;
using PersonalAssistant.Harness.Settings;

namespace PersonalAssistant.Server.Endpoints;

public static class TerminalEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapTerminalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.Map("/ws/agents/personal/terminal", HandlePersonalTerminalAsync);
        return endpoints;
    }

    private static async Task HandlePersonalTerminalAsync(HttpContext context)
    {
        if (!HasSameOrigin(context))
        {
            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "terminal_origin_rejected", "The terminal origin is not allowed.");
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "terminal_websocket_required", "The terminal route requires a WebSocket connection.");
            return;
        }

        var agents = context.RequestServices.GetRequiredService<IAgentSessionService>();
        AgentStatus status;
        try
        {
            status = agents.GetPersonal();
        }
        catch (AgentConfigurationException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable, "agent_configuration_invalid", exception.Message);
            return;
        }
        catch (AgentLifecycleException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable, exception.Code, exception.Message);
            return;
        }

        if (!status.RuntimeHealthy)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status409Conflict,
                "terminal_session_unavailable",
                "The personal Claude session is not healthy enough to observe.");
            return;
        }

        var terminalStream = context.RequestServices.GetRequiredService<TmuxTerminalStream>();
        var settings = context.RequestServices.GetRequiredService<SettingsService>();
        var scrollbackLines = ReadScrollbackLines(settings);
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var lease = terminalStream.Subscribe(status.Session.TmuxSessionName);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        using var sendLock = new SemaphoreSlim(1, 1);

        try
        {
            var snapshot = terminalStream.Capture(status.Session.TmuxSessionName, scrollbackLines);
            await SendJsonAsync(socket, new TerminalHelloFrame(TerminalProtocol.ContractVersion, status.Definition.Id), sendLock, cancellation.Token);
            await SendJsonAsync(socket, CreateBoundedSnapshotFrame(snapshot), sendLock, cancellation.Token);

            var sendTask = SendOutputAsync(socket, lease.Output, sendLock, cancellation.Token);
            var receiveTask = ReceiveClientFramesAsync(socket, sendLock, cancellation.Token);
            var completedTask = await Task.WhenAny(sendTask, receiveTask);
            var streamFailure = GetTerminalStreamFailure(completedTask);
            cancellation.Cancel();
            if (streamFailure is not null)
            {
                await SendTerminalErrorAndCloseAsync(socket, streamFailure.Code, streamFailure.Message, sendLock);
            }

            await AwaitQuietlyAsync(sendTask);
            await AwaitQuietlyAsync(receiveTask);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Request cancellation and normal browser close are expected observer lifecycle events.
        }
        catch (TerminalStreamException)
        {
            // The subscription has already been completed with a stable local stream error.
        }
        catch (TerminalProtocolException exception)
        {
            await SendTerminalErrorAndCloseAsync(socket, exception.Code, exception.Message, sendLock);
        }
        catch (WebSocketException)
        {
            // The browser may close without a complete close handshake.
        }
        finally
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "terminal observer closed", CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    // The peer has already gone away.
                }
            }
        }
    }

    private static async Task SendOutputAsync(
        WebSocket socket,
        TerminalOutputSubscription subscription,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in subscription.Reader.ReadAllAsync(cancellationToken))
            {
                await SendJsonAsync(socket, new TerminalOutputFrame(message.Sequence, message.Data), sendLock, cancellationToken);
            }
        }
        catch (TerminalProtocolException exception)
        {
            throw new TerminalStreamException(exception.Code, exception.Message);
        }
    }

    private static async Task ReceiveClientFramesAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult receive;
                do
                {
                    receive = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (receive.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    if (receive.MessageType != WebSocketMessageType.Text)
                    {
                        await SendJsonAsync(socket, new TerminalErrorFrame("text_frame_required"), sendLock, cancellationToken);
                        break;
                    }

                    if (message.Length + receive.Count > TerminalProtocol.MaxPayloadBytes)
                    {
                        await SendJsonAsync(socket, new TerminalErrorFrame("payload_too_large"), sendLock, cancellationToken);
                        return;
                    }

                    await message.WriteAsync(buffer.AsMemory(0, receive.Count), cancellationToken);
                }
                while (!receive.EndOfMessage);

                if (receive.MessageType != WebSocketMessageType.Text || message.Length == 0)
                {
                    continue;
                }

                try
                {
                    var frame = TerminalProtocolValidator.ParseClientFrame(Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length)));
                    if (frame is TerminalPingFrame ping)
                    {
                        await SendJsonAsync(socket, new TerminalPongFrame(ping.Sequence), sendLock, cancellationToken);
                    }
                    else
                    {
                        await SendJsonAsync(socket, new TerminalErrorFrame("terminal_input_not_ready", "Terminal input is enabled by the next task."), sendLock, cancellationToken);
                    }
                }
                catch (TerminalProtocolException exception)
                {
                    await SendJsonAsync(socket, new TerminalErrorFrame(exception.Code), sendLock, cancellationToken);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task SendJsonAsync(
        WebSocket socket,
        object frame,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(frame, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > TerminalProtocol.MaxPayloadBytes)
        {
            throw new TerminalProtocolException("payload_too_large", "The terminal frame exceeds the configured limit.");
        }

        await sendLock.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static TerminalSnapshotFrame CreateBoundedSnapshotFrame(TmuxPaneSnapshot snapshot)
    {
        if (SerializedSnapshotBytes(snapshot.Data, snapshot.ScrollbackLines) <= TerminalProtocol.MaxPayloadBytes)
        {
            return new TerminalSnapshotFrame(0, snapshot.Data, snapshot.ScrollbackLines);
        }

        var encoded = Encoding.UTF8.GetBytes(snapshot.Data);
        var start = Math.Max(0, encoded.Length - TerminalProtocol.MaxPayloadBytes);
        while (start < encoded.Length)
        {
            while (start < encoded.Length && (encoded[start] & 0xC0) == 0x80)
            {
                start++;
            }

            var data = Encoding.UTF8.GetString(encoded, start, encoded.Length - start);
            var firstLineBreak = data.IndexOf('\n');
            if (firstLineBreak >= 0 && firstLineBreak < data.Length - 1)
            {
                data = data[(firstLineBreak + 1)..];
            }

            var lineCount = data.Count(character => character == '\n');
            var retainedLines = Math.Clamp(lineCount == 0 && data.Length > 0 ? 1 : lineCount, 0, snapshot.ScrollbackLines);
            if (SerializedSnapshotBytes(data, retainedLines) <= TerminalProtocol.MaxPayloadBytes)
            {
                return new TerminalSnapshotFrame(0, data, retainedLines);
            }

            var remaining = encoded.Length - start;
            start += Math.Max(1, remaining / 10);
        }

        return new TerminalSnapshotFrame(0, string.Empty, 0);
    }

    private static int SerializedSnapshotBytes(string data, int scrollbackLines) =>
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new TerminalSnapshotFrame(0, data, scrollbackLines), JsonOptions));

    private static TerminalStreamException? GetTerminalStreamFailure(Task task) =>
        task.IsFaulted ? task.Exception?.GetBaseException() as TerminalStreamException : null;

    private static async Task SendTerminalErrorAndCloseAsync(
        WebSocket socket,
        string code,
        string detail,
        SemaphoreSlim sendLock)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            await SendJsonAsync(socket, new TerminalErrorFrame(code, detail), sendLock, CancellationToken.None);
        }
        catch (WebSocketException)
        {
            return;
        }
        catch (TerminalProtocolException)
        {
            return;
        }

        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, code, CancellationToken.None);
        }
        catch (WebSocketException)
        {
            // The browser may close as soon as the error frame arrives.
        }
    }

    private static int ReadScrollbackLines(SettingsService settings)
    {
        var setting = settings.GetSnapshot().Settings.Single(item => item.Key == "appearance.browserScrollbackLines");
        return setting.Value is long value
            ? (int)Math.Clamp(value, 100, 100000)
            : 5000;
    }

    private static bool HasSameOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        var host = context.Request.Host;
        var defaultPort = string.Equals(context.Request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
        var requestPort = host.Port ?? defaultPort;
        var originPort = originUri.IsDefaultPort ? defaultPort : originUri.Port;
        return string.Equals(originUri.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(originUri.Host, host.Host, StringComparison.OrdinalIgnoreCase)
            && originPort == requestPort;
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = "Terminal request rejected",
            Detail = detail,
            Type = $"https://localhost/problems/{code}"
        };
        problem.Extensions["code"] = code;
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }

    private static async Task AwaitQuietlyAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (TerminalStreamException)
        {
        }
        catch (WebSocketException)
        {
        }
    }
}
