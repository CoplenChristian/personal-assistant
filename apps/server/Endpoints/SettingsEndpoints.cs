using Microsoft.AspNetCore.Mvc;
using PersonalAssistant.Harness.Settings;
using PersonalAssistant.Server.Contracts;

namespace PersonalAssistant.Server.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/settings", (SettingsService service) =>
        {
            try
            {
                return Results.Ok(service.GetSnapshot());
            }
            catch (SettingsException exception)
            {
                return ToProblem(exception);
            }
        });

        endpoints.MapPatch("/api/settings", (SettingsPatchRequest? request, SettingsService service) =>
        {
            try
            {
                if (request?.Changes is null)
                {
                    throw new SettingsException("invalid_request", "The changes array is required.");
                }

                var changes = request.Changes.Select(ToSettingChange).ToArray();
                return Results.Ok(service.ApplyChanges(changes));
            }
            catch (SettingsException exception)
            {
                return ToProblem(exception);
            }
        });

        endpoints.MapDelete("/api/settings/{key}", (string key, SettingsService service) =>
        {
            try
            {
                return Results.Ok(service.Reset(key));
            }
            catch (SettingsException exception)
            {
                return ToProblem(exception);
            }
        });

        return endpoints;
    }

    private static SettingChange ToSettingChange(SettingsChangeRequest? change)
    {
        if (change is null || string.IsNullOrWhiteSpace(change.Key))
        {
            throw new SettingsException("invalid_request", "Every setting change requires a key.");
        }

        var scope = SettingScopeType.Global;
        var scopeId = change.Scope?.Id;
        if (change.Scope is not null && !string.IsNullOrWhiteSpace(change.Scope.Type)
            && !Enum.TryParse(change.Scope.Type, ignoreCase: true, out scope))
        {
            throw new SettingsException("unsupported_scope", "The requested settings scope is not supported.");
        }

        return new SettingChange(change.Key, change.Value, scope, scopeId);
    }

    private static IResult ToProblem(SettingsException exception)
    {
        var status = exception.Code is "settings_store_invalid" or "settings_unavailable" ? 503 : 400;
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = exception.Code
        };
        if (exception.Fields.Count > 0)
        {
            extensions["fields"] = exception.Fields;
        }

        return Results.Problem(
            statusCode: status,
            title: "Settings request rejected",
            detail: exception.Message,
            extensions: extensions);
    }
}
