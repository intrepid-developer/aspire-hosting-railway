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

    /// <summary>
    /// Adds GHCR with parameter-backed credentials. Values are test
    /// placeholders, not real tokens.
    /// </summary>
    public static IResourceBuilder<ContainerRegistryResource> AddTestGhcr(
        this IDistributedApplicationBuilder builder,
        string repository = "intrepid-developer/playground")
    {
        var username = builder.AddParameter("ghcr-username", GraphQLFixtures.RegistryUsername);
        var password = builder.AddParameter("ghcr-password", GraphQLFixtures.RegistryPassword, secret: true);
        return builder.AddContainerRegistry("ghcr", "ghcr.io", repository)
            .WithUsername(username)
            .WithPassword(password);
    }

    /// <summary>
    /// Adds a real <c>AddProject</c> consumer against a throwaway csproj so
    /// plan tests can distinguish .NET projects from containers.
    /// </summary>
    public static IResourceBuilder<ProjectResource> AddTestProject(
        this IDistributedApplicationBuilder builder,
        string name = "api")
    {
        var directory = Path.Combine(Path.GetTempPath(), "aspire-railway-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, $"{name}.csproj");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);
        return builder.AddProject(name, projectPath);
    }
}
