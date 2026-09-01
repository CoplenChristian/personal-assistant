using PersonalAssistant.Harness;
using PersonalAssistant.Harness.Agents;
using PersonalAssistant.Harness.Settings;
using PersonalAssistant.Server.Endpoints;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
var repositoryRoot = FindRepositoryRoot(builder.Configuration["PA_REPOSITORY_ROOT"] ?? builder.Environment.ContentRootPath);
var bootstrapEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
{
    ["PA_RUNTIME_DIR"] = builder.Configuration["PA_RUNTIME_DIR"],
    ["PA_SERVER_HOST"] = builder.Configuration["PA_SERVER_HOST"],
    ["PA_SERVER_PORT"] = builder.Configuration["PA_SERVER_PORT"],
    ["PA_TMUX_PREFIX"] = builder.Configuration["PA_TMUX_PREFIX"],
    ["PA_VAULT_DIR"] = builder.Configuration["PA_VAULT_DIR"]
};
var harnessRuntime = HarnessRuntime.Create(repositoryRoot, bootstrapEnvironment, repositoryRoot);

builder.Services.AddSingleton<HarnessRuntime>(_ => harnessRuntime);
builder.Services.AddSingleton<SettingsService>(serviceProvider => serviceProvider.GetRequiredService<HarnessRuntime>().Settings);
builder.Services.AddSingleton<IAgentSessionService>(serviceProvider => serviceProvider.GetRequiredService<HarnessRuntime>().Agents);
builder.Services.AddProblemDetails();
builder.WebHost.UseUrls($"http://{harnessRuntime.Bootstrap.ServerHost}:{harnessRuntime.Bootstrap.ServerPort}");

var app = builder.Build();
var dashboardRoot = Path.Combine(repositoryRoot, "apps", "dashboard", "dist");
if (Directory.Exists(dashboardRoot))
{
    var dashboardProvider = new PhysicalFileProvider(dashboardRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = dashboardProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = dashboardProvider });
}

app.MapSettingsEndpoints();
app.MapAgentEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
if (Directory.Exists(dashboardRoot))
{
    var dashboardIndex = Path.Combine(dashboardRoot, "index.html");
    app.MapFallback("/api/{**path}", context =>
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    });
    app.MapFallback(async context =>
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(dashboardIndex);
    });
}
app.Lifetime.ApplicationStopping.Register(harnessRuntime.Dispose);
app.Run();

static string FindRepositoryRoot(string startPath)
{
    var current = new DirectoryInfo(Path.GetFullPath(startPath));
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "policies", "defaults", "runtime.yaml")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Unable to locate the repository root containing policies/defaults/runtime.yaml.");
}

public partial class Program;
