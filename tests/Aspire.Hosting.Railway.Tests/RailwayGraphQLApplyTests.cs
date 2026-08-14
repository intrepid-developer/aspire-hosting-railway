using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aspire.Hosting.Railway.Tests;

public class RailwayGraphQLApplyTests
{
    [Fact]
    public async Task Apply_CreatesProjectAndUsesProductionEnvironmentFromProjectCreate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var state = new MemoryDeploymentStateManager();
        var reporter = new RecordingReportingStep();
        var apply = GraphQLFixtures.CreateApplyService(handler);

        var result = await apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(),
            GraphQLFixtures.CreateRequest(),
            reporter,
            state);

        Assert.Equal(GraphQLFixtures.ProjectId, result.ProjectId);
        Assert.Equal(GraphQLFixtures.ProductionEnvironmentId, result.EnvironmentId);
        Assert.True(result.CreatedProject);
        Assert.False(result.CreatedEnvironment);
        Assert.Equal(1, handler.Count("projectCreate"));
        Assert.Equal(0, handler.Count("environmentCreate"));
        Assert.Equal(GraphQLFixtures.ApiServiceId, result.ServiceIds["api"]);
        Assert.Contains("environmentId", handler.Bodies.First(body => body.Contains("serviceCreate", StringComparison.Ordinal)), StringComparison.Ordinal);

        var snapshot = await RailwayDeploymentStateStore.LoadAsync(state, "railway", "production", CancellationToken.None);
        Assert.Equal(GraphQLFixtures.ProjectId, snapshot.ProjectId);
        Assert.Equal(GraphQLFixtures.ProductionEnvironmentId, snapshot.EnvironmentId);
    }

    [Fact]
    public async Task Apply_AdoptsExistingProjectAndEnvironment_DoesNotCreate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var result = await apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(adoptExisting: true),
            GraphQLFixtures.CreateRequest(
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.False(result.CreatedProject);
        Assert.False(result.CreatedEnvironment);
        Assert.Equal(0, handler.Count("projectCreate"));
        Assert.Equal(0, handler.Count("environmentCreate"));
        Assert.Equal(GraphQLFixtures.ProjectId, result.ProjectId);
        Assert.Equal(GraphQLFixtures.ProductionEnvironmentId, result.EnvironmentId);
    }

    [Fact]
    public async Task Apply_StagingDuplicatesProductionByDefault()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("environmentCreate", GraphQLFixtures.EnvironmentCreateStaging);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var result = await apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(railwayEnvironmentName: "staging", includeApi: false),
            GraphQLFixtures.CreateRequest(includeApiImage: false),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.True(result.CreatedEnvironment);
        Assert.Equal(GraphQLFixtures.StagingEnvironmentId, result.EnvironmentId);
        Assert.Equal(1, handler.Count("environmentCreate"));
        Assert.Contains("sourceEnvironmentId", handler.Bodies.Single(body => body.Contains("environmentCreate", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains(GraphQLFixtures.ProductionEnvironmentId, handler.Bodies.Single(body => body.Contains("environmentCreate", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_StagingEmptyCreate_IsOptIn()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("environmentCreate", GraphQLFixtures.EnvironmentCreateEmpty);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var result = await apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(railwayEnvironmentName: "staging", createEmpty: true, includeApi: false),
            GraphQLFixtures.CreateRequest(createEmpty: true, includeApiImage: false),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal("env_empty_placeholder", result.EnvironmentId);
        var body = handler.Bodies.Single(item => item.Contains("environmentCreate", StringComparison.Ordinal));
        Assert.DoesNotContain("sourceEnvironmentId", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_StagingDuplicateWithoutProductionId_FailsHonestly()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", """{"data":{"projectCreate":{"id":"proj_placeholder","name":"railway","environments":{"edges":[]}}}}""");

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(railwayEnvironmentName: "staging", includeApi: false),
            GraphQLFixtures.CreateRequest(includeApiImage: false),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("production environment id is unknown", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Count("environmentCreate"));
    }

    [Fact]
    public async Task Apply_TemplatesAndBucket_UseConfirmedOperations()
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

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var result = await apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(includePostgres: true, includeRedis: true, includeBucket: true),
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Contains("postgres", result.AppliedTemplateCodes);
        Assert.Contains("redis", result.AppliedTemplateCodes);
        Assert.Equal(GraphQLFixtures.BucketId, result.BucketIds["uploads"]);
        Assert.Equal(2, handler.Count("template"));
        Assert.Equal(2, handler.Count("templateDeployV2"));
        Assert.Equal(1, handler.Count("bucketCreate"));
        Assert.Contains("secretAccessKey", handler.Bodies.Single(body => body.Contains("bucketS3Credentials", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, string.Join('\n', handler.Bodies), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_MissingServiceImage_FailsClearly()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(),
            GraphQLFixtures.CreateRequest(includeApiImage: false),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("no container image", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IContainerRegistry", exception.Message, StringComparison.Ordinal);
        Assert.Contains("railway up", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Count("serviceCreate"));
    }

    [Fact]
    public async Task Apply_SecondDeploy_ReusesPersistedIds()
    {
        var firstHandler = new ScriptedGraphQLHandler();
        firstHandler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        firstHandler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        firstHandler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        firstHandler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        firstHandler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        firstHandler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var state = new MemoryDeploymentStateManager();
        var first = GraphQLFixtures.CreateApplyService(firstHandler);
        await first.ApplyAsync(
            GraphQLFixtures.CreatePlan(),
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            state);

        var secondHandler = new ScriptedGraphQLHandler();
        secondHandler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        secondHandler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        secondHandler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        secondHandler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var second = GraphQLFixtures.CreateApplyService(secondHandler);
        var result = await second.ApplyAsync(
            GraphQLFixtures.CreatePlan(),
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            state);

        Assert.False(result.CreatedProject);
        Assert.Equal(0, secondHandler.Count("projectCreate"));
        Assert.Equal(0, secondHandler.Count("serviceCreate"));
        Assert.Equal(GraphQLFixtures.ProjectId, result.ProjectId);
        Assert.Equal(GraphQLFixtures.ApiServiceId, result.ServiceIds["api"]);
    }

    [Fact]
    public async Task Apply_GraphQLError_DoesNotReportSuccess()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.GraphQLError("project tokens cannot call projectCreate"));

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(includeApi: false),
            GraphQLFixtures.CreateRequest(includeApiImage: false),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("projectCreate", exception.Message, StringComparison.Ordinal);
        Assert.Contains("project tokens cannot call projectCreate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_FailedTemplateWorkflow_FailsHonestly()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("template", GraphQLFixtures.TemplatePostgres);
        handler.Enqueue("templateDeployV2", GraphQLFixtures.TemplateDeployV2);
        handler.Enqueue("workflowStatus", GraphQLFixtures.WorkflowError);

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(includeApi: false, includePostgres: true),
            GraphQLFixtures.CreateRequest(includeApiImage: false),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("template workflow failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployAsync_ImageBasedServiceWithoutRegistry_Throws()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        await TestAppBuilder.ExecuteBeforeStartHooksAsync(app);

        var context = CreatePipelineContext(TestAppBuilder.GetModel(app), app.Services);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => railway.Resource.DeployAsync(context));

        Assert.Contains("no container image registry", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AddContainerRegistry", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployAsync_WithRegistryAndFakeClient_AppliesAndPersistsIds()
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
        builder.AddContainer("api", "nginx");

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

        Assert.Equal(1, handler.Count("projectCreate"));
        Assert.Equal(1, handler.Count("serviceCreate"));
        var state = provider.GetRequiredService<IDeploymentStateManager>();
        var snapshot = await RailwayDeploymentStateStore.LoadAsync(state, "railway", "production", CancellationToken.None);
        Assert.Equal(GraphQLFixtures.ProjectId, snapshot.ProjectId);
        Assert.Equal(GraphQLFixtures.ProductionEnvironmentId, snapshot.EnvironmentId);
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
