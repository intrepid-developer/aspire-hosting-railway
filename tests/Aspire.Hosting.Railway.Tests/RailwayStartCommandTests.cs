using Aspire.Hosting.Railway;

namespace Aspire.Hosting.Railway.Tests;

public sealed class RailwayStartCommandTests
{
    [Fact]
    public void CreateUpdateInput_SetsStartCommandAndOmitsUnset()
    {
        var withStart = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService
            {
                Name = "api",
                StartCommand = "/bin/sh -c \"exec dotnet MyApp.dll --urls http://*:$PORT\""
            },
            "nginx");

        Assert.Equal(
            "/bin/sh -c \"exec dotnet MyApp.dll --urls http://*:$PORT\"",
            withStart.StartCommand);
        Assert.Null(withStart.PreDeployCommand);
        Assert.Null(withStart.HealthcheckPath);
        Assert.Null(withStart.HealthcheckTimeout);
        Assert.Null(withStart.RestartPolicyType);
        Assert.Null(withStart.RestartPolicyMaxRetries);
        Assert.Null(withStart.NumReplicas);
        Assert.Null(withStart.SleepApplication);
        Assert.Null(withStart.MultiRegionConfig);

        var imageOnly = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService { Name = "api" },
            "nginx");

        Assert.Null(imageOnly.StartCommand);
        Assert.Null(imageOnly.PreDeployCommand);
        Assert.Equal("nginx", imageOnly.Source?.Image);
    }

    [Fact]
    public void CreateUpdateInput_SetsPreDeployCommandAsArrayAndOmitsEmpty()
    {
        var withPreDeploy = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService
            {
                Name = "api",
                PreDeployCommand = ["dotnet MyApp.dll --migrate"]
            },
            "nginx");

        Assert.Equal(["dotnet MyApp.dll --migrate"], withPreDeploy.PreDeployCommand);
        Assert.Null(withPreDeploy.StartCommand);

        var emptyArray = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService
            {
                Name = "api",
                PreDeployCommand = []
            },
            "nginx");

        Assert.Null(emptyArray.PreDeployCommand);
        Assert.Null(emptyArray.StartCommand);
    }

    [Fact]
    public void CreateUpdateInput_HealthcheckRestartAndStartSurviveTogether()
    {
        var input = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService
            {
                Name = "api",
                HealthcheckPath = "/health",
                HealthcheckTimeout = 90,
                RestartPolicyType = "NEVER",
                RestartPolicyMaxRetries = 1,
                StartCommand = "/bin/sh -c \"exec ./api\"",
                PreDeployCommand = ["dotnet MyApp.dll --migrate"]
            },
            "nginx");

        Assert.Equal("/health", input.HealthcheckPath);
        Assert.Equal(90, input.HealthcheckTimeout);
        Assert.Equal("NEVER", input.RestartPolicyType);
        Assert.Equal(1, input.RestartPolicyMaxRetries);
        Assert.Equal("/bin/sh -c \"exec ./api\"", input.StartCommand);
        Assert.Equal(["dotnet MyApp.dll --migrate"], input.PreDeployCommand);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateUpdateInput_EmptyStartCommand_Fails(string startCommand)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayServiceComputeSettings.CreateUpdateInput(
                new RailwayPlanService { Name = "api", StartCommand = startCommand },
                "nginx"));

        Assert.Contains("startCommand", exception.Message, StringComparison.Ordinal);
        Assert.Contains("non-empty", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateUpdateInput_EmptyPreDeployStep_Fails(string step)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayServiceComputeSettings.CreateUpdateInput(
                new RailwayPlanService { Name = "api", PreDeployCommand = [step] },
                "nginx"));

        Assert.Contains("preDeployCommand", exception.Message, StringComparison.Ordinal);
        Assert.Contains("non-empty", exception.Message, StringComparison.Ordinal);
    }
}
