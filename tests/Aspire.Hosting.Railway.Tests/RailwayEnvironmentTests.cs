using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Railway;

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
        // Aspire WithReplicas is ProjectResource-only; it stamps ReplicaAnnotation.
        // GetReplicaCount reads that annotation on any IResource.
        var api = builder.AddContainer("api", "nginx").WithAnnotation(new ReplicaAnnotation(2));

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
        Assert.DoesNotContain("\"cpu\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("memoryGb", json, StringComparison.Ordinal);
        Assert.DoesNotContain("healthcheckPath", json, StringComparison.Ordinal);
        Assert.DoesNotContain("healthcheckTimeout", json, StringComparison.Ordinal);
        Assert.DoesNotContain("restartPolicyType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("restartPolicyMaxRetries", json, StringComparison.Ordinal);
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
        Assert.Null(service.Cpu);
        Assert.Null(service.MemoryGb);
        Assert.DoesNotContain("replicas", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("\"cpu\"", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("memoryGb", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("healthcheckPath", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("healthcheckTimeout", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("restartPolicyType", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("restartPolicyMaxRetries", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_PublishAsRailwayService_CopiesRegionServerlessAndReplicaRegions()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var api = builder.AddContainer("api", "nginx")
            .WithAnnotation(new ReplicaAnnotation(2))
            .PublishAsRailwayService(s =>
            {
                s.Region = RailwayRegion.EuropeWest4;
                s.Serverless = true;
                s.ReplicaRegions = new Dictionary<RailwayRegion, int>
                {
                    [RailwayRegion.UsWest2] = 2,
                    [RailwayRegion.EuropeWest4] = 1
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
    public void Plan_PublishAsRailwayService_CopiesCpuAndMemoryGb()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .WithAnnotation(new ReplicaAnnotation(2))
            .PublishAsRailwayService(s =>
            {
                s.Region = RailwayRegion.EuropeWest4;
                s.Cpu = 1;
                s.MemoryGb = 2;
            });

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Equal(2, service.Replicas);
        Assert.Equal("europe-west4-drams3a", service.Region);
        Assert.Equal(1, service.Cpu);
        Assert.Equal(2, service.MemoryGb);
        Assert.Contains("\"cpu\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"memoryGb\": 2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("vCPUs", json, StringComparison.Ordinal);
        Assert.DoesNotContain("memoryGB", json, StringComparison.Ordinal);
        Assert.DoesNotContain("memoryBytes", json, StringComparison.Ordinal);
        Assert.DoesNotContain("limitOverride", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(-1, 2)]
    [InlineData(double.NaN, 2)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, double.NegativeInfinity)]
    public void Plan_InvalidCpuOrMemory_FailsBeforeGraphQL(double cpu, double memoryGb)
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s =>
            {
                s.Cpu = cpu;
                s.MemoryGb = memoryGb;
            });

        using var app = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production"));

        Assert.Contains("must be greater than 0", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("24", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plan_PublishAsRailwayService_AfterPrepare_UsesDeploymentTarget()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .WithAnnotation(new ReplicaAnnotation(3))
            .PublishAsRailwayService(s =>
            {
                s.Region = RailwayRegion.UsWest2;
                s.Serverless = false;
                s.Cpu = 0.5;
                s.MemoryGb = 1;
            });

        using var app = builder.Build();
        await TestAppBuilder.ExecuteBeforeStartHooksAsync(app);

        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);

        Assert.Equal(3, service.Replicas);
        Assert.Equal("us-west2", service.Region);
        Assert.False(service.Serverless);
        Assert.Equal(0.5, service.Cpu);
        Assert.Equal(1, service.MemoryGb);
    }

    [Fact]
    public void Plan_UndefinedRailwayRegion_FailsBeforeGraphQL()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s => s.Region = (RailwayRegion)999);

        using var app = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production"));

        Assert.Contains("999", exception.Message, StringComparison.Ordinal);
        Assert.Contains("RailwayRegion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("us-west2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Region.region", exception.Message, StringComparison.Ordinal);
        Assert.Contains("docs.railway.com/deployments/regions", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-railway-region")]
    [InlineData("sjc")]
    [InlineData("iad")]
    [InlineData("ams")]
    [InlineData("sin")]
    [InlineData("us-west1")]
    [InlineData("us-east4")]
    [InlineData("europe-west4")]
    public void Plan_DeserializedAirportCodeOrOldRegionId_Fails(string regionId)
    {
        var plan = new RailwayPlan
        {
            Services =
            {
                new RailwayPlanService { Name = "api", Region = regionId }
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayServiceComputeSettings.ValidatePlanServices(plan));

        Assert.Contains(regionId, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Airport codes", exception.Message, StringComparison.Ordinal);
        Assert.Contains("us-west2", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("deprecat", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_VolumeBackedPostgresWithReplicas_FailsHonestly()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres")
            .PublishAsRailwayPostgres()
            .WithAnnotation(new ReplicaAnnotation(2));
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production"));

        Assert.Contains("volume-backed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("docs.railway.com/volumes/reference", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PublishAsRailwayPostgres", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_ManagedPostgresAndRedis_DoNotGetComputeScaleFields()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddRedis("redis").PublishAsRailwayRedis();
        builder.AddContainer("api", "nginx").WithAnnotation(new ReplicaAnnotation(2));

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");

        Assert.Single(plan.Services);
        var api = Assert.Single(plan.Services);
        Assert.Equal(2, api.Replicas);
        Assert.Null(api.Cpu);
        Assert.Null(api.MemoryGb);
        Assert.Null(api.HealthcheckPath);
        Assert.Null(api.HealthcheckTimeout);
        Assert.Null(api.RestartPolicyType);
        Assert.Null(api.RestartPolicyMaxRetries);
        Assert.Null(api.StartCommand);
        Assert.Null(api.PreDeployCommand);
        Assert.All(plan.ManagedServices, managed =>
        {
            Assert.Contains(managed.Kind, ["postgres", "redis"], StringComparer.Ordinal);
        });
        var json = RailwayPlanBuilder.ToJson(plan);
        Assert.DoesNotContain("\"cpu\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("memoryGb", json, StringComparison.Ordinal);
        Assert.DoesNotContain("healthcheckPath", json, StringComparison.Ordinal);
        Assert.DoesNotContain("healthcheckTimeout", json, StringComparison.Ordinal);
        Assert.DoesNotContain("restartPolicyType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("restartPolicyMaxRetries", json, StringComparison.Ordinal);
        Assert.DoesNotContain("startCommand", json, StringComparison.Ordinal);
        Assert.DoesNotContain("preDeployCommand", json, StringComparison.Ordinal);
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

    [Fact]
    public void Plan_WithHttpHealthCheck_CopiesPathAndOmitsTimeout()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .WithHttpEndpoint(targetPort: 80)
            .WithHttpHealthCheck("/health");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Equal("/health", service.HealthcheckPath);
        Assert.Null(service.HealthcheckTimeout);
        Assert.Contains("\"healthcheckPath\": \"/health\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("healthcheckTimeout", json, StringComparison.Ordinal);
        Assert.DoesNotContain("RAILWAY_HEALTHCHECK_TIMEOUT_SEC", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WithHttpHealthCheckAndTimeout_CopiesBoth()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .WithHttpEndpoint(targetPort: 80)
            .WithHttpHealthCheck("/health")
            .PublishAsRailwayService(s => s.HealthcheckTimeoutSeconds = 120);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Equal("/health", service.HealthcheckPath);
        Assert.Equal(120, service.HealthcheckTimeout);
        Assert.Contains("\"healthcheckPath\": \"/health\"", json, StringComparison.Ordinal);
        Assert.Contains("\"healthcheckTimeout\": 120", json, StringComparison.Ordinal);
        Assert.DoesNotContain("HealthcheckTimeoutSeconds", json, StringComparison.Ordinal);
        Assert.DoesNotContain("RAILWAY_HEALTHCHECK_TIMEOUT_SEC", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WithHttpHealthCheckNon200Status_CopiesPathAndIgnoresStatus()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .WithHttpEndpoint(targetPort: 80)
            .WithHttpHealthCheck("/health", statusCode: 204);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);

        Assert.Equal("/health", service.HealthcheckPath);
        Assert.Null(service.HealthcheckTimeout);
    }

    [Fact]
    public void Plan_CustomHealthCheck_DoesNotBecomeHealthcheckPath()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .WithAnnotation(new HealthCheckAnnotation("my-custom-check"));

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);

        Assert.Null(service.HealthcheckPath);
        Assert.Null(service.HealthcheckTimeout);
        Assert.DoesNotContain("healthcheckPath", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Plan_InvalidHealthcheckTimeout_FailsBeforeGraphQL(int timeout)
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s => s.HealthcheckTimeoutSeconds = timeout);

        using var app = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production"));

        Assert.Contains("healthcheckTimeout", exception.Message, StringComparison.Ordinal);
        Assert.Contains("greater than 0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_ManagedPostgresRedisAndBucket_DoNotGetHealthcheckFields()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddRedis("redis").PublishAsRailwayRedis();
        builder.AddRailwayBucket("uploads");
        builder.AddContainer("api", "nginx")
            .WithHttpEndpoint(targetPort: 80)
            .WithHttpHealthCheck("/health")
            .PublishAsRailwayService(s => s.HealthcheckTimeoutSeconds = 120);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");

        Assert.Single(plan.Services);
        var api = Assert.Single(plan.Services);
        Assert.Equal("/health", api.HealthcheckPath);
        Assert.Equal(120, api.HealthcheckTimeout);
        Assert.Contains(plan.ManagedServices, managed => managed.Kind == "postgres");
        Assert.Contains(plan.ManagedServices, managed => managed.Kind == "redis");
        Assert.Contains(plan.ManagedServices, managed => managed.Kind == "bucket");
        Assert.All(plan.ManagedServices, managed =>
        {
            Assert.DoesNotContain(
                "healthcheckPath",
                System.Text.Json.JsonSerializer.Serialize(managed),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "healthcheckTimeout",
                System.Text.Json.JsonSerializer.Serialize(managed),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "restartPolicyType",
                System.Text.Json.JsonSerializer.Serialize(managed),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "restartPolicyMaxRetries",
                System.Text.Json.JsonSerializer.Serialize(managed),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "startCommand",
                System.Text.Json.JsonSerializer.Serialize(managed),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "preDeployCommand",
                System.Text.Json.JsonSerializer.Serialize(managed),
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Plan_UnsetRestartPolicy_OmitsBothFields()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Null(service.RestartPolicyType);
        Assert.Null(service.RestartPolicyMaxRetries);
        Assert.DoesNotContain("restartPolicyType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("restartPolicyMaxRetries", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_PublishAsRailwayService_CopiesRestartPolicyAndMaxRetries()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s =>
            {
                s.RestartPolicy = RailwayRestartPolicy.OnFailure;
                s.RestartPolicyMaxRetries = 10;
            });

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Equal("ON_FAILURE", service.RestartPolicyType);
        Assert.Equal(10, service.RestartPolicyMaxRetries);
        Assert.Contains("\"restartPolicyType\": \"ON_FAILURE\"", json, StringComparison.Ordinal);
        Assert.Contains("\"restartPolicyMaxRetries\": 10", json, StringComparison.Ordinal);
        Assert.DoesNotContain("OnFailure", json, StringComparison.Ordinal);
        Assert.DoesNotContain("RestartPolicyMaxRetries", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RailwayRestartPolicy.OnFailure, "ON_FAILURE")]
    [InlineData(RailwayRestartPolicy.Always, "ALWAYS")]
    [InlineData(RailwayRestartPolicy.Never, "NEVER")]
    public void Plan_RestartPolicy_MapsEnumToGraphQLString(RailwayRestartPolicy policy, string expected)
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s => s.RestartPolicy = policy);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Equal(expected, service.RestartPolicyType);
        Assert.Null(service.RestartPolicyMaxRetries);
        Assert.Contains($"\"restartPolicyType\": \"{expected}\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("restartPolicyMaxRetries", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_RestartPolicyMaxRetriesOnly_OmitsType()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s => s.RestartPolicyMaxRetries = 3);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Null(service.RestartPolicyType);
        Assert.Equal(3, service.RestartPolicyMaxRetries);
        Assert.DoesNotContain("restartPolicyType", json, StringComparison.Ordinal);
        Assert.Contains("\"restartPolicyMaxRetries\": 3", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Plan_InvalidRestartPolicyMaxRetries_FailsBeforeGraphQL(int retries)
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s => s.RestartPolicyMaxRetries = retries);

        using var app = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production"));

        Assert.Contains("restartPolicyMaxRetries", exception.Message, StringComparison.Ordinal);
        Assert.Contains("greater than 0", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("10", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_UndefinedRailwayRestartPolicy_FailsBeforeGraphQL()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s => s.RestartPolicy = (RailwayRestartPolicy)999);

        using var app = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production"));

        Assert.Contains("999", exception.Message, StringComparison.Ordinal);
        Assert.Contains("RailwayRestartPolicy", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ON_FAILURE", exception.Message, StringComparison.Ordinal);
        Assert.Contains("docs.railway.com/deployments/restart-policy", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("on_failure")]
    [InlineData("OnFailure")]
    [InlineData("not-a-restart-policy")]
    public void Plan_DeserializedUnknownRestartPolicyType_Fails(string type)
    {
        var plan = new RailwayPlan
        {
            Services =
            {
                new RailwayPlanService { Name = "api", RestartPolicyType = type }
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayServiceComputeSettings.ValidatePlanServices(plan));

        Assert.Contains(type, exception.Message, StringComparison.Ordinal);
        Assert.Contains("ON_FAILURE", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_ManagedPostgresRedisAndBucket_DoNotGetRestartPolicyFields()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddRedis("redis").PublishAsRailwayRedis();
        builder.AddRailwayBucket("uploads");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s =>
            {
                s.RestartPolicy = RailwayRestartPolicy.Always;
                s.RestartPolicyMaxRetries = 5;
            });

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");

        Assert.Single(plan.Services);
        var api = Assert.Single(plan.Services);
        Assert.Equal("ALWAYS", api.RestartPolicyType);
        Assert.Equal(5, api.RestartPolicyMaxRetries);
        Assert.Contains(plan.ManagedServices, managed => managed.Kind == "postgres");
        Assert.Contains(plan.ManagedServices, managed => managed.Kind == "redis");
        Assert.Contains(plan.ManagedServices, managed => managed.Kind == "bucket");
        Assert.All(plan.ManagedServices, managed =>
        {
            Assert.DoesNotContain(
                "restartPolicyType",
                System.Text.Json.JsonSerializer.Serialize(managed),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "restartPolicyMaxRetries",
                System.Text.Json.JsonSerializer.Serialize(managed),
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Plan_UnsetStartAndPreDeployCommand_OmitsBothFields()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Null(service.StartCommand);
        Assert.Null(service.PreDeployCommand);
        Assert.DoesNotContain("startCommand", json, StringComparison.Ordinal);
        Assert.DoesNotContain("preDeployCommand", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_PublishAsRailwayService_CopiesStartCommandAndPreDeployAsArray()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s =>
            {
                s.StartCommand = "/bin/sh -c \"exec dotnet MyApp.dll --urls http://*:$PORT\"";
                s.PreDeployCommand = "dotnet MyApp.dll --migrate";
            });

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Equal(
            "/bin/sh -c \"exec dotnet MyApp.dll --urls http://*:$PORT\"",
            service.StartCommand);
        Assert.Equal(["dotnet MyApp.dll --migrate"], service.PreDeployCommand);
        using (var document = System.Text.Json.JsonDocument.Parse(json))
        {
            var planned = document.RootElement.GetProperty("services")[0];
            Assert.Equal(
                "/bin/sh -c \"exec dotnet MyApp.dll --urls http://*:$PORT\"",
                planned.GetProperty("startCommand").GetString());
            Assert.Equal(1, planned.GetProperty("preDeployCommand").GetArrayLength());
            Assert.Equal(
                "dotnet MyApp.dll --migrate",
                planned.GetProperty("preDeployCommand")[0].GetString());
        }

        Assert.Contains("\"startCommand\":", json, StringComparison.Ordinal);
        Assert.Contains("\"preDeployCommand\": [", json, StringComparison.Ordinal);
        Assert.DoesNotContain("StartCommand", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PreDeployCommand", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_StartCommandOnly_OmitsPreDeploy()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s => s.StartCommand = "./api");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Equal("./api", service.StartCommand);
        Assert.Null(service.PreDeployCommand);
        Assert.Contains("\"startCommand\": \"./api\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("preDeployCommand", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_PreDeployCommandOnly_OmitsStart()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s => s.PreDeployCommand = "dotnet MyApp.dll --migrate");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Null(service.StartCommand);
        Assert.Equal(["dotnet MyApp.dll --migrate"], service.PreDeployCommand);
        Assert.DoesNotContain("startCommand", json, StringComparison.Ordinal);
        Assert.Contains("\"preDeployCommand\": [", json, StringComparison.Ordinal);
        Assert.Contains("\"dotnet MyApp.dll --migrate\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Plan_EmptyStartCommand_FailsBeforeGraphQL(string startCommand)
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s => s.StartCommand = startCommand);

        using var app = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production"));

        Assert.Contains("startCommand", exception.Message, StringComparison.Ordinal);
        Assert.Contains("non-empty", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Plan_EmptyPreDeployCommand_FailsBeforeGraphQL(string preDeployCommand)
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s => s.PreDeployCommand = preDeployCommand);

        using var app = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production"));

        Assert.Contains("preDeployCommand", exception.Message, StringComparison.Ordinal);
        Assert.Contains("non-empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WithArgs_DoesNotBecomeStartCommand()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx").WithArgs("--urls", "http://*:8080");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Null(service.StartCommand);
        Assert.Null(service.PreDeployCommand);
        Assert.DoesNotContain("startCommand", json, StringComparison.Ordinal);
        Assert.DoesNotContain("--urls", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_HealthcheckRestartAndStartSurviveTogether()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .WithHttpEndpoint(targetPort: 80)
            .WithHttpHealthCheck("/health")
            .PublishAsRailwayService(s =>
            {
                s.HealthcheckTimeoutSeconds = 90;
                s.RestartPolicy = RailwayRestartPolicy.Never;
                s.RestartPolicyMaxRetries = 1;
                s.StartCommand = "./api";
                s.PreDeployCommand = "dotnet MyApp.dll --migrate";
            });

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);

        Assert.Equal("/health", service.HealthcheckPath);
        Assert.Equal(90, service.HealthcheckTimeout);
        Assert.Equal("NEVER", service.RestartPolicyType);
        Assert.Equal(1, service.RestartPolicyMaxRetries);
        Assert.Equal("./api", service.StartCommand);
        Assert.Equal(["dotnet MyApp.dll --migrate"], service.PreDeployCommand);
    }

    [Fact]
    public void Plan_ManagedPostgresRedisAndBucket_DoNotGetStartOrPreDeployFields()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddRedis("redis").PublishAsRailwayRedis();
        builder.AddRailwayBucket("uploads");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s =>
            {
                s.StartCommand = "./api";
                s.PreDeployCommand = "dotnet MyApp.dll --migrate";
            });

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");

        Assert.Single(plan.Services);
        var api = Assert.Single(plan.Services);
        Assert.Equal("./api", api.StartCommand);
        Assert.Equal(["dotnet MyApp.dll --migrate"], api.PreDeployCommand);
        Assert.Contains(plan.ManagedServices, managed => managed.Kind == "postgres");
        Assert.Contains(plan.ManagedServices, managed => managed.Kind == "redis");
        Assert.Contains(plan.ManagedServices, managed => managed.Kind == "bucket");
        Assert.All(plan.ManagedServices, managed =>
        {
            Assert.DoesNotContain(
                "startCommand",
                System.Text.Json.JsonSerializer.Serialize(managed),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "preDeployCommand",
                System.Text.Json.JsonSerializer.Serialize(managed),
                StringComparison.Ordinal);
        });
    }
}
