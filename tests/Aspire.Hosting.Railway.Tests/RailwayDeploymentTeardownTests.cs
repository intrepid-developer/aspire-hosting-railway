using Aspire.Hosting.Railway;

namespace Aspire.Hosting.Railway.Tests;

public sealed class RailwayDeploymentTeardownTests
{
    [Fact]
    public void CreateUpdateInput_SetsOverlapAndDrainingAndOmitsUnset()
    {
        var withBoth = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService
            {
                Name = "api",
                OverlapSeconds = 60,
                DrainingSeconds = 10
            },
            "nginx");

        Assert.Equal(60, withBoth.OverlapSeconds);
        Assert.Equal(10, withBoth.DrainingSeconds);
        Assert.Null(withBoth.StartCommand);
        Assert.Null(withBoth.PreDeployCommand);
        Assert.Null(withBoth.HealthcheckPath);
        Assert.Null(withBoth.HealthcheckTimeout);
        Assert.Null(withBoth.RestartPolicyType);
        Assert.Null(withBoth.RestartPolicyMaxRetries);
        Assert.Null(withBoth.NumReplicas);
        Assert.Null(withBoth.SleepApplication);
        Assert.Null(withBoth.MultiRegionConfig);

        var imageOnly = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService { Name = "api" },
            "nginx");

        Assert.Null(imageOnly.OverlapSeconds);
        Assert.Null(imageOnly.DrainingSeconds);
        Assert.Equal("nginx", imageOnly.Source?.Image);
    }

    [Fact]
    public void CreateUpdateInput_EitherTeardownFieldAlone_OmitsTheOther()
    {
        var overlapOnly = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService { Name = "api", OverlapSeconds = 60 },
            "nginx");

        Assert.Equal(60, overlapOnly.OverlapSeconds);
        Assert.Null(overlapOnly.DrainingSeconds);

        var drainOnly = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService { Name = "api", DrainingSeconds = 10 },
            "nginx");

        Assert.Null(drainOnly.OverlapSeconds);
        Assert.Equal(10, drainOnly.DrainingSeconds);
    }

    [Fact]
    public void CreateUpdateInput_ZeroSeconds_AreSent()
    {
        var input = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService
            {
                Name = "api",
                OverlapSeconds = 0,
                DrainingSeconds = 0
            },
            "nginx");

        Assert.Equal(0, input.OverlapSeconds);
        Assert.Equal(0, input.DrainingSeconds);
    }

    [Fact]
    public void CreateUpdateInput_HealthcheckRestartStartAndOverlapSurviveTogether()
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
                PreDeployCommand = ["dotnet MyApp.dll --migrate"],
                OverlapSeconds = 60,
                DrainingSeconds = 10,
                CronSchedule = "0 3 * * *"
            },
            "nginx");

        Assert.Equal("/health", input.HealthcheckPath);
        Assert.Equal(90, input.HealthcheckTimeout);
        Assert.Equal("NEVER", input.RestartPolicyType);
        Assert.Equal(1, input.RestartPolicyMaxRetries);
        Assert.Equal("/bin/sh -c \"exec ./api\"", input.StartCommand);
        Assert.Equal(["dotnet MyApp.dll --migrate"], input.PreDeployCommand);
        Assert.Equal(60, input.OverlapSeconds);
        Assert.Equal(10, input.DrainingSeconds);
        Assert.Equal("0 3 * * *", input.CronSchedule);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-60)]
    public void CreateUpdateInput_NegativeOverlapSeconds_Fails(int seconds)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayServiceComputeSettings.CreateUpdateInput(
                new RailwayPlanService { Name = "api", OverlapSeconds = seconds },
                "nginx"));

        Assert.Contains("overlapSeconds", exception.Message, StringComparison.Ordinal);
        Assert.Contains("greater than or equal to 0", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    public void CreateUpdateInput_NegativeDrainingSeconds_Fails(int seconds)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayServiceComputeSettings.CreateUpdateInput(
                new RailwayPlanService { Name = "api", DrainingSeconds = seconds },
                "nginx"));

        Assert.Contains("drainingSeconds", exception.Message, StringComparison.Ordinal);
        Assert.Contains("greater than or equal to 0", exception.Message, StringComparison.Ordinal);
    }
}
