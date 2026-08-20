using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Railway;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aspire.Hosting.Railway.Tests;

public sealed class RailwayCustomDomainTests
{
    [Fact]
    public void Plan_PublishAsRailwayService_CopiesCustomDomainsAndOmitsTokens()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .WithHttpEndpoint(targetPort: 8080)
            .WithExternalHttpEndpoints()
            .PublishAsRailwayService(s =>
            {
                s.CustomDomains.Add("API.Example.com");
                s.CustomDomains.Add("www.example.com");
            });

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Equal(["API.Example.com", "www.example.com"], service.CustomDomains);
        Assert.Equal(8080, service.TargetPort);
        Assert.Equal("API.Example.com", service.CustomDomains![0]);
        using (var document = System.Text.Json.JsonDocument.Parse(json))
        {
            var planned = document.RootElement.GetProperty("services")[0];
            Assert.Equal("API.Example.com", planned.GetProperty("customDomains")[0].GetString());
            Assert.Equal("www.example.com", planned.GetProperty("customDomains")[1].GetString());
            Assert.Equal(8080, planned.GetProperty("targetPort").GetInt32());
        }

        Assert.DoesNotContain("verificationToken", json, StringComparison.Ordinal);
        Assert.DoesNotContain("verify-", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomDomains", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WithoutCustomDomains_OmitsField()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .WithHttpEndpoint(targetPort: 80)
            .WithExternalHttpEndpoints();

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var service = Assert.Single(plan.Services);
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Null(service.CustomDomains);
        Assert.Equal(80, service.TargetPort);
        Assert.DoesNotContain("customDomains", json, StringComparison.Ordinal);
        Assert.DoesNotContain("verificationToken", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Plan_EmptyCustomDomain_FailsBeforeGraphQL(string hostname)
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s => s.CustomDomains.Add(hostname));

        using var app = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production"));

        Assert.Contains("customDomains", exception.Message, StringComparison.Ordinal);
        Assert.Contains("non-empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_DuplicateCustomDomains_FailsBeforeGraphQL()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s =>
            {
                s.CustomDomains.Add("api.example.com");
                s.CustomDomains.Add("API.example.com");
            });

        using var app = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production"));

        Assert.Contains("customDomains", exception.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("API.example.com", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_ManagedPostgresRedisAndBucket_DoNotGetCustomDomains()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddRedis("redis").PublishAsRailwayRedis();
        builder.AddRailwayBucket("uploads");
        builder.AddContainer("api", "nginx")
            .WithExternalHttpEndpoints()
            .PublishAsRailwayService(s => s.CustomDomains.Add("api.example.com"));

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");

        var api = Assert.Single(plan.Services);
        Assert.Equal(["api.example.com"], api.CustomDomains);
        Assert.Contains(plan.ManagedServices, managed => managed.Kind == "postgres");
        Assert.Contains(plan.ManagedServices, managed => managed.Kind == "redis");
        Assert.Contains(plan.ManagedServices, managed => managed.Kind == "bucket");
        Assert.All(plan.ManagedServices, managed =>
        {
            var json = System.Text.Json.JsonSerializer.Serialize(managed);
            Assert.DoesNotContain("customDomains", json, StringComparison.Ordinal);
            Assert.DoesNotContain("verificationToken", json, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ValidatePlan_ManagedServiceCustomDomains_Fails()
    {
        var plan = GraphQLFixtures.CreatePlan(includePostgres: true);
        plan.Services[0].Name = "postgres";
        plan.Services[0].CustomDomains = ["db.example.com"];

        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayServiceComputeSettings.ValidatePlanServices(plan));

        Assert.Contains("customDomains", exception.Message, StringComparison.Ordinal);
        Assert.Contains("postgres", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_ExternalHttp_CreatesServiceDomainAndCustomDomain()
    {
        var handler = new ScriptedGraphQLHandler();
        EnqueueCreateServiceWithDomains(handler, includeCustomDomain: true);

        var reporter = new RecordingReportingStep();
        var state = new MemoryDeploymentStateManager();
        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].CustomDomains = ["api.example.com"];
        plan.Services[0].TargetPort = 8080;
        var request = GraphQLFixtures.CreateRequest();
        request.ExternalHttpServices.Add("api");

        var result = await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            request,
            reporter,
            state);

        Assert.Equal(1, handler.Count("serviceDomainCreate"));
        Assert.Equal(1, handler.Count("domains"));
        Assert.Equal(1, handler.Count("customDomainAvailable"));
        Assert.Equal(1, handler.Count("customDomainCreate"));
        Assert.Equal(0, handler.Count("customDomain"));
        Assert.Equal(0, handler.Count("customDomainUpdate"));
        Assert.Equal(0, handler.Count("customDomainDelete"));
        Assert.Equal(GraphQLFixtures.CustomDomainId, result.CustomDomainIds["api.example.com"]);

        var serviceDomain = GraphQLFixtures.GetServiceDomainCreateInput(handler.Bodies);
        Assert.Equal(GraphQLFixtures.ApiServiceId, serviceDomain.GetProperty("serviceId").GetString());
        Assert.Equal(GraphQLFixtures.ProductionEnvironmentId, serviceDomain.GetProperty("environmentId").GetString());
        Assert.Equal(8080, serviceDomain.GetProperty("targetPort").GetInt32());
        Assert.False(serviceDomain.TryGetProperty("domain", out _));

        var create = GraphQLFixtures.GetCustomDomainCreateInput(handler.Bodies);
        Assert.Equal("api.example.com", create.GetProperty("domain").GetString());
        Assert.Equal(GraphQLFixtures.ProductionEnvironmentId, create.GetProperty("environmentId").GetString());
        Assert.Equal(GraphQLFixtures.ProjectId, create.GetProperty("projectId").GetString());
        Assert.Equal(GraphQLFixtures.ApiServiceId, create.GetProperty("serviceId").GetString());
        Assert.Equal(8080, create.GetProperty("targetPort").GetInt32());
        Assert.False(create.TryGetProperty("verificationToken", out _));

        var createBody = handler.Bodies.Single(body =>
            body.Contains("\"operationName\":\"customDomainCreate\"", StringComparison.Ordinal));
        Assert.Contains("CustomDomainCreateInput", createBody, StringComparison.Ordinal);
        Assert.DoesNotContain("null", createBody, StringComparison.Ordinal);
        Assert.DoesNotContain("customDomainDelete", createBody, StringComparison.Ordinal);
        Assert.DoesNotContain("pluginCreate", createBody, StringComparison.Ordinal);
        Assert.DoesNotContain("customDomainIssueCertificate", createBody, StringComparison.Ordinal);

        Assert.Contains(
            reporter.Completions,
            text => text.Contains("api.example.com", StringComparison.Ordinal) &&
                    text.Contains("DNS_RECORD_TYPE_CNAME", StringComparison.Ordinal) &&
                    text.Contains("verify-placeholder", StringComparison.Ordinal) &&
                    text.Contains("_railway.example.com", StringComparison.Ordinal) &&
                    text.Contains("CERTIFICATE_STATUS_TYPE_VALIDATING_OWNERSHIP", StringComparison.Ordinal));

        var snapshot = await RailwayDeploymentStateStore.LoadAsync(state, "railway", "production", CancellationToken.None);
        Assert.Equal(GraphQLFixtures.CustomDomainId, snapshot.CustomDomainIds["api.example.com"]);
        Assert.DoesNotContain("verify-placeholder", snapshot.CustomDomainIds.Values);
        Assert.DoesNotContain(
            handler.Operations,
            name => name.Contains("Delete", StringComparison.Ordinal) ||
                    name.Contains("pluginCreate", StringComparison.Ordinal) ||
                    name.Contains("IssueCertificate", StringComparison.Ordinal) ||
                    name.Contains("trustedDomain", StringComparison.Ordinal) ||
                    name.Contains("railwayDomain", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_ExistingCustomDomain_AdoptsAndSkipsCreate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceDomainCreate", GraphQLFixtures.ServiceDomainCreate);
        handler.Enqueue("domains", GraphQLFixtures.DomainsWithCustom);
        handler.Enqueue("customDomain", GraphQLFixtures.CustomDomainQuery);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].CustomDomains = ["API.Example.com"];
        plan.Services[0].TargetPort = 8080;
        var request = GraphQLFixtures.CreateRequest();
        request.ExternalHttpServices.Add("api");

        var result = await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            request,
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(1, handler.Count("domains"));
        Assert.Equal(1, handler.Count("customDomain"));
        Assert.Equal(0, handler.Count("customDomainAvailable"));
        Assert.Equal(0, handler.Count("customDomainCreate"));
        Assert.Equal(0, handler.Count("customDomainUpdate"));
        Assert.Equal(0, handler.Count("customDomainDelete"));
        Assert.Equal(GraphQLFixtures.CustomDomainId, result.CustomDomainIds["API.Example.com"]);

        var queryBody = handler.Bodies.Single(body =>
            body.Contains("\"operationName\":\"customDomain\"", StringComparison.Ordinal));
        Assert.Contains(GraphQLFixtures.CustomDomainId, queryBody, StringComparison.Ordinal);
        Assert.Contains(GraphQLFixtures.ProjectId, queryBody, StringComparison.Ordinal);
        Assert.DoesNotContain("null", queryBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_AdoptedDomainTargetPortChange_CallsCustomDomainUpdate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceDomainCreate", GraphQLFixtures.ServiceDomainCreate);
        handler.Enqueue("domains", GraphQLFixtures.DomainsWithCustom);
        handler.Enqueue("customDomainUpdate", GraphQLFixtures.CustomDomainUpdate);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].CustomDomains = ["api.example.com"];
        plan.Services[0].TargetPort = 80;
        var request = GraphQLFixtures.CreateRequest();
        request.ExternalHttpServices.Add("api");

        await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            request,
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(1, handler.Count("customDomainUpdate"));
        Assert.Equal(0, handler.Count("customDomainCreate"));
        var updateBody = handler.Bodies.Single(body =>
            body.Contains("\"operationName\":\"customDomainUpdate\"", StringComparison.Ordinal));
        Assert.Contains("\"environmentId\":\"env_production_placeholder\"", updateBody, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"cdom_placeholder\"", updateBody, StringComparison.Ordinal);
        Assert.Contains("\"targetPort\":80", updateBody, StringComparison.Ordinal);
        Assert.DoesNotContain("null", updateBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_CustomDomainsWithoutExternalHttp_DoesNotCallCustomDomainCreate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].CustomDomains = ["api.example.com"];

        await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(0, handler.Count("serviceDomainCreate"));
        Assert.Equal(0, handler.Count("domains"));
        Assert.Equal(0, handler.Count("customDomainAvailable"));
        Assert.Equal(0, handler.Count("customDomainCreate"));
        Assert.Equal(0, handler.Count("customDomainDelete"));
    }

    [Fact]
    public async Task Apply_UnavailableCustomDomain_FailsHonestly()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceDomainCreate", GraphQLFixtures.ServiceDomainCreate);
        handler.Enqueue("domains", GraphQLFixtures.DomainsEmpty);
        handler.Enqueue("customDomainAvailable", GraphQLFixtures.CustomDomainAvailableFalse);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].CustomDomains = ["taken.example.com"];
        var request = GraphQLFixtures.CreateRequest();
        request.ExternalHttpServices.Add("api");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
                plan,
                request,
                new RecordingReportingStep(),
                new MemoryDeploymentStateManager()));

        Assert.Contains("taken.example.com", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not available", exception.Message, StringComparison.Ordinal);
        Assert.Contains("already in use", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Count("customDomainCreate"));
        Assert.Equal(0, handler.Count("customDomainDelete"));
    }

    [Fact]
    public async Task Apply_TemplatesAndBucket_DoNotCallCustomDomainOperations()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("template", GraphQLFixtures.TemplatePostgres);
        handler.Enqueue("templateDeployV2", GraphQLFixtures.TemplateDeployV2);
        handler.Enqueue("workflowStatus", GraphQLFixtures.WorkflowComplete);
        handler.Enqueue("template", GraphQLFixtures.TemplateRedis);
        handler.Enqueue("templateDeployV2", GraphQLFixtures.TemplateDeployV2);
        handler.Enqueue("workflowStatus", GraphQLFixtures.WorkflowComplete);
        handler.Enqueue("bucketCreate", GraphQLFixtures.BucketCreate);
        handler.Enqueue("bucketS3Credentials", GraphQLFixtures.BucketCredentials);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateUploads);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            GraphQLFixtures.CreatePlan(includePostgres: true, includeRedis: true, includeBucket: true),
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(0, handler.Count("customDomainCreate"));
        Assert.Equal(0, handler.Count("customDomainDelete"));
        Assert.Equal(0, handler.Count("domains"));
        Assert.Equal(0, handler.Count("customDomainAvailable"));
        Assert.DoesNotContain(
            handler.Operations,
            name => name.Contains("customDomain", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeployAsync_WithExternalHttpEndpoints_CreatesServiceDomainAndCustomHostname()
    {
        var handler = new ScriptedGraphQLHandler();
        EnqueueCreateServiceWithDomains(handler, includeCustomDomain: true);

        var builder = TestAppBuilder.CreatePublish();
        builder.Configuration["RAILWAY_TOKEN"] = GraphQLFixtures.Token;
        var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io");
        var railway = builder.AddRailwayEnvironment("railway").WithContainerRegistry(ghcr);
        builder.AddContainer("api", "nginx")
            .WithHttpEndpoint(targetPort: 8080)
            .WithExternalHttpEndpoints()
            .PublishAsRailwayService(s => s.CustomDomains.Add("api.example.com"));

        using var app = builder.Build();
        await TestAppBuilder.ExecuteBeforeStartHooksAsync(app);

        var services = new ServiceCollection();
        services.AddSingleton(app.Services.GetRequiredService<DistributedApplicationModel>());
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment());
        services.AddSingleton<IDeploymentStateManager>(new MemoryDeploymentStateManager());
        services.AddSingleton(new RailwayGraphQLClient(new HttpClient(handler)));
        var provider = services.BuildServiceProvider();

        var context = CreatePipelineContext(TestAppBuilder.GetModel(app), provider);
        await railway.Resource.DeployAsync(context);

        Assert.Equal(1, handler.Count("serviceDomainCreate"));
        Assert.Equal(1, handler.Count("customDomainCreate"));
        Assert.Equal(0, handler.Count("customDomainDelete"));

        var serviceDomain = GraphQLFixtures.GetServiceDomainCreateInput(handler.Bodies);
        Assert.Equal(8080, serviceDomain.GetProperty("targetPort").GetInt32());
        var create = GraphQLFixtures.GetCustomDomainCreateInput(handler.Bodies);
        Assert.Equal("api.example.com", create.GetProperty("domain").GetString());
        Assert.Equal(8080, create.GetProperty("targetPort").GetInt32());
        Assert.DoesNotContain("null", handler.Bodies.Single(body =>
            body.Contains("\"operationName\":\"customDomainCreate\"", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            handler.Operations,
            name => string.Equals(name, "customDomainDelete", StringComparison.Ordinal) ||
                    string.Equals(name, "pluginCreate", StringComparison.Ordinal) ||
                    string.Equals(name, "customDomainIssueCertificate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeployAsync_WithoutExternalHttp_DoesNotCreateCustomDomain()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var builder = TestAppBuilder.CreatePublish();
        builder.Configuration["RAILWAY_TOKEN"] = GraphQLFixtures.Token;
        var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io");
        var railway = builder.AddRailwayEnvironment("railway").WithContainerRegistry(ghcr);
        builder.AddContainer("api", "nginx")
            .PublishAsRailwayService(s => s.CustomDomains.Add("api.example.com"));

        using var app = builder.Build();
        await TestAppBuilder.ExecuteBeforeStartHooksAsync(app);

        var services = new ServiceCollection();
        services.AddSingleton(app.Services.GetRequiredService<DistributedApplicationModel>());
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment());
        services.AddSingleton<IDeploymentStateManager>(new MemoryDeploymentStateManager());
        services.AddSingleton(new RailwayGraphQLClient(new HttpClient(handler)));
        var provider = services.BuildServiceProvider();

        var context = CreatePipelineContext(TestAppBuilder.GetModel(app), provider);
        await railway.Resource.DeployAsync(context);

        Assert.Equal(0, handler.Count("serviceDomainCreate"));
        Assert.Equal(0, handler.Count("customDomainCreate"));
        Assert.Equal(0, handler.Count("customDomainDelete"));
    }

    [Fact]
    public async Task Save_ThenFlattenUnflatten_KeepsCustomDomainIdsWithoutTokens()
    {
        var state = new MemoryDeploymentStateManager();
        var result = new RailwayApplyResult
        {
            ProjectId = GraphQLFixtures.ProjectId,
            EnvironmentId = GraphQLFixtures.ProductionEnvironmentId,
            ProductionEnvironmentId = GraphQLFixtures.ProductionEnvironmentId
        };
        result.CustomDomainIds["api.example.com"] = GraphQLFixtures.CustomDomainId;

        await RailwayDeploymentStateStore.SaveAsync(
            state,
            "railway",
            "production",
            result,
            CancellationToken.None);

        await RailwayDeploymentStateStoreTests.FlattenUnflattenSectionAsync(state, "Railway:railway");

        var snapshot = await RailwayDeploymentStateStore.LoadAsync(
            state,
            "railway",
            "production",
            CancellationToken.None);

        Assert.Equal(GraphQLFixtures.CustomDomainId, snapshot.CustomDomainIds["api.example.com"]);
        var section = await state.AcquireSectionAsync("Railway:railway");
        var scoped = Assert.IsType<System.Text.Json.Nodes.JsonObject>(
            section.Data[RailwayDeploymentStateStore.CustomDomainsKey]?["production"]);
        Assert.IsNotType<System.Text.Json.Nodes.JsonArray>(
            section.Data[RailwayDeploymentStateStore.CustomDomainsKey]?["production"]);
        Assert.Equal(GraphQLFixtures.CustomDomainId, scoped["api.example.com"]?.GetValue<string>());
        Assert.DoesNotContain("verify", scoped.ToJsonString(), StringComparison.Ordinal);
    }

    private static void EnqueueCreateServiceWithDomains(ScriptedGraphQLHandler handler, bool includeCustomDomain)
    {
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceDomainCreate", GraphQLFixtures.ServiceDomainCreate);
        if (includeCustomDomain)
        {
            handler.Enqueue("domains", GraphQLFixtures.DomainsEmpty);
            handler.Enqueue("customDomainAvailable", GraphQLFixtures.CustomDomainAvailableTrue);
            handler.Enqueue("customDomainCreate", GraphQLFixtures.CustomDomainCreate);
        }

        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);
    }

    private static PipelineStepContext CreatePipelineContext(DistributedApplicationModel model, IServiceProvider services)
    {
        var pipeline = new PipelineContext(
            model,
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
            services,
            NullLogger.Instance,
            CancellationToken.None);

        return new PipelineStepContext
        {
            PipelineContext = pipeline,
            ReportingStep = new RecordingReportingStep()
        };
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
