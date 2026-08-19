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
    public async Task ResolveDeployImage_PrefersFullRemoteImageNameOverPlanPlaceholder()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io", "northwind-samples/harbor");
        railway.WithContainerRegistry(ghcr);
        var api = builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        await TestAppBuilder.ExecuteBeforeStartHooksAsync(app);

        var resolved = await RailwayEnvironmentResource.ResolveDeployImageAsync(
            api.Resource,
            railway.Resource.ResolveContainerRegistry(TestAppBuilder.GetModel(app)),
            "{api.containerImage}",
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(resolved));
        Assert.DoesNotContain("{", resolved, StringComparison.Ordinal);
        Assert.Contains("ghcr.io", resolved, StringComparison.Ordinal);
        Assert.Contains("api", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveDeployImage_IgnoresPlanPlaceholderWhenNoRegistry()
    {
        var resolved = await RailwayEnvironmentResource.ResolveDeployImageAsync(
            resource: null,
            registry: null,
            planImage: "{api.containerImage}",
            CancellationToken.None);

        Assert.Null(resolved);
    }

    [Fact]
    public void Plan_DatabaseChildReference_EmitsCatalogConnectionStringAndParameterEnv()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var postgres = builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        var database = postgres.AddDatabase("catalog");
        var secret = builder.AddParameter("billing-secret-key", "sk_test_placeholder", secret: true);
        builder.AddContainer("api", "nginx")
            .WithReference(database)
            .WithEnvironment("Billing__SecretKey", secret)
            .WithEnvironment("Storage__RequireBucket", "true");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var api = Assert.Single(plan.Services, service => service.Name == "api");

        Assert.Equal("${{postgres.DATABASE_URL}}", api.Environment["ConnectionStrings__catalog"]);
        Assert.False(api.Environment.ContainsKey("ConnectionStrings__postgres"));
        Assert.Equal("billing-secret-key", api.Environment["Billing__SecretKey"]);
        Assert.Equal("true", api.Environment["Storage__RequireBucket"]);
        Assert.Contains("billing-secret-key", plan.Parameters);
        Assert.DoesNotContain("sk_test_placeholder", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void RailwayReference_RewritesServiceNameCasing()
    {
        var rewritten = RailwayReferenceExpressions.RewriteServiceName(
            "${{postgres.DATABASE_URL}}",
            ["Postgres", "api"]);

        Assert.Equal("${{Postgres.DATABASE_URL}}", rewritten);
    }

    [Fact]
    public void PlanJson_ContainsNoSecrets()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var db = builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        var cache = builder.AddRedis("redis").PublishAsRailwayRedis();
        builder.AddContainer("api", "nginx")
            .WithReference(db)
            .WithReference(cache);

        using var app = builder.Build();
        var model = TestAppBuilder.GetModel(app);
        var plan = RailwayPlanBuilder.Create(model, railway.Resource, "Production");
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Equal("production", plan.RailwayEnvironmentName);
        Assert.Contains("RAILWAY_TOKEN", json, StringComparison.Ordinal);
        Assert.Contains("${{postgres.DATABASE_URL}}", json, StringComparison.Ordinal);
        Assert.Contains("${{redis.REDIS_URL}}", json, StringComparison.Ordinal);
        Assert.Contains("postgres-password", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_OnlyEmitsConnectionStringsForServicesThatWithReference()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var db = builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddContainer("api", "nginx").WithReference(db);
        builder.AddContainer("marketing", "nginx");

        using var app = builder.Build();
        var model = TestAppBuilder.GetModel(app);
        var plan = RailwayPlanBuilder.Create(model, railway.Resource, "Production");

        var api = Assert.Single(plan.Services, service => service.Name == "api");
        var marketing = Assert.Single(plan.Services, service => service.Name == "marketing");
        Assert.Equal("${{postgres.DATABASE_URL}}", api.Environment["ConnectionStrings__postgres"]);
        Assert.False(marketing.Environment.ContainsKey("ConnectionStrings__postgres"));
    }

    [Fact]
    public void Plan_CapturesNonRailwayConnectionStringAsParameterName()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var key = builder.AddParameter("xai-api-key", "placeholder-openai-key", secret: true);
        var chat = builder.AddResource(new FakeChatConnectionStringResource("chat", key.Resource));
        builder.AddContainer("api", "nginx").WithReference(chat);
        builder.AddContainer("marketing", "nginx");

        using var app = builder.Build();
        var model = TestAppBuilder.GetModel(app);
        var plan = RailwayPlanBuilder.Create(model, railway.Resource, "Production");
        var json = RailwayPlanBuilder.ToJson(plan);

        var api = Assert.Single(plan.Services, service => service.Name == "api");
        var marketing = Assert.Single(plan.Services, service => service.Name == "marketing");
        Assert.Equal("xai-api-key", api.Environment["ConnectionStrings__chat"]);
        Assert.Contains("xai-api-key", plan.Parameters);
        Assert.False(marketing.Environment.ContainsKey("ConnectionStrings__chat"));
        Assert.DoesNotContain("placeholder-openai-key", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", json, StringComparison.Ordinal);
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
