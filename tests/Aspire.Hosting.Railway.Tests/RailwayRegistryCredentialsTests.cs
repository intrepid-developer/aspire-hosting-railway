using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using IHostEnvironment = Microsoft.Extensions.Hosting.IHostEnvironment;

namespace Aspire.Hosting.Railway.Tests;

public class RailwayRegistryCredentialsTests
{
    [Fact]
    public void Plan_WithUsernameAndPassword_WritesParameterNamesOnly()
    {
        var builder = TestAppBuilder.CreatePublish();
        var ghcr = builder.AddTestGhcr();
        var railway = builder.AddRailwayEnvironment("railway").WithContainerRegistry(ghcr);
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Contains("ghcr.io", json, StringComparison.Ordinal);
        Assert.Contains("ghcr-username", json, StringComparison.Ordinal);
        Assert.Contains("ghcr-password", json, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.RegistryUsername, json, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.RegistryPassword, json, StringComparison.Ordinal);
        Assert.DoesNotContain("registryCredentials", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", json, StringComparison.Ordinal);
        Assert.DoesNotContain("github_pat_", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_PrivateGhcrImage_StagesAndCommitsRegistryCredentials()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        GraphQLFixtures.EnqueueRegistryCredentials(handler);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var state = new MemoryDeploymentStateManager();
        var apply = GraphQLFixtures.CreateApplyService(handler);
        var plan = GraphQLFixtures.CreatePlan();
        var request = GraphQLFixtures.CreateRequest();

        await apply.ApplyAsync(plan, request, new RecordingReportingStep(), state);

        Assert.Equal(1, handler.Count("environmentStageChanges"));
        Assert.Equal(1, handler.Count("environmentPatchCommit"));
        var createIndex = handler.Operations.IndexOf("serviceCreate");
        var stageIndex = handler.Operations.IndexOf("environmentStageChanges");
        var updateIndex = handler.Operations.IndexOf("serviceInstanceUpdate");
        Assert.True(createIndex >= 0 && stageIndex == createIndex + 1);
        Assert.True(updateIndex == stageIndex + 2);

        var staged = GraphQLFixtures.GetEnvironmentStageChangesVariables(handler.Bodies, "registryCredentials");
        Assert.Equal(GraphQLFixtures.ProductionEnvironmentId, staged.GetProperty("environmentId").GetString());
        Assert.True(staged.GetProperty("merge").GetBoolean());
        var credentials = staged.GetProperty("input").GetProperty("services")
            .GetProperty(GraphQLFixtures.ApiServiceId)
            .GetProperty("deploy")
            .GetProperty("registryCredentials");
        Assert.Equal(GraphQLFixtures.RegistryUsername, credentials.GetProperty("username").GetString());
        Assert.Equal(GraphQLFixtures.RegistryPassword, credentials.GetProperty("password").GetString());
        Assert.False(staged.GetProperty("input").TryGetProperty("buckets", out _));

        var committed = GraphQLFixtures.GetEnvironmentPatchCommitVariables(handler.Bodies, "registryCredentials");
        Assert.Equal(
            GraphQLFixtures.RegistryUsername,
            committed.GetProperty("patch").GetProperty("services").GetProperty(GraphQLFixtures.ApiServiceId)
                .GetProperty("deploy").GetProperty("registryCredentials").GetProperty("username").GetString());

        var update = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("ghcr.io/example/api:placeholder", update.GetProperty("source").GetProperty("image").GetString());
        Assert.False(update.TryGetProperty("registryCredentials", out _));

        var planJson = RailwayPlanBuilder.ToJson(plan);
        var section = await state.AcquireSectionAsync("Railway:railway");
        var persisted = section.Data.ToJsonString();
        Assert.DoesNotContain(GraphQLFixtures.RegistryUsername, planJson, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.RegistryPassword, planJson, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.RegistryUsername, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.RegistryPassword, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("registryCredentials", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, persisted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_PrivateGhcrImageWithoutCredentials_FailsBeforeGraphQLMutations()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(),
            GraphQLFixtures.CreateRequest(includeRegistryCredentials: false),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("cannot pull", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ghcr.io", exception.Message, StringComparison.Ordinal);
        Assert.Contains("WithUsername", exception.Message, StringComparison.Ordinal);
        Assert.Contains("WithPassword", exception.Message, StringComparison.Ordinal);
        Assert.Contains("GITHUB_TOKEN", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Pro plan", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.Count("projectCreate"));
        Assert.Equal(0, handler.Count("serviceCreate"));
        Assert.Equal(0, handler.Count("environmentStageChanges"));
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
        Assert.Equal(0, handler.Count("serviceInstanceDeployV2"));
    }

    [Fact]
    public async Task DeployAsync_WithUsernameAndPassword_DoesNotLeakIntoPlanOrState()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        GraphQLFixtures.EnqueueRegistryCredentials(handler);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var builder = TestAppBuilder.CreatePublish();
        builder.Configuration["RAILWAY_TOKEN"] = GraphQLFixtures.Token;
        var ghcr = builder.AddTestGhcr();
        var railway = builder.AddRailwayEnvironment("railway").WithContainerRegistry(ghcr);
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        await TestAppBuilder.ExecuteBeforeStartHooksAsync(app);

        var model = TestAppBuilder.GetModel(app);
        var plan = RailwayPlanBuilder.Create(model, railway.Resource, "Production");
        var planJson = RailwayPlanBuilder.ToJson(plan);
        Assert.DoesNotContain(GraphQLFixtures.RegistryUsername, planJson, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.RegistryPassword, planJson, StringComparison.Ordinal);

        var state = new MemoryDeploymentStateManager();
        var services = new ServiceCollection();
        services.AddSingleton(model);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment());
        services.AddSingleton<IDeploymentStateManager>(state);
        services.AddSingleton(new RailwayGraphQLClient(new HttpClient(handler)));
        var provider = services.BuildServiceProvider();

        await railway.Resource.DeployAsync(CreatePipelineContext(model, provider));

        var staged = GraphQLFixtures.GetEnvironmentStageChangesVariables(handler.Bodies, "registryCredentials");
        Assert.Equal(
            GraphQLFixtures.RegistryUsername,
            staged.GetProperty("input").GetProperty("services").GetProperty(GraphQLFixtures.ApiServiceId)
                .GetProperty("deploy").GetProperty("registryCredentials").GetProperty("username").GetString());

        var persisted = (await state.AcquireSectionAsync("Railway:railway")).Data.ToJsonString();
        Assert.DoesNotContain(GraphQLFixtures.RegistryUsername, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.RegistryPassword, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, persisted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployAsync_PrivateImageWithoutCredentials_FailsClearly()
    {
        var builder = TestAppBuilder.CreatePublish();
        builder.Configuration["RAILWAY_TOKEN"] = GraphQLFixtures.Token;
        var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io", "intrepid-developer/playground");
        var railway = builder.AddRailwayEnvironment("railway").WithContainerRegistry(ghcr);
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        await TestAppBuilder.ExecuteBeforeStartHooksAsync(app);

        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        var services = new ServiceCollection();
        services.AddSingleton(TestAppBuilder.GetModel(app));
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment());
        services.AddSingleton<IDeploymentStateManager>(new MemoryDeploymentStateManager());
        services.AddSingleton(new RailwayGraphQLClient(new HttpClient(handler)));
        var provider = services.BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => railway.Resource.DeployAsync(CreatePipelineContext(TestAppBuilder.GetModel(app), provider)));

        Assert.Contains("cannot pull", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WithUsername", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Count("serviceCreate"));
        Assert.Equal(0, handler.Count("environmentStageChanges"));
    }

    [Theory]
    [InlineData("ghcr.io/example/api:placeholder", true)]
    [InlineData("registry.gitlab.com/group/api:1", true)]
    [InlineData("quay.io/org/api:1", true)]
    [InlineData("us-west1-docker.pkg.dev/proj/repo/api:1", true)]
    [InlineData("nginx", false)]
    [InlineData("docker.io/library/nginx:latest", false)]
    [InlineData("mcr.microsoft.com/dotnet/runtime:10.0", false)]
    public void RequiresCredentials_ClassifiesImageHosts(string image, bool required)
    {
        Assert.Equal(required, RailwayImageRegistry.RequiresCredentials(image));
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
}
