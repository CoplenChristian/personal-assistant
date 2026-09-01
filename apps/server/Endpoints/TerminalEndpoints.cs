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
        var terminalInput = context.RequestServices.GetRequiredService<TerminalInputSerializer>();
        var terminalState = context.RequestServices.GetRequiredService<TerminalActivityStateTracker>();
        var tmux = context.RequestServices.GetRequiredService<TmuxSessionManager>();
        var settings = context.RequestServices.GetRequiredService<SettingsService>();
        var scrollbackLines = ReadScrollbackLines(settings);
        terminalState.ResetForHealthySession(terminalInput.QueuedCount > 0 || terminalInput.HasInFlightOperation);

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var lease = terminalStream.Subscribe(status.Session.TmuxSessionName);
        using var stateSubscription = terminalState.Subscribe();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        using var sendLock = new SemaphoreSlim(1, 1);

        try
        {
            var snapshot = terminalStream.Capture(status.Session.TmuxSessionName, scrollbackLines);
            var screen = TerminalScreenNormalizer.Normalize(snapshot);
            await SendJsonAsync(socket, new TerminalHelloFrame(TerminalProtocol.ContractVersion, status.Definition.Id), sendLock, cancellation.Token);
            await SendJsonAsync(socket, new TerminalScreenFrame(0, screen.Data, screen.Columns, screen.Rows, HydrationBoundary: true), sendLock, cancellation.Token);
            var initialState = await stateSubscription.Reader.ReadAsync(cancellation.Token);
            await SendJsonAsync(socket, new TerminalStateFrame(initialState.ToWireValue()), sendLock, cancellation.Token);

            var sendTask = SendScreenUpdatesAsync(
                socket,
                lease.Output,
                terminalStream,
                status.Session.TmuxSessionName,
                scrollbackLines,
                sendLock,
                cancellation.Token);
            var stateTask = SendStateAsync(socket, stateSubscription, sendLock, cancellation.Token);
            var receiveTask = ReceiveClientFramesAsync(
                socket,
                sendLock,
                cancellation.Token,
                agents,
                tmux,
                terminalInput,
                terminalState);
            var completedTask = await Task.WhenAny(sendTask, stateTask, receiveTask);
            var streamFailure = GetTerminalStreamFailure(completedTask);
            cancellation.Cancel();
            if (streamFailure is not null)
            {
                terminalState.MarkError();
                await SendTerminalErrorAndCloseAsync(socket, streamFailure.Code, streamFailure.Message, sendLock);
            }

            await AwaitQuietlyAsync(sendTask);
            await AwaitQuietlyAsync(stateTask);
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
        catch (TerminalStateException exception)
        {
            terminalState.MarkError();
            await SendTerminalErrorAndCloseAsync(socket, exception.Code, exception.Message, sendLock);
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

    private static async Task SendScreenUpdatesAsync(
        WebSocket socket,
        TerminalOutputSubscription subscription,
        TmuxTerminalStream terminalStream,
        string sessionName,
        int scrollbackLines,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        var sequence = 0L;
        try
        {
            await foreach (var message in subscription.Reader.ReadAllAsync(cancellationToken))
            {
                _ = message;
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                while (subscription.Reader.TryRead(out _))
                {
                }

                var snapshot = terminalStream.Capture(sessionName, scrollbackLines);
                var screen = TerminalScreenNormalizer.Normalize(snapshot);
                await SendJsonAsync(
                    socket,
                    new TerminalScreenFrame(++sequence, screen.Data, screen.Columns, screen.Rows, HydrationBoundary: false),
                    sendLock,
                    cancellationToken);
            }
        }
        catch (TerminalProtocolException exception)
        {
            throw new TerminalStreamException(exception.Code, exception.Message);
        }
        catch (AgentLifecycleException exception)
        {
            throw new TerminalStreamException(exception.Code, exception.Message);
        }
        catch (TmuxUnavailableException exception)
        {
            throw new TerminalStreamException("terminal_screen_unavailable", exception.Message);
        }
    }

    private static async Task SendStateAsync(
        WebSocket socket,
        TerminalActivityStateSubscription subscription,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var state in subscription.Reader.ReadAllAsync(cancellationToken))
            {
                await SendJsonAsync(socket, new TerminalStateFrame(state.ToWireValue()), sendLock, cancellationToken);
            }
        }
        catch (TerminalStateException exception)
        {
            throw new TerminalStreamException(exception.Code, exception.Message);
        }
    }

    private static async Task ReceiveClientFramesAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken,
        IAgentSessionService agents,
        TmuxSessionManager tmux,
        TerminalInputSerializer terminalInput,
        TerminalActivityStateTracker terminalState)
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
                    else if (frame is TerminalInputFrame input)
                    {
                        await HandleInputAsync(socket, sendLock, cancellationToken, agents, terminalInput, terminalState, input);
                    }
                    else if (frame is TerminalResizeFrame resize)
                    {
                        await HandleResizeAsync(socket, sendLock, cancellationToken, agents, tmux, terminalState, resize);
                    }
                    else
                    {
                        await SendJsonAsync(socket, new TerminalErrorFrame("terminal_frame_not_ready"), sendLock, cancellationToken);
                    }
                }
                catch (TerminalProtocolException exception)
                {
                    terminalState.MarkError();
                    await SendJsonAsync(socket, new TerminalErrorFrame(exception.Code), sendLock, cancellationToken);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task HandleInputAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken,
        IAgentSessionService agents,
        TerminalInputSerializer terminalInput,
        TerminalActivityStateTracker terminalState,
        TerminalInputFrame input)
    {
        if (!TryGetHealthyAgent(agents, out var status, out var errorCode, out var errorDetail))
        {
            terminalState.MarkError();
            await SendJsonAsync(socket, new TerminalErrorFrame(errorCode, errorDetail), sendLock, cancellationToken);
            return;
        }

        terminalState.MarkBusy();
        try
        {
            var acknowledgement = await terminalInput.EnqueueAsync(input.Sequence, input.Data, cancellationToken);
            await SendJsonAsync(socket, new TerminalInputAcknowledgementFrame(acknowledgement.Sequence), sendLock, cancellationToken);
            if (terminalInput.QueuedCount == 0 && !terminalInput.HasInFlightOperation)
            {
                terminalState.MarkIdle();
            }
        }
        catch (TerminalInputException exception)
        {
            terminalState.MarkError();
            await SendJsonAsync(socket, new TerminalErrorFrame(exception.Code, exception.Message), sendLock, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var quiescent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void HandleQuiescence() => quiescent.TrySetResult(true);
            terminalInput.BecameQuiescent += HandleQuiescence;
            try
            {
                if (terminalInput.QueuedCount == 0 && !terminalInput.HasInFlightOperation)
                {
                    quiescent.TrySetResult(true);
                }

                await quiescent.Task;
                terminalState.MarkIdle();
            }
            finally
            {
                terminalInput.BecameQuiescent -= HandleQuiescence;
            }

            throw;
        }
    }

    private static async Task HandleResizeAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken,
        IAgentSessionService agents,
        TmuxSessionManager tmux,
        TerminalActivityStateTracker terminalState,
        TerminalResizeFrame resize)
    {
        if (!TryGetHealthyAgent(agents, out var status, out var errorCode, out var errorDetail))
        {
            terminalState.MarkError();
            await SendJsonAsync(socket, new TerminalErrorFrame(errorCode, errorDetail), sendLock, cancellationToken);
            return;
        }

        try
        {
            await tmux.ResizePaneAsync(status.Session.TmuxSessionName, resize.Columns, resize.Rows, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AgentLifecycleException exception)
        {
            terminalState.MarkError();
            await SendJsonAsync(socket, new TerminalErrorFrame(exception.Code, exception.Message), sendLock, cancellationToken);
        }
        catch (TmuxUnavailableException)
        {
            terminalState.MarkError();
            await SendJsonAsync(
                socket,
                new TerminalErrorFrame("terminal_resize_unavailable", "The tmux resize boundary is unavailable."),
                sendLock,
                cancellationToken);
        }
        catch (AgentConfigurationException exception)
        {
            terminalState.MarkError();
            await SendJsonAsync(socket, new TerminalErrorFrame("terminal_resize_invalid", exception.Message), sendLock, cancellationToken);
        }
    }

    private static bool TryGetHealthyAgent(
        IAgentSessionService agents,
        out AgentStatus status,
        out string errorCode,
        out string errorDetail)
    {
        try
        {
            status = agents.GetPersonal();
        }
        catch (AgentConfigurationException exception)
        {
            status = null!;
            errorCode = "agent_configuration_invalid";
            errorDetail = exception.Message;
            return false;
        }
        catch (AgentLifecycleException exception)
        {
            status = null!;
            errorCode = exception.Code;
            errorDetail = exception.Message;
            return false;
        }

        if (!status.RuntimeHealthy)
        {
            errorCode = "terminal_session_unavailable";
            errorDetail = "The personal Claude session is not healthy enough to receive terminal input.";
            return false;
        }

        errorCode = string.Empty;
        errorDetail = string.Empty;
        return true;
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
