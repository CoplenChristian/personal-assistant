using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Server.Contracts;

namespace PersonalAssistant.Server.Endpoints;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/agents/personal", (IAgentSessionService service) =>
        {
            try
            {
                return Results.Ok(AgentStatusResponse.From(service.GetPersonal()));
            }
            catch (Exception exception) when (IsHandled(exception))
            {
                return ToProblem(exception);
            }
        });

        endpoints.MapPost("/api/agents/personal/start", (IAgentSessionService service) =>
        {
            try
            {
                return Results.Ok(AgentStatusResponse.From(service.StartPersonal()));
            }
            catch (Exception exception) when (IsHandled(exception))
            {
                return ToProblem(exception);
            }
        });

        endpoints.MapPost("/api/agents/personal/stop", (IAgentSessionService service) =>
        {
            try
            {
                return Results.Ok(AgentStatusResponse.From(service.StopPersonal()));
            }
            catch (Exception exception) when (IsHandled(exception))
            {
                return ToProblem(exception);
            }
        });

        endpoints.MapGet("/api/agents/work", (IAgentSessionService service) =>
        {
            try
            {
                return Results.Ok(AgentStatusResponse.From(service.GetWork()));
            }
            catch (Exception exception) when (IsHandled(exception))
            {
                return ToProblem(exception);
            }
        });

        endpoints.MapPost("/api/agents/work/start", (IAgentSessionService service) =>
        {
            try
            {
                return Results.Ok(AgentStatusResponse.From(service.StartWork()));
            }
            catch (Exception exception) when (IsHandled(exception))
            {
                return ToProblem(exception);
            }
        });

        endpoints.MapPost("/api/agents/work/stop", (IAgentSessionService service) =>
        {
            try
            {
                return Results.Ok(AgentStatusResponse.From(service.StopWork()));
            }
            catch (Exception exception) when (IsHandled(exception))
            {
                return ToProblem(exception);
            }
        });

        return endpoints;
    }

    private static bool IsHandled(Exception exception) =>
        exception is AgentConfigurationException or AgentLifecycleException;

    private static IResult ToProblem(Exception exception)
    {
        var code = exception switch
        {
            AgentLifecycleException lifecycle => lifecycle.Code,
            AgentConfigurationException => "agent_configuration_invalid",
            _ => "agent_unavailable"
        };
        var status = code is "agent_runtime_unavailable" or "agent_configuration_invalid" ? 503 : 409;
        return Results.Problem(
            statusCode: status,
            title: "Agent request rejected",
            detail: exception.Message,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}
