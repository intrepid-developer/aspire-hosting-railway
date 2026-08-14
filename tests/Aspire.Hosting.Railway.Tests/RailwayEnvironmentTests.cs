using Aspire.Hosting.ApplicationModel;

using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting.Railway.Tests;

public class RailwayEnvironmentTests
{
    [Fact]
    public void RunMode_DoesNotAddEnvironmentToModel()
    {
        var builder = TestAppBuilder.CreateRun();
        builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var model = TestAppBuilder.GetModel(app);

        Assert.DoesNotContain(model.Resources, resource => resource is RailwayEnvironmentResource);
    }

    [Fact]
    public void PublishMode_AddsEnvironmentToModel()
    {
        var builder = TestAppBuilder.CreatePublish();
        builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var model = TestAppBuilder.GetModel(app);

        Assert.Contains(model.Resources, resource => resource is RailwayEnvironmentResource);
    }

    [Fact]
    public async Task PublishMode_AttachesDeploymentTargetToComputeResources()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var api = builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        await TestAppBuilder.ExecuteBeforeStartHooksAsync(app);

        var annotation = api.Resource.GetDeploymentTargetAnnotation();
        Assert.NotNull(annotation);
        Assert.Same(railway.Resource, annotation.ComputeEnvironment);
        var service = Assert.IsType<RailwayServiceResource>(annotation.DeploymentTarget);
        Assert.Same(railway.Resource, service.Parent);
    }

    [Fact]
    public async Task PublishAsRailwayPostgres_WithoutEnvironment_ThrowsOnValidate()
    {
        var builder = TestAppBuilder.CreatePublish();
        builder.AddPostgres("postgres").PublishAsRailwayPostgres();

        using var app = builder.Build();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => TestAppBuilder.ExecuteBeforeStartHooksAsync(app));

        Assert.Contains("RailwayEnvironmentResource", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AddRailwayEnvironment", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetHostAddressExpression_ReturnsHostOnly()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var api = builder.AddContainer("api", "nginx").WithHttpEndpoint(targetPort: 8080);

        var host = railway.Resource.GetHostAddressExpression(api.GetEndpoint("http"));

        Assert.Equal("api.railway.internal", host.ValueExpression);
        Assert.DoesNotContain("://", host.ValueExpression, StringComparison.Ordinal);
        Assert.DoesNotContain(":8080", host.ValueExpression, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanJson_ContainsNoSecrets()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddRedis("redis").PublishAsRailwayRedis();
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var model = TestAppBuilder.GetModel(app);
        var plan = RailwayPlanBuilder.Create(model, railway.Resource, "Production");
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Equal("production", plan.RailwayEnvironmentName);
        Assert.Contains("RAILWAY_TOKEN", json, StringComparison.Ordinal);
        Assert.Contains("${{postgres.DATABASE_URL}}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithComputeEnvironment_SkipsForeignEnvironment()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var other = builder.AddRailwayEnvironment("other");
        var api = builder.AddContainer("api", "nginx").WithComputeEnvironment(other);

        using var app = builder.Build();
        await TestAppBuilder.ExecuteBeforeStartHooksAsync(app);

        Assert.Null(api.Resource.GetDeploymentTargetAnnotation(railway.Resource));
        var annotation = api.Resource.GetDeploymentTargetAnnotation(other.Resource);
        Assert.NotNull(annotation);
        Assert.Same(other.Resource, annotation.ComputeEnvironment);
    }
}
