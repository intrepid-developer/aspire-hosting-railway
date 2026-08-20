using Aspire.Hosting.Railway;

namespace Aspire.Hosting.Railway.Tests;

public sealed class RailwayCronScheduleTests
{
    [Fact]
    public void CreateUpdateInput_SetsCronScheduleAndOmitsUnset()
    {
        var withCron = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService
            {
                Name = "api",
                CronSchedule = "0 3 * * *"
            },
            "nginx");

        Assert.Equal("0 3 * * *", withCron.CronSchedule);
        Assert.Null(withCron.StartCommand);
        Assert.Null(withCron.PreDeployCommand);
        Assert.Null(withCron.HealthcheckPath);
        Assert.Null(withCron.HealthcheckTimeout);
        Assert.Null(withCron.RestartPolicyType);
        Assert.Null(withCron.RestartPolicyMaxRetries);
        Assert.Null(withCron.OverlapSeconds);
        Assert.Null(withCron.DrainingSeconds);
        Assert.Null(withCron.NumReplicas);
        Assert.Null(withCron.SleepApplication);
        Assert.Null(withCron.MultiRegionConfig);

        var imageOnly = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService { Name = "api" },
            "nginx");

        Assert.Null(imageOnly.CronSchedule);
        Assert.Equal("nginx", imageOnly.Source?.Image);
    }

    [Theory]
    [InlineData("*/15 * * * *")]
    [InlineData("0 3 * * *")]
    public void CreateUpdateInput_AcceptsFiveFieldSchedules(string cronSchedule)
    {
        var input = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService { Name = "api", CronSchedule = cronSchedule },
            "nginx");

        Assert.Equal(cronSchedule, input.CronSchedule);
    }

    [Fact]
    public void CreateUpdateInput_HealthcheckRestartStartOverlapAndCronSurviveTogether()
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
    [InlineData("")]
    [InlineData("   ")]
    public void CreateUpdateInput_EmptyCronSchedule_Fails(string cronSchedule)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayServiceComputeSettings.CreateUpdateInput(
                new RailwayPlanService { Name = "api", CronSchedule = cronSchedule },
                "nginx"));

        Assert.Contains("cronSchedule", exception.Message, StringComparison.Ordinal);
        Assert.Contains("non-empty", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("* * * * *")]
    [InlineData("*/2 * * * *")]
    [InlineData("*/1 * * * *")]
    [InlineData("*/3 * * * *")]
    [InlineData("*/4 * * * *")]
    public void CreateUpdateInput_FasterThanEveryFiveMinutes_Fails(string cronSchedule)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayServiceComputeSettings.CreateUpdateInput(
                new RailwayPlanService { Name = "api", CronSchedule = cronSchedule },
                "nginx"));

        Assert.Contains("cronSchedule", exception.Message, StringComparison.Ordinal);
        Assert.Contains("5 minutes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateUpdateInput_TimezoneField_FailsAsNotFiveField()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayServiceComputeSettings.CreateUpdateInput(
                new RailwayPlanService { Name = "api", CronSchedule = "0 3 * * * Europe/London" },
                "nginx"));

        Assert.Contains("cronSchedule", exception.Message, StringComparison.Ordinal);
        Assert.Contains("five-field", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Timezone", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateUpdateInput_ReplicasGreaterThanOne_Fails()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayServiceComputeSettings.CreateUpdateInput(
                new RailwayPlanService
                {
                    Name = "api",
                    Replicas = 2,
                    CronSchedule = "0 3 * * *"
                },
                "nginx"));

        Assert.Contains("cronSchedule", exception.Message, StringComparison.Ordinal);
        Assert.Contains("replicas", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateUpdateInput_ServerlessTrue_Fails()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayServiceComputeSettings.CreateUpdateInput(
                new RailwayPlanService
                {
                    Name = "api",
                    Serverless = true,
                    CronSchedule = "0 3 * * *"
                },
                "nginx"));

        Assert.Contains("cronSchedule", exception.Message, StringComparison.Ordinal);
        Assert.Contains("serverless", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateUpdateInput_ReplicasOneAndServerlessFalse_AreAllowed()
    {
        var input = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService
            {
                Name = "api",
                Replicas = 1,
                Serverless = false,
                CronSchedule = "*/15 * * * *"
            },
            "nginx");

        Assert.Equal("*/15 * * * *", input.CronSchedule);
        Assert.Equal(1, input.NumReplicas);
        Assert.False(input.SleepApplication);
    }
}
