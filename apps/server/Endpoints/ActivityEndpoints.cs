using Microsoft.AspNetCore.Mvc;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Server.Contracts;

namespace PersonalAssistant.Server.Endpoints;

public static class ActivityEndpoints
{
    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/activity", (
            string? date,
            string? timezone,
            int? limit,
            ActivityQueryService activityQuery) =>
        {
            try
            {
                var result = activityQuery.Query(new ActivityQueryRequest(date, timezone, limit));
                return Results.Ok(ActivityResponse.From(result));
            }
            catch (ActivityQueryException exception)
            {
                return ToProblem(exception);
            }
        });

        return endpoints;
    }

    private static IResult ToProblem(ActivityQueryException exception) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Activity request rejected",
            detail: exception.Message,
            extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
}
