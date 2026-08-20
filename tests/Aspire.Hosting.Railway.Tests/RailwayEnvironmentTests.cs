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
        var secret = builder.AddParameter("billing-secret-key", "test-placeholder-value", secret: true);
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
        Assert.DoesNotContain("test-placeholder-value", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
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
    public void Plan_KeepsLiteralEnvironmentValuesAndSkipsUnknownManifestParameters()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .WithEnvironment("Storage__RequireBucket", "true")
            .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY", "in_memory")
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"] =
                    new ManifestExpression("{in_memory.value}");
            });

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var api = Assert.Single(plan.Services, service => service.Name == "api");

        Assert.Equal("true", api.Environment["Storage__RequireBucket"]);
        Assert.Equal("in_memory", api.Environment["OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY"]);
        Assert.False(api.Environment.ContainsKey("OTEL_EXPORTER_OTLP_ENDPOINT"));
        Assert.DoesNotContain("in_memory", plan.Parameters);
    }

    [Fact]
    public void Coalesce_UsesLiteralsWhenTheValueIsNotACapturedParameter()
    {
        Assert.Equal(
            "in_memory",
            RailwayPlanBuilder.CoalesceCapturedEnvironmentValue("in_memory", valueRead: false, null, []));
        Assert.Equal(
            "true",
            RailwayPlanBuilder.CoalesceCapturedEnvironmentValue("true", valueRead: false, null, ["billing-secret-key"]));
        Assert.Equal(
            "resolved-secret",
            RailwayPlanBuilder.CoalesceCapturedEnvironmentValue(
                "billing-secret-key",
                valueRead: true,
                "resolved-secret",
                ["billing-secret-key"]));
        Assert.Equal(
            "",
            RailwayPlanBuilder.CoalesceCapturedEnvironmentValue(
                "stripe-webhook-secret",
                valueRead: true,
                "",
                ["stripe-webhook-secret"]));
        Assert.Null(
            RailwayPlanBuilder.CoalesceCapturedEnvironmentValue(
                "billing-secret-key",
                valueRead: false,
                null,
                ["billing-secret-key"]));
    }

    private sealed class ManifestExpression(string value) : IManifestExpressionProvider
    {
        public string ValueExpression { get; } = value;
    }

    [Fact]
    public void Plan_WithReplicas_CopiesGetReplicaCountWithoutPublishAsRailwayService()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var api = builder.AddContainer("api", "nginx").WithReplicas(2);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Equal(2, api.Resource.GetReplicaCount());
        Assert.Equal(api.Resource.GetReplicaCount(), service.Replicas);
        Assert.Null(service.Region);
        Assert.Null(service.Serverless);
        Assert.Null(service.ReplicaRegions);
        Assert.Contains("\"replicas\": 2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("replicaRegions", json, StringComparison.Ordinal);
        Assert.DoesNotContain("serverless", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"region\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WithoutReplicaAnnotation_OmitsReplicas()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var api = builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);

        Assert.Equal(1, api.Resource.GetReplicaCount());
        Assert.Null(service.Replicas);
        Assert.DoesNotContain("replicas", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_PublishAsRailwayService_CopiesRegionServerlessAndReplicaRegions()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var api = builder.AddContainer("api", "nginx")
            .WithReplicas(2)
            .PublishAsRailwayService(s =>
            {
                s.Region = "europe-west4-drams3a";
                s.Serverless = true;
                s.ReplicaRegions = new Dictionary<string, int>
                {
                    ["us-west2"] = 2,
                    ["europe-west4-drams3a"] = 1
                };
            });

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Equal(2, api.Resource.GetReplicaCount());
        Assert.Equal(api.Resource.GetReplicaCount(), service.Replicas);
        Assert.Equal("europe-west4-drams3a", service.Region);
        Assert.True(service.Serverless);
        Assert.NotNull(service.ReplicaRegions);
        Assert.Equal(2, service.ReplicaRegions["us-west2"]);
        Assert.Equal(1, service.ReplicaRegions["europe-west4-drams3a"]);
        Assert.Contains("europe-west4-drams3a", json, StringComparison.Ordinal);
        Assert.Contains("\"serverless\": true", json, StringComparison.Ordinal);
        Assert.Contains("replicaRegions", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sleepApplication", json, StringComparison.Ordinal);
        Assert.DoesNotContain("multiRegionConfig", json, StringComparison.Ordinal);
        Assert.DoesNotContain("numReplicas", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plan_PublishAsRailwayService_AfterPrepare_UsesDeploymentTarget()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .WithReplicas(3)
            .PublishAsRailwayService(s =>
            {
                s.Region = "us-west2";
                s.Serverless = false;
            });

        using var app = builder.Build();
        await TestAppBuilder.ExecuteBeforeStartHooksAsync(app);

        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);

        Assert.Equal(3, service.Replicas);
        Assert.Equal("us-west2", service.Region);
        Assert.False(service.Serverless);
    }

    [Fact]
    public void Plan_UnknownRegion_FailsBeforeGraphQL()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s => s.Region = "not-a-railway-region");

        using var app = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production"));

        Assert.Contains("not-a-railway-region", exception.Message, StringComparison.Ordinal);
        Assert.Contains("us-west2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("docs.railway.com/deployments/regions", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_ManagedPostgresAndRedis_DoNotGetComputeScaleFields()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddRedis("redis").PublishAsRailwayRedis();
        builder.AddContainer("api", "nginx").WithReplicas(2);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");

        Assert.Single(plan.Services);
        Assert.Equal(2, Assert.Single(plan.Services).Replicas);
        Assert.All(plan.ManagedServices, managed =>
        {
            Assert.Contains(managed.Kind, ["postgres", "redis"], StringComparer.Ordinal);
        });
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
