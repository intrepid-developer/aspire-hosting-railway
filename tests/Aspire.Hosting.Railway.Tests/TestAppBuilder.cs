using System.Reflection;

using Aspire.Hosting.ApplicationModel;

using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting.Railway.Tests;

internal static class TestAppBuilder
{
    public static IDistributedApplicationBuilder CreateRun()
    {
        return DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = [],
            DisableDashboard = true,
            AllowUnsecuredTransport = true
        });
    }

    public static IDistributedApplicationBuilder CreatePublish(string? outputPath = null)
    {
        outputPath ??= Path.Combine(Path.GetTempPath(), "aspire-railway-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);

        return DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args =
            [
                "AppHost:Operation=publish",
                $"Pipeline:OutputPath={outputPath}"
            ],
            DisableDashboard = true,
            AllowUnsecuredTransport = true
        });
    }

    public static Task ExecuteBeforeStartHooksAsync(DistributedApplication app, CancellationToken cancellationToken = default)
    {
        var method = typeof(DistributedApplication).GetMethod(
            "ExecuteBeforeStartHooksAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        return (Task)method.Invoke(app, [cancellationToken])!;
    }

    public static DistributedApplicationModel GetModel(DistributedApplication app) =>
        app.Services.GetRequiredService<DistributedApplicationModel>();
}
