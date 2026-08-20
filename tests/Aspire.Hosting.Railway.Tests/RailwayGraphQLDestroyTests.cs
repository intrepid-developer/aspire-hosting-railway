using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Railway;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aspire.Hosting.Railway.Tests;

public class RailwayGraphQLDestroyTests
{
    [Fact]
    public async Task Destroy_AdoptedResources_AreSkipped()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectWithExistingCanvas);

        var state = new MemoryDeploymentStateManager();
        await SeedStateAsync(
            state,
            "production",
            createdProject: false,
            createdEnvironment: false,
            createdServices: false);

        var reporter = new RecordingReportingStep();
        var destroy = GraphQLFixtures.CreateDestroyService(handler);
        var result = await destroy.DestroyAsync(
            GraphQLFixtures.CreatePlan(adoptExisting: true, includePostgres: true),
            GraphQLFixtures.CreateDestroyRequest(
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            reporter,
            state);

        Assert.Equal(0, handler.Count("serviceDelete"));
        Assert.Equal(0, handler.Count("environmentDelete"));
        Assert.Equal(0, handler.Count("projectDelete"));
        Assert.Equal(0, handler.Count("bucketDelete"));
        Assert.Equal(0, handler.Count("serviceDomainDelete"));
        Assert.Equal(0, handler.Count("customDomainDelete"));
        Assert.DoesNotContain(
            handler.Operations,
            name => name.Contains("Delete", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "project", StringComparison.Ordinal));
        Assert.Contains(
            result.Skipped,
            item => item.Contains("api", StringComparison.OrdinalIgnoreCase) &&
                    item.Contains("adopted", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            result.Skipped,
            item => item.Contains("postgres", StringComparison.OrdinalIgnoreCase) &&
                    item.Contains("adopted", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            result.Skipped,
            item => item.Contains("projectDelete", StringComparison.Ordinal));
        Assert.DoesNotContain(
            reporter.Completions,
            text => text.Contains("not implemented", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Destroy_StagingOnly_DoesNotDeleteProductionOrProject()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectWithProductionAndStaging);
        handler.Enqueue("domains", GraphQLFixtures.DomainsEmpty);
        handler.Enqueue("domains", GraphQLFixtures.DomainsEmpty);
        handler.Enqueue("environmentDelete", GraphQLFixtures.EnvironmentDelete);

        var state = new MemoryDeploymentStateManager();
        await SeedStateAsync(
            state,
            "production",
            createdProject: true,
            createdEnvironment: false,
            createdServices: true);
        await SeedStateAsync(
            state,
            "staging",
            createdProject: false,
            createdEnvironment: true,
            createdServices: false,
            environmentId: GraphQLFixtures.StagingEnvironmentId);

        var reporter = new RecordingReportingStep();
        var destroy = GraphQLFixtures.CreateDestroyService(handler);
        var result = await destroy.DestroyAsync(
            GraphQLFixtures.CreatePlan(
                railwayEnvironmentName: "staging",
                includePostgres: true,
                includeBucket: true),
            GraphQLFixtures.CreateDestroyRequest(),
            reporter,
            state);

        Assert.Equal(1, handler.Count("environmentDelete"));
        Assert.Equal(0, handler.Count("serviceDelete"));
        Assert.Equal(0, handler.Count("projectDelete"));
        Assert.Equal(0, handler.Count("bucketDelete"));
        Assert.Contains(
            GraphQLFixtures.StagingEnvironmentId,
            handler.Bodies.Single(body => body.Contains("environmentDelete", StringComparison.Ordinal)),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            handler.Bodies,
            body => body.Contains(GraphQLFixtures.ProductionEnvironmentId, StringComparison.Ordinal) &&
                    body.Contains("environmentDelete", StringComparison.Ordinal));
        Assert.Contains(
            result.Skipped,
            item => item.Contains("serviceDelete would remove this service from other Railway environments", StringComparison.Ordinal) ||
                    item.Contains("adopted", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Deleted, item => item.Contains("staging", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Skipped, item => item.Contains("uploads", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Skipped, item => item.Contains("projectDelete", StringComparison.Ordinal));

        var production = await RailwayDeploymentStateStore.LoadAsync(
            state, "railway", "production", CancellationToken.None);
        Assert.Equal(GraphQLFixtures.ProjectId, production.ProjectId);
        Assert.Equal(GraphQLFixtures.ApiServiceId, production.ServiceIds["api"]);

        var staging = await RailwayDeploymentStateStore.LoadAsync(
            state, "railway", "staging", CancellationToken.None);
        Assert.Empty(staging.ServiceIds);
        Assert.Null(staging.EnvironmentId);
    }

    [Fact]
    public async Task Destroy_EmptyState_FailsClosed()
    {
        var handler = new ScriptedGraphQLHandler();
        var destroy = GraphQLFixtures.CreateDestroyService(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            destroy.DestroyAsync(
                GraphQLFixtures.CreatePlan(),
                GraphQLFixtures.CreateDestroyRequest(),
                new RecordingReportingStep(),
                new MemoryDeploymentStateManager()));

        Assert.Contains("Failing closed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("deployment state is empty", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
    }

    [Fact]
    public async Task DestroyAsync_EmptyState_FailsClosed()
    {
        var builder = TestAppBuilder.CreatePublish();
        builder.Configuration["RAILWAY_TOKEN"] = GraphQLFixtures.Token;
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        await TestAppBuilder.ExecuteBeforeStartHooksAsync(app);

        var services = new ServiceCollection();
        services.AddSingleton(app.Services.GetRequiredService<DistributedApplicationModel>());
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment());
        services.AddSingleton<IDeploymentStateManager>(new MemoryDeploymentStateManager());
        var provider = services.BuildServiceProvider();
        var context = CreatePipelineContext(TestAppBuilder.GetModel(app), provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => railway.Resource.DestroyAsync(context));

        Assert.Contains("Failing closed", exception.Message, StringComparison.Ordinal);
        var reporter = Assert.IsType<RecordingReportingStep>(context.ReportingStep);
        Assert.DoesNotContain(
            reporter.Completions,
            text => text.Contains("Nothing to destroy", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("not implemented", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Destroy_CreatedProduction_DeletesServicesAndDomains_NotProject()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectCanvas(
            [(GraphQLFixtures.ApiServiceId, "api"), (GraphQLFixtures.PostgresServiceId, "Postgres")],
            [(GraphQLFixtures.BucketId, "uploads")]));
        handler.Enqueue("domains", GraphQLFixtures.DomainsWithCustom);
        handler.Enqueue("domains", GraphQLFixtures.DomainsEmpty);
        handler.Enqueue("serviceDomainDelete", GraphQLFixtures.ServiceDomainDelete);
        handler.Enqueue("customDomainDelete", GraphQLFixtures.CustomDomainDelete);
        handler.Enqueue("serviceDelete", GraphQLFixtures.ServiceDelete);
        handler.Enqueue("serviceDelete", GraphQLFixtures.ServiceDelete);

        var state = new MemoryDeploymentStateManager();
        await SeedStateAsync(
            state,
            "production",
            createdProject: true,
            createdEnvironment: false,
            createdServices: true,
            includePostgres: true,
            includeBucket: true,
            includeDomains: true);

        var reporter = new RecordingReportingStep();
        var destroy = GraphQLFixtures.CreateDestroyService(handler);
        var result = await destroy.DestroyAsync(
            GraphQLFixtures.CreatePlan(
                includePostgres: true,
                includeBucket: true,
                includeApi: true),
            GraphQLFixtures.CreateDestroyRequest(),
            reporter,
            state);

        Assert.Equal(1, handler.Count("serviceDomainDelete"));
        Assert.Equal(1, handler.Count("customDomainDelete"));
        Assert.Equal(2, handler.Count("serviceDelete"));
        Assert.Equal(0, handler.Count("environmentDelete"));
        Assert.Equal(0, handler.Count("projectDelete"));
        Assert.Equal(0, handler.Count("bucketDelete"));
        Assert.Equal(0, handler.Count("volumeDelete"));
        Assert.Equal(0, handler.Count("volumeInstanceBackupDelete"));
        Assert.DoesNotContain(handler.Operations, name =>
            string.Equals(name, "bucketDelete", StringComparison.Ordinal) ||
            string.Equals(name, "projectDelete", StringComparison.Ordinal) ||
            string.Equals(name, "pluginCreate", StringComparison.Ordinal));

        var serviceDeleteBodies = handler.Bodies
            .Where(body => body.Contains("\"operationName\":\"serviceDelete\"", StringComparison.Ordinal))
            .ToArray();
        Assert.All(serviceDeleteBodies, body =>
        {
            Assert.Contains("environmentId", body, StringComparison.Ordinal);
            Assert.Contains(GraphQLFixtures.ProductionEnvironmentId, body, StringComparison.Ordinal);
        });

        Assert.Contains(result.Deleted, item => item.Contains("api", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Deleted, item => item.Contains("postgres", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Skipped, item => item.Contains("uploads", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Skipped, item => item.Contains("bucketDelete", StringComparison.Ordinal));
        Assert.Contains(result.Skipped, item => item.Contains("projectDelete", StringComparison.Ordinal));
        Assert.DoesNotContain(
            reporter.Completions,
            text => text.Contains("not implemented", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("only a warning", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DestroyAsync_YesPath_DeletesCreatedService()
    {
        var applyHandler = new ScriptedGraphQLHandler();
        applyHandler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        applyHandler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        applyHandler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        applyHandler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        applyHandler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        applyHandler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var destroyHandler = new ScriptedGraphQLHandler();
        destroyHandler.Enqueue("project", GraphQLFixtures.ProjectWithApi);
        destroyHandler.Enqueue("domains", GraphQLFixtures.DomainsEmpty);
        destroyHandler.Enqueue("serviceDelete", GraphQLFixtures.ServiceDelete);

        var builder = TestAppBuilder.CreatePublish();
        builder.Configuration["RAILWAY_TOKEN"] = GraphQLFixtures.Token;
        var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io");
        var railway = builder.AddRailwayEnvironment("railway").WithContainerRegistry(ghcr);
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        await TestAppBuilder.ExecuteBeforeStartHooksAsync(app);

        var state = new MemoryDeploymentStateManager();
        var services = new ServiceCollection();
        services.AddSingleton(app.Services.GetRequiredService<DistributedApplicationModel>());
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment());
        services.AddSingleton<IDeploymentStateManager>(state);
        services.AddSingleton(new RailwayGraphQLClient(new HttpClient(applyHandler)));
        var provider = services.BuildServiceProvider();
        await railway.Resource.DeployAsync(CreatePipelineContext(TestAppBuilder.GetModel(app), provider));

        var snapshot = await RailwayDeploymentStateStore.LoadAsync(
            state, "railway", "production", CancellationToken.None);
        Assert.True(snapshot.CreatedProject);
        Assert.Equal(GraphQLFixtures.ApiServiceId, snapshot.CreatedServiceIds["api"]);

        var destroyServices = new ServiceCollection();
        destroyServices.AddSingleton(app.Services.GetRequiredService<DistributedApplicationModel>());
        destroyServices.AddSingleton<IHostEnvironment>(new FakeHostEnvironment());
        destroyServices.AddSingleton<IDeploymentStateManager>(state);
        destroyServices.AddSingleton(new RailwayGraphQLClient(new HttpClient(destroyHandler)));
        var destroyProvider = destroyServices.BuildServiceProvider();
        var destroyContext = CreatePipelineContext(TestAppBuilder.GetModel(app), destroyProvider);
        await railway.Resource.DestroyAsync(destroyContext);

        Assert.Equal(1, destroyHandler.Count("serviceDelete"));
        Assert.Equal(0, destroyHandler.Count("projectDelete"));
        var reporter = Assert.IsType<RecordingReportingStep>(destroyContext.ReportingStep);
        Assert.DoesNotContain(
            reporter.Completions,
            text => text.Contains("not implemented", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Nothing to destroy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            reporter.Completions,
            text => text.Contains("Deleted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DestroyOperations_AreConfirmedDeletes_NotInvented()
    {
        Assert.Contains("serviceDelete", RailwayGraphQLOperations.ServiceDelete, StringComparison.Ordinal);
        Assert.Contains("$environmentId: String", RailwayGraphQLOperations.ServiceDelete, StringComparison.Ordinal);
        Assert.Contains("serviceDomainDelete", RailwayGraphQLOperations.ServiceDomainDelete, StringComparison.Ordinal);
        Assert.Contains("customDomainDelete", RailwayGraphQLOperations.CustomDomainDelete, StringComparison.Ordinal);
        Assert.Contains("environmentDelete", RailwayGraphQLOperations.EnvironmentDelete, StringComparison.Ordinal);
        Assert.DoesNotContain("bucketDelete", RailwayGraphQLOperations.ServiceDelete, StringComparison.Ordinal);
        Assert.DoesNotContain("projectDelete", RailwayGraphQLOperations.EnvironmentDelete, StringComparison.Ordinal);
        Assert.DoesNotContain("volumeDelete", RailwayGraphQLOperations.ServiceDelete, StringComparison.Ordinal);
        Assert.DoesNotContain("pluginCreate", RailwayGraphQLOperations.ServiceDelete, StringComparison.Ordinal);
    }

    private static async Task SeedStateAsync(
        MemoryDeploymentStateManager state,
        string railwayEnvironmentName,
        bool createdProject,
        bool createdEnvironment,
        bool createdServices,
        bool includePostgres = false,
        bool includeBucket = false,
        bool includeDomains = false,
        string? environmentId = null)
    {
        var result = new RailwayApplyResult
        {
            ProjectId = GraphQLFixtures.ProjectId,
            EnvironmentId = environmentId ?? GraphQLFixtures.ProductionEnvironmentId,
            ProductionEnvironmentId = GraphQLFixtures.ProductionEnvironmentId,
            CreatedProject = createdProject,
            CreatedEnvironment = createdEnvironment
        };
        result.ServiceIds["api"] = GraphQLFixtures.ApiServiceId;
        if (createdServices)
        {
            result.CreatedServiceIds["api"] = GraphQLFixtures.ApiServiceId;
        }

        if (includePostgres)
        {
            result.ServiceIds["postgres"] = GraphQLFixtures.PostgresServiceId;
            if (createdServices)
            {
                result.CreatedServiceIds["postgres"] = GraphQLFixtures.PostgresServiceId;
            }
        }

        if (includeBucket)
        {
            result.BucketIds["uploads"] = GraphQLFixtures.BucketId;
        }

        if (includeDomains)
        {
            result.CreatedServiceDomainIds["api"] = "domain_placeholder";
            result.CustomDomainIds["api.example.com"] = GraphQLFixtures.CustomDomainId;
            result.CreatedCustomDomainIds["api.example.com"] = GraphQLFixtures.CustomDomainId;
        }

        await RailwayDeploymentStateStore.SaveAsync(
            state,
            "railway",
            railwayEnvironmentName,
            result,
            CancellationToken.None);
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
