using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Memory;
using PersonalAssistant.Harness.Runtime;
using PersonalAssistant.Server.Contracts;

namespace PersonalAssistant.Server.Endpoints;

public static class SessionHygieneEndpoints
{
    public static IEndpointRouteBuilder MapSessionHygieneEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/agents/personal/hygiene/compact",
            (SessionHygieneRequestContract? request, ISessionHygieneService service, CancellationToken cancellationToken) =>
                ExecuteActionAsync(SessionHygieneAction.Compact, request, service, cancellationToken));
        endpoints.MapPost(
            "/api/agents/personal/hygiene/clear",
            (SessionHygieneRequestContract? request, ISessionHygieneService service, CancellationToken cancellationToken) =>
                ExecuteActionAsync(SessionHygieneAction.Clear, request, service, cancellationToken));
        endpoints.MapPost(
            "/api/agents/personal/hygiene/rotate",
            (SessionHygieneRequestContract? request, ISessionHygieneService service, CancellationToken cancellationToken) =>
                ExecuteActionAsync(SessionHygieneAction.Rotate, request, service, cancellationToken));
        endpoints.MapPost(
            "/api/agents/personal/hygiene/checkpoint",
            (SessionHygieneRequestContract? request, ISessionHygieneService service, CancellationToken cancellationToken) =>
                ExecuteCheckpointAsync(request, service, cancellationToken));
        return endpoints;
    }

    private static async Task<IResult> ExecuteActionAsync(
        SessionHygieneAction action,
        SessionHygieneRequestContract? request,
        ISessionHygieneService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var typedRequest = ToTypedRequest(action, request);
            var result = await service.ExecutePersonalAsync(typedRequest, cancellationToken);
            return Results.Ok(SessionHygieneResponse.From(result));
        }
        catch (Exception exception) when (IsHandled(exception))
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> ExecuteCheckpointAsync(
        SessionHygieneRequestContract? request,
        ISessionHygieneService service,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request?.RequestId is null || request.Checkpoint is null)
            {
                throw new SessionHygieneException("hygiene_request_invalid", "A request id and checkpoint payload are required.");
            }

            var checkpoint = ToCheckpointRequest(request.Checkpoint);
            var receipt = await service.CheckpointPersonalAsync(request.RequestId, checkpoint, cancellationToken);
            return Results.Ok(CheckpointResponse.From(receipt));
        }
        catch (Exception exception) when (IsHandled(exception))
        {
            return ToProblem(exception);
        }
    }

    private static SessionHygieneRequest ToTypedRequest(
        SessionHygieneAction action,
        SessionHygieneRequestContract? request)
    {
        if (request?.RequestId is null || request.Checkpoint is null)
        {
            throw new SessionHygieneException("hygiene_request_invalid", "A request id and checkpoint payload are required.");
        }

        var checkpoint = ToCheckpointRequest(request.Checkpoint);
        return new SessionHygieneRequest(request.RequestId, action, checkpoint);
    }

    private static CheckpointRequest ToCheckpointRequest(CheckpointRequestContract request)
    {
        if (request.Reason is null || request.GeneratedMemory is null || request.GeneratedHandoff is null)
        {
            throw new SessionHygieneException("hygiene_request_invalid", "Checkpoint reason, memory, and handoff fields are required.");
        }

        return new CheckpointRequest(request.Reason, request.GeneratedMemory, request.GeneratedHandoff);
    }

    private static bool IsHandled(Exception exception) =>
        exception is AgentConfigurationException or AgentLifecycleException;

    private static IResult ToProblem(Exception exception)
    {
        var code = exception switch
        {
            AgentLifecycleException lifecycle => lifecycle.Code,
            AgentConfigurationException => "agent_configuration_invalid",
            _ => "hygiene_unavailable"
        };
        var status = code switch
        {
            "hygiene_request_invalid" or "hygiene_action_invalid" or "hygiene_checkpoint_mismatch" => 400,
            "agent_runtime_unavailable" or "agent_configuration_invalid" or "tmux_unavailable" => 503,
            _ => 409
        };
        return Results.Problem(
            statusCode: status,
            title: "Session hygiene request rejected",
            detail: exception.Message,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}
