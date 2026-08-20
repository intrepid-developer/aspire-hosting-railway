using Aspire.Hosting.Railway;

namespace Aspire.Hosting.Railway.Tests;

public sealed class RailwayRestartPolicyTests
{
    public static readonly TheoryData<RailwayRestartPolicy, string> OfficialMembers = new()
    {
        { RailwayRestartPolicy.OnFailure, "ON_FAILURE" },
        { RailwayRestartPolicy.Always, "ALWAYS" },
        { RailwayRestartPolicy.Never, "NEVER" }
    };

    [Theory]
    [MemberData(nameof(OfficialMembers))]
    public void ToGraphQL_MapsOfficialMembers(RailwayRestartPolicy policy, string expected)
    {
        Assert.Equal(expected, RailwayRestartPolicyMapper.ToGraphQL(policy));
        Assert.Contains(expected, RailwayConstants.OfficialRestartPolicyTypes);
    }

    [Fact]
    public void OfficialTypes_AreExactlyTheThreeGraphQLValues()
    {
        Assert.Equal(
            ["ON_FAILURE", "ALWAYS", "NEVER"],
            RailwayConstants.OfficialRestartPolicyTypes);
        Assert.Equal(
            Enum.GetValues<RailwayRestartPolicy>().Length,
            RailwayConstants.OfficialRestartPolicyTypes.Count);
    }

    [Theory]
    [InlineData("on_failure")]
    [InlineData("OnFailure")]
    [InlineData("always")]
    [InlineData("never")]
    [InlineData("ONFAILURE")]
    [InlineData("not-a-restart-policy")]
    public void RequireOfficialType_RejectsUnknownStrings(string type)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayRestartPolicyMapper.RequireOfficialType("api", type));

        Assert.Contains(type, exception.Message, StringComparison.Ordinal);
        Assert.Contains("ON_FAILURE", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ALWAYS", exception.Message, StringComparison.Ordinal);
        Assert.Contains("NEVER", exception.Message, StringComparison.Ordinal);
        Assert.Contains("docs.railway.com/deployments/restart-policy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateUpdateInput_SetsRestartPolicyFieldsAndOmitsUnset()
    {
        var withPolicy = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService
            {
                Name = "api",
                RestartPolicyType = "ON_FAILURE",
                RestartPolicyMaxRetries = 10
            },
            "nginx");

        Assert.Equal("ON_FAILURE", withPolicy.RestartPolicyType);
        Assert.Equal(10, withPolicy.RestartPolicyMaxRetries);
        Assert.Null(withPolicy.HealthcheckPath);
        Assert.Null(withPolicy.HealthcheckTimeout);
        Assert.Null(withPolicy.NumReplicas);
        Assert.Null(withPolicy.SleepApplication);
        Assert.Null(withPolicy.MultiRegionConfig);

        var imageOnly = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService { Name = "api" },
            "nginx");

        Assert.Null(imageOnly.RestartPolicyType);
        Assert.Null(imageOnly.RestartPolicyMaxRetries);
        Assert.Equal("nginx", imageOnly.Source?.Image);
    }

    [Fact]
    public void CreateUpdateInput_RestartPolicyDoesNotDropHealthcheck()
    {
        var input = RailwayServiceComputeSettings.CreateUpdateInput(
            new RailwayPlanService
            {
                Name = "api",
                HealthcheckPath = "/health",
                HealthcheckTimeout = 90,
                RestartPolicyType = "NEVER",
                RestartPolicyMaxRetries = 1
            },
            "nginx");

        Assert.Equal("/health", input.HealthcheckPath);
        Assert.Equal(90, input.HealthcheckTimeout);
        Assert.Equal("NEVER", input.RestartPolicyType);
        Assert.Equal(1, input.RestartPolicyMaxRetries);
        Assert.Null(input.StartCommand);
        Assert.Null(input.PreDeployCommand);
    }
}
