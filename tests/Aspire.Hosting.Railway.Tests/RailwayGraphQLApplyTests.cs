using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Railway;

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
    public async Task Apply_AdoptsExistingPostgresService_SkipsTemplateDeployV2()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectWithExistingCanvas);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var result = await apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(adoptExisting: true, includePostgres: true),
            GraphQLFixtures.CreateRequest(
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.False(result.CreatedProject);
        Assert.Contains("postgres", result.AppliedTemplateCodes);
        Assert.Equal(GraphQLFixtures.PostgresServiceId, result.ServiceIds["postgres"]);
        Assert.Equal(GraphQLFixtures.ApiServiceId, result.ServiceIds["api"]);
        Assert.Equal(1, handler.Count("project"));
        Assert.Equal(0, handler.Count("template"));
        Assert.Equal(0, handler.Count("templateDeployV2"));
        Assert.Equal(0, handler.Count("serviceCreate"));
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        Assert.Equal(1, handler.Count("serviceInstanceDeployV2"));
        var projectBody = handler.Bodies.Single(body => body.Contains("\"operationName\":\"project\"", StringComparison.Ordinal));
        Assert.Contains(GraphQLFixtures.ProjectId, projectBody, StringComparison.Ordinal);
        Assert.Contains("services", projectBody, StringComparison.Ordinal);
        Assert.Contains("buckets", projectBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_AdoptsExistingProjectAndEnvironment_DoesNotCreate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectEmpty);
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
        var productionHandler = new ScriptedGraphQLHandler();
        EnqueueProductionServiceTemplateAndBucket(productionHandler);

        var state = new MemoryDeploymentStateManager();
        var production = GraphQLFixtures.CreateApplyService(productionHandler);
        await production.ApplyAsync(
            GraphQLFixtures.CreatePlan(includePostgres: true, includeBucket: true),
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            state);

        var stagingHandler = new ScriptedGraphQLHandler();
        stagingHandler.Enqueue("environmentCreate", GraphQLFixtures.EnvironmentCreateStaging);
        stagingHandler.Enqueue("project", GraphQLFixtures.ProjectWithExistingCanvas);
        stagingHandler.Enqueue("bucketS3Credentials", GraphQLFixtures.BucketCredentials);
        stagingHandler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        stagingHandler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        stagingHandler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        stagingHandler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        stagingHandler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var staging = GraphQLFixtures.CreateApplyService(stagingHandler);
        var result = await staging.ApplyAsync(
            GraphQLFixtures.CreatePlan(railwayEnvironmentName: "staging", includePostgres: true, includeBucket: true),
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            state);

        Assert.True(result.CreatedEnvironment);
        Assert.Equal(GraphQLFixtures.StagingEnvironmentId, result.EnvironmentId);
        Assert.Equal(GraphQLFixtures.ApiServiceId, result.ServiceIds["api"]);
        Assert.Equal(GraphQLFixtures.BucketId, result.BucketIds["uploads"]);
        Assert.Contains("postgres", result.AppliedTemplateCodes);
        Assert.Equal(1, stagingHandler.Count("environmentCreate"));
        Assert.Equal(0, stagingHandler.Count("projectCreate"));
        Assert.Equal(0, stagingHandler.Count("serviceCreate"));
        Assert.Equal(0, stagingHandler.Count("templateDeployV2"));
        Assert.Equal(0, stagingHandler.Count("bucketCreate"));
        Assert.Equal(1, stagingHandler.Count("serviceInstanceUpdate"));
        Assert.Equal(1, stagingHandler.Count("serviceInstanceDeployV2"));
        var environmentCreate = stagingHandler.Bodies.Single(body => body.Contains("environmentCreate", StringComparison.Ordinal));
        Assert.Contains("sourceEnvironmentId", environmentCreate, StringComparison.Ordinal);
        Assert.Contains(GraphQLFixtures.ProductionEnvironmentId, environmentCreate, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_TemplatePersistedBeforeComputeFailure_RetrySkipsTemplateDeploy()
    {
        var firstHandler = new ScriptedGraphQLHandler();
        firstHandler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        firstHandler.Enqueue("template", GraphQLFixtures.TemplatePostgres);
        firstHandler.Enqueue("templateDeployV2", GraphQLFixtures.TemplateDeployV2);
        firstHandler.Enqueue("workflowStatus", GraphQLFixtures.WorkflowComplete);

        var state = new MemoryDeploymentStateManager();
        var first = GraphQLFixtures.CreateApplyService(firstHandler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => first.ApplyAsync(
            GraphQLFixtures.CreatePlan(includePostgres: true),
            GraphQLFixtures.CreateRequest(includeApiImage: false),
            new RecordingReportingStep(),
            state));

        Assert.Contains("no container image", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, firstHandler.Count("templateDeployV2"));

        var snapshot = await RailwayDeploymentStateStore.LoadAsync(state, "railway", "production", CancellationToken.None);
        Assert.Contains("postgres", snapshot.TemplateCodes);

        var retryHandler = new ScriptedGraphQLHandler();
        retryHandler.Enqueue("project", GraphQLFixtures.ProjectEmpty);
        retryHandler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        retryHandler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        retryHandler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        retryHandler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        retryHandler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var retry = GraphQLFixtures.CreateApplyService(retryHandler);
        var result = await retry.ApplyAsync(
            GraphQLFixtures.CreatePlan(includePostgres: true),
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            state);

        Assert.Contains("postgres", result.AppliedTemplateCodes);
        Assert.Equal(0, retryHandler.Count("template"));
        Assert.Equal(0, retryHandler.Count("templateDeployV2"));
        Assert.Equal(1, retryHandler.Count("serviceCreate"));
    }

    [Fact]
    public async Task Apply_MissingWorkflowId_DoesNotMarkTemplateApplied()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("template", GraphQLFixtures.TemplatePostgres);
        handler.Enqueue("templateDeployV2", GraphQLFixtures.TemplateDeployV2WithoutWorkflow);

        var state = new MemoryDeploymentStateManager();
        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(includeApi: false, includePostgres: true),
            GraphQLFixtures.CreateRequest(includeApiImage: false),
            new RecordingReportingStep(),
            state));

        Assert.Contains("workflowId", exception.Message, StringComparison.Ordinal);
        var snapshot = await RailwayDeploymentStateStore.LoadAsync(state, "railway", "production", CancellationToken.None);
        Assert.DoesNotContain("postgres", snapshot.TemplateCodes);
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
    public async Task Apply_AdoptsExistingBucketByName_SkipsBucketCreate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectWithExistingBucket);
        handler.Enqueue("bucketS3Credentials", GraphQLFixtures.BucketCredentials);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var state = new MemoryDeploymentStateManager();
        var apply = GraphQLFixtures.CreateApplyService(handler);
        var result = await apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(adoptExisting: true, includeBucket: true),
            GraphQLFixtures.CreateRequest(
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            new RecordingReportingStep(),
            state);

        Assert.Equal(GraphQLFixtures.BucketId, result.BucketIds["uploads"]);
        Assert.Equal(GraphQLFixtures.UploadsServiceId, result.ServiceIds["uploads"]);
        Assert.NotEqual(result.ServiceIds["uploads"], result.BucketIds["uploads"]);
        Assert.Equal(0, handler.Count("bucketCreate"));
        Assert.Equal(1, handler.Count("bucketS3Credentials"));
        Assert.Equal(0, handler.Count("serviceCreate"));
        var credentialsBody = handler.Bodies.Single(body => body.Contains("bucketS3Credentials", StringComparison.Ordinal));
        Assert.Contains(GraphQLFixtures.BucketId, credentialsBody, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.UploadsServiceId, credentialsBody, StringComparison.Ordinal);

        var snapshot = await RailwayDeploymentStateStore.LoadAsync(state, "railway", "production", CancellationToken.None);
        Assert.Equal(GraphQLFixtures.BucketId, snapshot.BucketIds["uploads"]);
        var section = await state.AcquireSectionAsync("Railway:railway");
        var persisted = section.Data.ToJsonString();
        Assert.DoesNotContain("placeholder-access-key", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-secret-key", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretAccessKey", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, persisted, StringComparison.Ordinal);
        Assert.Contains(GraphQLFixtures.BucketId, persisted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_MissingBucket_StillCreates()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectWithApi);
        handler.Enqueue("bucketCreate", GraphQLFixtures.BucketCreate);
        handler.Enqueue("bucketS3Credentials", GraphQLFixtures.BucketCredentials);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateUploads);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var result = await apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(adoptExisting: true, includeBucket: true),
            GraphQLFixtures.CreateRequest(
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(GraphQLFixtures.BucketId, result.BucketIds["uploads"]);
        Assert.Equal(1, handler.Count("bucketCreate"));
        Assert.Equal(1, handler.Count("bucketS3Credentials"));
        Assert.Equal(1, handler.Count("serviceCreate"));
        var createBody = handler.Bodies.Single(body => body.Contains("bucketCreate", StringComparison.Ordinal));
        Assert.Contains("uploads", createBody, StringComparison.Ordinal);
        Assert.Contains(GraphQLFixtures.ProductionEnvironmentId, createBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_SameNameService_IsNotUsedAsBucketId()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectWithUploadsServiceOnly);
        handler.Enqueue("bucketCreate", GraphQLFixtures.BucketCreate);
        handler.Enqueue("bucketS3Credentials", GraphQLFixtures.BucketCredentials);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var result = await apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(adoptExisting: true, includeBucket: true),
            GraphQLFixtures.CreateRequest(
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(GraphQLFixtures.UploadsServiceId, result.ServiceIds["uploads"]);
        Assert.Equal(GraphQLFixtures.BucketId, result.BucketIds["uploads"]);
        Assert.NotEqual(result.ServiceIds["uploads"], result.BucketIds["uploads"]);
        Assert.Equal(1, handler.Count("bucketCreate"));
        Assert.Equal(0, handler.Count("serviceCreate"));
        var credentialsBody = handler.Bodies.Single(body => body.Contains("bucketS3Credentials", StringComparison.Ordinal));
        Assert.Contains(GraphQLFixtures.BucketId, credentialsBody, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.UploadsServiceId, credentialsBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_BucketCreate_RetriesCredentialsUntilInstanceExists()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("bucketCreate", GraphQLFixtures.BucketCreate);
        handler.Enqueue("bucketS3Credentials", GraphQLFixtures.GraphQLError("BucketInstance not found"));
        handler.Enqueue("bucketS3Credentials", GraphQLFixtures.BucketCredentials);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateUploads);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var result = await apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(includeApi: false, includeBucket: true),
            GraphQLFixtures.CreateRequest(includeApiImage: false),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(GraphQLFixtures.BucketId, result.BucketIds["uploads"]);
        Assert.Equal(1, handler.Count("bucketCreate"));
        Assert.Equal(2, handler.Count("bucketS3Credentials"));
    }

    [Fact]
    public async Task Apply_BucketSecrets_AreNotWrittenToPlanOrState()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("bucketCreate", GraphQLFixtures.BucketCreate);
        handler.Enqueue("bucketS3Credentials", GraphQLFixtures.BucketCredentials);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateUploads);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var state = new MemoryDeploymentStateManager();
        var apply = GraphQLFixtures.CreateApplyService(handler);
        var plan = GraphQLFixtures.CreatePlan(includeApi: false, includeBucket: true);
        var planJson = RailwayPlanBuilder.ToJson(plan);
        Assert.DoesNotContain("placeholder-access-key", planJson, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-secret-key", planJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretAccessKey", planJson, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, planJson, StringComparison.Ordinal);

        var result = await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(includeApiImage: false),
            new RecordingReportingStep(),
            state);

        Assert.Contains("placeholder-secret-key", result.BucketConnectionStrings["uploads"], StringComparison.Ordinal);

        var snapshot = await RailwayDeploymentStateStore.LoadAsync(state, "railway", "production", CancellationToken.None);
        Assert.Equal(GraphQLFixtures.BucketId, snapshot.BucketIds["uploads"]);
        Assert.DoesNotContain("placeholder-access-key", snapshot.BucketIds.Values, StringComparer.Ordinal);
        Assert.DoesNotContain("placeholder-secret-key", snapshot.BucketIds.Values, StringComparer.Ordinal);

        await RailwayDeploymentStateStoreTests.FlattenUnflattenSectionAsync(state, "Railway:railway");
        var flattenedSnapshot = await RailwayDeploymentStateStore.LoadAsync(state, "railway", "production", CancellationToken.None);
        Assert.Equal(GraphQLFixtures.BucketId, flattenedSnapshot.BucketIds["uploads"]);

        var section = await state.AcquireSectionAsync("Railway:railway");
        Assert.IsType<System.Text.Json.Nodes.JsonObject>(section.Data[RailwayDeploymentStateStore.BucketsKey]?["production"]);
        Assert.IsNotType<System.Text.Json.Nodes.JsonArray>(section.Data[RailwayDeploymentStateStore.BucketsKey]?["production"]);
        var persisted = section.Data.ToJsonString();
        Assert.DoesNotContain("placeholder-access-key", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-secret-key", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, persisted, StringComparison.Ordinal);
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
        var deployBodies = handler.Bodies.Where(body => body.Contains("templateDeployV2", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, deployBodies.Length);
        Assert.Equal(
            GraphQLFixtures.ReadTemplateIdFromResponse(GraphQLFixtures.TemplatePostgres),
            GraphQLFixtures.ReadTemplateIdFromDeployBody(deployBodies[0]));
        Assert.Equal(
            GraphQLFixtures.ReadTemplateIdFromResponse(GraphQLFixtures.TemplateRedis),
            GraphQLFixtures.ReadTemplateIdFromDeployBody(deployBodies[1]));
        var credentialsBody = handler.Bodies.Single(body => body.Contains("bucketS3Credentials", StringComparison.Ordinal));
        Assert.Contains("secretAccessKey", credentialsBody, StringComparison.Ordinal);
        Assert.Contains("projectId", credentialsBody, StringComparison.Ordinal);
        Assert.Contains("bucketName", credentialsBody, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, string.Join('\n', handler.Bodies), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_UpsertsResolvedNonRailwayConnectionString()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].Environment["ConnectionStrings__chat"] = "xai-api-key";
        plan.Parameters.Add("xai-api-key");

        var request = GraphQLFixtures.CreateRequest();
        request.ResolvedServiceEnvironment["api"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ConnectionStrings__chat"] = "Endpoint=https://api.example.test/v1;Key=placeholder-openai-key"
        };

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            request,
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var upsert = handler.Bodies.Single(body => body.Contains("variableCollectionUpsert", StringComparison.Ordinal));
        Assert.Contains("ConnectionStrings__chat", upsert, StringComparison.Ordinal);
        Assert.Contains("placeholder-openai-key", upsert, StringComparison.Ordinal);
        Assert.Contains("${{postgres.DATABASE_URL}}", upsert, StringComparison.Ordinal);
        Assert.DoesNotContain("\"xai-api-key\"", upsert, StringComparison.Ordinal);
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
        secondHandler.Enqueue("project", GraphQLFixtures.ProjectWithApi);
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
    public async Task DeployAsync_WithNonRailwayConnectionString_UpsertsResolvedValue()
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
        var key = builder.AddParameter("xai-api-key", "placeholder-openai-key", secret: true);
        var chat = builder.AddResource(new FakeChatConnectionStringResource("chat", key.Resource));
        builder.AddContainer("api", "nginx").WithReference(chat);

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

        var upsert = handler.Bodies.Single(body => body.Contains("variableCollectionUpsert", StringComparison.Ordinal));
        Assert.Contains("ConnectionStrings__chat", upsert, StringComparison.Ordinal);
        Assert.Contains("placeholder-openai-key", upsert, StringComparison.Ordinal);
        Assert.Contains("https://api.example.test/v1", upsert, StringComparison.Ordinal);
        Assert.DoesNotContain("\"xai-api-key\"", upsert, StringComparison.Ordinal);
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

    [Fact]
    public async Task Apply_ImageOnlyServiceInstanceUpdate_OmitsScaleFields()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(),
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("ghcr.io/example/api:placeholder", input.GetProperty("source").GetProperty("image").GetString());
        Assert.False(input.TryGetProperty("multiRegionConfig", out _));
        Assert.False(input.TryGetProperty("sleepApplication", out _));
        Assert.False(input.TryGetProperty("numReplicas", out _));
        Assert.False(input.TryGetProperty("region", out _));
        Assert.False(input.TryGetProperty("vCPUs", out _));
        Assert.False(input.TryGetProperty("memoryGB", out _));
        Assert.False(input.TryGetProperty("healthcheckPath", out _));
        Assert.False(input.TryGetProperty("healthcheckTimeout", out _));
        Assert.False(input.TryGetProperty("restartPolicyType", out _));
        Assert.False(input.TryGetProperty("restartPolicyMaxRetries", out _));
        Assert.Equal(0, handler.Count("serviceInstanceLimitsUpdate"));
    }

    [Fact]
    public async Task Apply_RegionReplicasAndServerless_SendsMultiRegionConfigAndSleepApplication()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].Region = "europe-west4-drams3a";
        plan.Services[0].Replicas = 2;
        plan.Services[0].Serverless = true;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("ghcr.io/example/api:placeholder", input.GetProperty("source").GetProperty("image").GetString());
        Assert.Equal(
            2,
            input.GetProperty("multiRegionConfig").GetProperty("europe-west4-drams3a").GetProperty("numReplicas").GetInt32());
        Assert.True(input.GetProperty("sleepApplication").GetBoolean());
        Assert.False(input.TryGetProperty("numReplicas", out _));
        Assert.False(input.TryGetProperty("region", out _));
        Assert.Equal(0, handler.Count("serviceInstanceLimitsUpdate"));
    }

    [Fact]
    public async Task Apply_CpuAndMemory_SendsServiceInstanceLimitsUpdateAfterImageUpdate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceLimitsUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].Cpu = 1;
        plan.Services[0].MemoryGb = 2;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        Assert.Equal(1, handler.Count("serviceInstanceLimitsUpdate"));
        var updateIndex = handler.Operations.IndexOf("serviceInstanceUpdate");
        var limitsIndex = handler.Operations.IndexOf("serviceInstanceLimitsUpdate");
        Assert.True(updateIndex >= 0 && limitsIndex == updateIndex + 1);

        var updateInput = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("ghcr.io/example/api:placeholder", updateInput.GetProperty("source").GetProperty("image").GetString());
        Assert.False(updateInput.TryGetProperty("vCPUs", out _));
        Assert.False(updateInput.TryGetProperty("memoryGB", out _));

        var limitsInput = GraphQLFixtures.GetServiceInstanceLimitsUpdateInput(handler.Bodies);
        Assert.Equal(GraphQLFixtures.ApiServiceId, limitsInput.GetProperty("serviceId").GetString());
        Assert.Equal(GraphQLFixtures.ProductionEnvironmentId, limitsInput.GetProperty("environmentId").GetString());
        Assert.Equal(1, limitsInput.GetProperty("vCPUs").GetDouble());
        Assert.Equal(2, limitsInput.GetProperty("memoryGB").GetDouble());
        Assert.False(limitsInput.TryGetProperty("source", out _));
    }

    [Fact]
    public async Task Apply_CpuOnly_OmitsUnsetMemoryGb()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceLimitsUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].Cpu = 0.5;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var limitsInput = GraphQLFixtures.GetServiceInstanceLimitsUpdateInput(handler.Bodies);
        Assert.Equal(0.5, limitsInput.GetProperty("vCPUs").GetDouble());
        Assert.False(limitsInput.TryGetProperty("memoryGB", out _));
    }

    [Fact]
    public async Task Apply_WithoutCpuMemory_DoesNotCallLimitsUpdate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            GraphQLFixtures.CreatePlan(),
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        Assert.Equal(0, handler.Count("serviceInstanceLimitsUpdate"));
        Assert.DoesNotContain(
            handler.Bodies,
            body => body.Contains("serviceInstanceLimitsUpdate", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0d, null)]
    [InlineData(-1d, null)]
    [InlineData(null, 0d)]
    [InlineData(null, -2d)]
    public async Task Apply_InvalidCpuOrMemory_FailsBeforeGraphQL(double? cpu, double? memoryGb)
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].Cpu = cpu;
        plan.Services[0].MemoryGb = memoryGb;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("must be greater than 0", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("24", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceLimitsUpdate"));
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
        Assert.Equal(0, handler.Count("projectCreate"));
    }

    [Fact]
    public async Task Apply_LimitsGraphQLError_FailsHonestly()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceLimitsUpdate", GraphQLFixtures.GraphQLError("over plan vCPU limit"));

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].Cpu = 32;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("serviceInstanceLimitsUpdate", exception.Message, StringComparison.Ordinal);
        Assert.Contains("over plan vCPU limit", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        Assert.Equal(1, handler.Count("serviceInstanceLimitsUpdate"));
        Assert.Equal(0, handler.Count("serviceInstanceDeployV2"));
    }

    [Fact]
    public async Task Apply_WithReplicasOnly_SendsNumReplicasForCurrentRegion()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].Replicas = 2;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("ghcr.io/example/api:placeholder", input.GetProperty("source").GetProperty("image").GetString());
        Assert.Equal(2, input.GetProperty("numReplicas").GetInt32());
        Assert.False(input.TryGetProperty("multiRegionConfig", out _));
        Assert.False(input.TryGetProperty("sleepApplication", out _));
    }

    [Fact]
    public async Task Apply_ReplicaRegions_WinsOverWithReplicasAndOmitsNumReplicas()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].Replicas = 2;
        plan.Services[0].Region = "us-west2";
        plan.Services[0].ReplicaRegions = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["us-west2"] = 2,
            ["europe-west4-drams3a"] = 1
        };

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        var multiRegion = input.GetProperty("multiRegionConfig");
        Assert.Equal(2, multiRegion.GetProperty("us-west2").GetProperty("numReplicas").GetInt32());
        Assert.Equal(1, multiRegion.GetProperty("europe-west4-drams3a").GetProperty("numReplicas").GetInt32());
        Assert.False(input.TryGetProperty("numReplicas", out _));
        Assert.False(input.TryGetProperty("region", out _));
    }

    [Fact]
    public async Task Apply_ReplicaCountZero_FailsBeforeGraphQL()
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].Replicas = 0;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("at least 1", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
    }

    [Fact]
    public async Task Apply_TotalReplicasAboveCap_FailsBeforeGraphQL()
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].ReplicaRegions = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["us-west2"] = 50,
            ["europe-west4-drams3a"] = 1
        };

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("50", exception.Message, StringComparison.Ordinal);
        Assert.Contains("docs.railway.com/cli/scale", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
    }

    [Fact]
    public async Task Apply_VolumeBackedManagedServiceScale_FailsBeforeGraphQL()
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan(includePostgres: true);
        plan.Services[0].Name = "postgres";
        plan.Services[0].Replicas = 2;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("volume-backed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("docs.railway.com/volumes/reference", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
        Assert.Equal(0, handler.Count("serviceInstanceLimitsUpdate"));
    }

    [Fact]
    public async Task Apply_VolumeBackedManagedServiceLimits_FailsBeforeGraphQL()
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan(includePostgres: true);
        plan.Services[0].Name = "postgres";
        plan.Services[0].Cpu = 1;
        plan.Services[0].MemoryGb = 2;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("managed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("serviceInstanceLimitsUpdate", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceLimitsUpdate"));
    }

    [Fact]
    public async Task Apply_UnknownRegion_FailsBeforeGraphQL()
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].Region = "not-a-railway-region";

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("not-a-railway-region", exception.Message, StringComparison.Ordinal);
        Assert.Contains("us-west2", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
        Assert.Equal(0, handler.Count("projectCreate"));
    }

    [Fact]
    public async Task Apply_TemplatesAndBucket_DoNotSendScaleOnManagedServices()
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

        var plan = GraphQLFixtures.CreatePlan(includePostgres: true, includeRedis: true, includeBucket: true);
        plan.Services[0].Replicas = 2;
        plan.Services[0].Region = "us-west2";
        plan.Services[0].Serverless = true;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var result = await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Contains("postgres", result.AppliedTemplateCodes);
        Assert.Contains("redis", result.AppliedTemplateCodes);
        Assert.Equal(GraphQLFixtures.BucketId, result.BucketIds["uploads"]);
        Assert.Equal(2, handler.Count("templateDeployV2"));
        Assert.Equal(1, handler.Count("bucketCreate"));
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));

        var updateInput = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal(
            2,
            updateInput.GetProperty("multiRegionConfig").GetProperty("us-west2").GetProperty("numReplicas").GetInt32());
        Assert.True(updateInput.GetProperty("sleepApplication").GetBoolean());

        var templateBodies = handler.Bodies.Where(body =>
            body.Contains("\"operationName\":\"templateDeployV2\"", StringComparison.Ordinal) ||
            body.Contains("\"operationName\":\"bucketCreate\"", StringComparison.Ordinal));
        Assert.All(templateBodies, body =>
        {
            Assert.DoesNotContain("multiRegionConfig", body, StringComparison.Ordinal);
            Assert.DoesNotContain("sleepApplication", body, StringComparison.Ordinal);
            Assert.DoesNotContain("serviceInstanceLimitsUpdate", body, StringComparison.Ordinal);
            Assert.DoesNotContain("vCPUs", body, StringComparison.Ordinal);
            Assert.DoesNotContain("memoryGB", body, StringComparison.Ordinal);
        });
        Assert.Equal(0, handler.Count("serviceInstanceLimitsUpdate"));
    }

    [Fact]
    public async Task DeployAsync_WithReplicas_SendsNumReplicasFromGetReplicaCount()
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
        var api = builder.AddContainer("api", "nginx").WithAnnotation(new ReplicaAnnotation(2));

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

        Assert.Equal(2, api.Resource.GetReplicaCount());
        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal(api.Resource.GetReplicaCount(), input.GetProperty("numReplicas").GetInt32());
        Assert.False(input.TryGetProperty("multiRegionConfig", out _));
    }

    [Fact]
    public async Task DeployAsync_PublishAsRailwayServiceRegion_SendsMultiRegionConfig()
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
            .WithAnnotation(new ReplicaAnnotation(2))
            .PublishAsRailwayService(s =>
            {
                s.Region = RailwayRegion.EuropeWest4;
                s.Serverless = true;
            });

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

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal(
            2,
            input.GetProperty("multiRegionConfig").GetProperty("europe-west4-drams3a").GetProperty("numReplicas").GetInt32());
        Assert.True(input.GetProperty("sleepApplication").GetBoolean());
        Assert.False(input.TryGetProperty("numReplicas", out _));
    }

    [Fact]
    public async Task DeployAsync_PublishAsRailwayServiceReplicaRegions_SendsOfficialRegionIds()
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
            .PublishAsRailwayService(s =>
            {
                s.ReplicaRegions = new()
                {
                    [RailwayRegion.UsWest2] = 2,
                    [RailwayRegion.EuropeWest4] = 1
                };
            });

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

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        var multiRegion = input.GetProperty("multiRegionConfig");
        Assert.Equal(2, multiRegion.GetProperty("us-west2").GetProperty("numReplicas").GetInt32());
        Assert.Equal(1, multiRegion.GetProperty("europe-west4-drams3a").GetProperty("numReplicas").GetInt32());
        Assert.False(input.TryGetProperty("numReplicas", out _));
        Assert.DoesNotContain("sjc", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
        Assert.DoesNotContain("us-west1", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
        Assert.DoesNotContain("europe-west4\"", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task DeployAsync_PublishAsRailwayServiceLimits_SendsServiceInstanceLimitsUpdate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceLimitsUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var builder = TestAppBuilder.CreatePublish();
        builder.Configuration["RAILWAY_TOKEN"] = GraphQLFixtures.Token;
        var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io");
        var railway = builder.AddRailwayEnvironment("railway").WithContainerRegistry(ghcr);
        builder.AddContainer("api", "nginx")
            .WithAnnotation(new ReplicaAnnotation(2))
            .PublishAsRailwayService(s =>
            {
                s.Region = RailwayRegion.EuropeWest4;
                s.Cpu = 1;
                s.MemoryGb = 2;
            });

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

        Assert.Equal(1, handler.Count("serviceInstanceLimitsUpdate"));
        var limitsInput = GraphQLFixtures.GetServiceInstanceLimitsUpdateInput(handler.Bodies);
        Assert.Equal(GraphQLFixtures.ApiServiceId, limitsInput.GetProperty("serviceId").GetString());
        Assert.Equal(GraphQLFixtures.ProductionEnvironmentId, limitsInput.GetProperty("environmentId").GetString());
        Assert.Equal(1, limitsInput.GetProperty("vCPUs").GetDouble());
        Assert.Equal(2, limitsInput.GetProperty("memoryGB").GetDouble());
    }

    [Fact]
    public async Task Apply_HealthcheckPathOnly_SendsPathAndOmitsTimeout()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].HealthcheckPath = "/health";

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("/health", input.GetProperty("healthcheckPath").GetString());
        Assert.False(input.TryGetProperty("healthcheckTimeout", out _));
        Assert.DoesNotContain("RAILWAY_HEALTHCHECK_TIMEOUT_SEC", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        Assert.Equal(0, handler.Count("environmentPatchCommit"));
    }

    [Fact]
    public async Task Apply_HealthcheckTimeout_SendsIntOnServiceInstanceUpdate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].HealthcheckPath = "/health";
        plan.Services[0].HealthcheckTimeout = 120;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("/health", input.GetProperty("healthcheckPath").GetString());
        Assert.Equal(120, input.GetProperty("healthcheckTimeout").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Number, input.GetProperty("healthcheckTimeout").ValueKind);
        Assert.False(input.TryGetProperty("RAILWAY_HEALTHCHECK_TIMEOUT_SEC", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Apply_InvalidHealthcheckTimeout_FailsBeforeGraphQL(int timeout)
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].HealthcheckTimeout = timeout;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("healthcheckTimeout", exception.Message, StringComparison.Ordinal);
        Assert.Contains("greater than 0", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
    }

    [Fact]
    public async Task Apply_ManagedServiceHealthcheck_FailsBeforeGraphQL()
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan(includePostgres: true);
        plan.Services[0].Name = "postgres";
        plan.Services[0].HealthcheckPath = "/health";

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("managed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("healthcheckPath", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
    }

    [Fact]
    public async Task Apply_TemplatesAndBucket_DoNotSendHealthcheckOnManagedServices()
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

        var plan = GraphQLFixtures.CreatePlan(includePostgres: true, includeRedis: true, includeBucket: true);
        plan.Services[0].HealthcheckPath = "/health";
        plan.Services[0].HealthcheckTimeout = 90;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        var updateInput = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("/health", updateInput.GetProperty("healthcheckPath").GetString());
        Assert.Equal(90, updateInput.GetProperty("healthcheckTimeout").GetInt32());

        var managedBodies = handler.Bodies.Where(body =>
            body.Contains("\"operationName\":\"templateDeployV2\"", StringComparison.Ordinal) ||
            body.Contains("\"operationName\":\"bucketCreate\"", StringComparison.Ordinal) ||
            body.Contains("\"operationName\":\"bucketS3Credentials\"", StringComparison.Ordinal));
        Assert.All(managedBodies, body =>
        {
            Assert.DoesNotContain("healthcheckPath", body, StringComparison.Ordinal);
            Assert.DoesNotContain("healthcheckTimeout", body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task DeployAsync_WithHttpHealthCheck_SendsHealthcheckPathFromAnnotation()
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
            .WithHttpEndpoint(targetPort: 80)
            .WithHttpHealthCheck("/health");

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

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("/health", input.GetProperty("healthcheckPath").GetString());
        Assert.False(input.TryGetProperty("healthcheckTimeout", out _));
        Assert.Contains("environmentId", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployAsync_HealthcheckTimeout_SendsIntOnExistingMutation()
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
            .WithHttpEndpoint(targetPort: 80)
            .WithHttpHealthCheck("/health")
            .PublishAsRailwayService(s => s.HealthcheckTimeoutSeconds = 120);

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

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("/health", input.GetProperty("healthcheckPath").GetString());
        Assert.Equal(120, input.GetProperty("healthcheckTimeout").GetInt32());
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        Assert.DoesNotContain(
            handler.Operations,
            name => name.Contains("healthcheck", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "serviceInstanceUpdate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_RestartPolicyTypeOnly_SendsTypeAndOmitsRetries()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].RestartPolicyType = "ALWAYS";

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("ALWAYS", input.GetProperty("restartPolicyType").GetString());
        Assert.False(input.TryGetProperty("restartPolicyMaxRetries", out _));
        Assert.DoesNotContain("null", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        Assert.Equal(0, handler.Count("environmentPatchCommit"));
    }

    [Fact]
    public async Task Apply_RestartPolicyMaxRetries_SendsIntOnServiceInstanceUpdate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].RestartPolicyType = "ON_FAILURE";
        plan.Services[0].RestartPolicyMaxRetries = 10;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("ON_FAILURE", input.GetProperty("restartPolicyType").GetString());
        Assert.Equal(10, input.GetProperty("restartPolicyMaxRetries").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Number, input.GetProperty("restartPolicyMaxRetries").ValueKind);
        Assert.False(input.TryGetProperty("healthcheckPath", out _));
    }

    [Fact]
    public async Task Apply_RestartPolicyWithHealthcheck_DoesNotDropLaterFields()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].HealthcheckPath = "/health";
        plan.Services[0].HealthcheckTimeout = 90;
        plan.Services[0].RestartPolicyType = "NEVER";
        plan.Services[0].RestartPolicyMaxRetries = 1;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("/health", input.GetProperty("healthcheckPath").GetString());
        Assert.Equal(90, input.GetProperty("healthcheckTimeout").GetInt32());
        Assert.Equal("NEVER", input.GetProperty("restartPolicyType").GetString());
        Assert.Equal(1, input.GetProperty("restartPolicyMaxRetries").GetInt32());
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Apply_InvalidRestartPolicyMaxRetries_FailsBeforeGraphQL(int retries)
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].RestartPolicyMaxRetries = retries;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("restartPolicyMaxRetries", exception.Message, StringComparison.Ordinal);
        Assert.Contains("greater than 0", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
    }

    [Fact]
    public async Task Apply_ManagedServiceRestartPolicy_FailsBeforeGraphQL()
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan(includePostgres: true);
        plan.Services[0].Name = "postgres";
        plan.Services[0].RestartPolicyType = "ALWAYS";

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("managed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restartPolicyType", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
    }

    [Fact]
    public async Task Apply_TemplatesAndBucket_DoNotSendRestartPolicyOnManagedServices()
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

        var plan = GraphQLFixtures.CreatePlan(includePostgres: true, includeRedis: true, includeBucket: true);
        plan.Services[0].RestartPolicyType = "ON_FAILURE";
        plan.Services[0].RestartPolicyMaxRetries = 4;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        var updateInput = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("ON_FAILURE", updateInput.GetProperty("restartPolicyType").GetString());
        Assert.Equal(4, updateInput.GetProperty("restartPolicyMaxRetries").GetInt32());

        var managedBodies = handler.Bodies.Where(body =>
            body.Contains("\"operationName\":\"templateDeployV2\"", StringComparison.Ordinal) ||
            body.Contains("\"operationName\":\"bucketCreate\"", StringComparison.Ordinal) ||
            body.Contains("\"operationName\":\"bucketS3Credentials\"", StringComparison.Ordinal));
        Assert.All(managedBodies, body =>
        {
            Assert.DoesNotContain("restartPolicyType", body, StringComparison.Ordinal);
            Assert.DoesNotContain("restartPolicyMaxRetries", body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task DeployAsync_RestartPolicy_SendsFieldsOnExistingMutation()
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
            .PublishAsRailwayService(s =>
            {
                s.RestartPolicy = RailwayRestartPolicy.OnFailure;
                s.RestartPolicyMaxRetries = 10;
            });

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

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("ON_FAILURE", input.GetProperty("restartPolicyType").GetString());
        Assert.Equal(10, input.GetProperty("restartPolicyMaxRetries").GetInt32());
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        Assert.DoesNotContain("null", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            handler.Operations,
            name => name.Contains("restart", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "serviceInstanceUpdate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_StartCommandOnly_SendsStringAndOmitsPreDeploy()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].StartCommand = "./api";

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("./api", input.GetProperty("startCommand").GetString());
        Assert.False(input.TryGetProperty("preDeployCommand", out _));
        Assert.DoesNotContain("null", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        Assert.Equal(0, handler.Count("environmentPatchCommit"));
    }

    [Fact]
    public async Task Apply_PreDeployCommand_SendsOneElementArrayOnServiceInstanceUpdate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].PreDeployCommand = ["dotnet MyApp.dll --migrate"];

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.False(input.TryGetProperty("startCommand", out _));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, input.GetProperty("preDeployCommand").ValueKind);
        Assert.Equal(1, input.GetProperty("preDeployCommand").GetArrayLength());
        Assert.Equal("dotnet MyApp.dll --migrate", input.GetProperty("preDeployCommand")[0].GetString());
    }

    [Fact]
    public async Task Apply_EmptyPreDeployArray_OmitsField()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].PreDeployCommand = [];

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.False(input.TryGetProperty("startCommand", out _));
        Assert.False(input.TryGetProperty("preDeployCommand", out _));
        Assert.DoesNotContain("null", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Apply_HealthcheckRestartAndStart_DoesNotDropLaterFields()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].HealthcheckPath = "/health";
        plan.Services[0].HealthcheckTimeout = 90;
        plan.Services[0].RestartPolicyType = "NEVER";
        plan.Services[0].RestartPolicyMaxRetries = 1;
        plan.Services[0].StartCommand = "./api";
        plan.Services[0].PreDeployCommand = ["dotnet MyApp.dll --migrate"];
        plan.Services[0].OverlapSeconds = 60;
        plan.Services[0].DrainingSeconds = 10;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("/health", input.GetProperty("healthcheckPath").GetString());
        Assert.Equal(90, input.GetProperty("healthcheckTimeout").GetInt32());
        Assert.Equal("NEVER", input.GetProperty("restartPolicyType").GetString());
        Assert.Equal(1, input.GetProperty("restartPolicyMaxRetries").GetInt32());
        Assert.Equal("./api", input.GetProperty("startCommand").GetString());
        Assert.Equal("dotnet MyApp.dll --migrate", input.GetProperty("preDeployCommand")[0].GetString());
        Assert.Equal(60, input.GetProperty("overlapSeconds").GetInt32());
        Assert.Equal(10, input.GetProperty("drainingSeconds").GetInt32());
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Apply_InvalidStartCommand_FailsBeforeGraphQL(string startCommand)
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].StartCommand = startCommand;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("startCommand", exception.Message, StringComparison.Ordinal);
        Assert.Contains("non-empty", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Apply_InvalidPreDeployCommand_FailsBeforeGraphQL(string step)
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].PreDeployCommand = [step];

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("preDeployCommand", exception.Message, StringComparison.Ordinal);
        Assert.Contains("non-empty", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
    }

    [Fact]
    public async Task Apply_ManagedServiceStartCommand_FailsBeforeGraphQL()
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan(includePostgres: true);
        plan.Services[0].Name = "postgres";
        plan.Services[0].StartCommand = "./api";

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("managed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("startCommand", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
    }

    [Fact]
    public async Task Apply_TemplatesAndBucket_DoNotSendStartOrPreDeployOnManagedServices()
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

        var plan = GraphQLFixtures.CreatePlan(includePostgres: true, includeRedis: true, includeBucket: true);
        plan.Services[0].StartCommand = "./api";
        plan.Services[0].PreDeployCommand = ["dotnet MyApp.dll --migrate"];

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        var updateInput = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("./api", updateInput.GetProperty("startCommand").GetString());
        Assert.Equal("dotnet MyApp.dll --migrate", updateInput.GetProperty("preDeployCommand")[0].GetString());

        var managedBodies = handler.Bodies.Where(body =>
            body.Contains("\"operationName\":\"templateDeployV2\"", StringComparison.Ordinal) ||
            body.Contains("\"operationName\":\"bucketCreate\"", StringComparison.Ordinal) ||
            body.Contains("\"operationName\":\"bucketS3Credentials\"", StringComparison.Ordinal));
        Assert.All(managedBodies, body =>
        {
            Assert.DoesNotContain("startCommand", body, StringComparison.Ordinal);
            Assert.DoesNotContain("preDeployCommand", body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task DeployAsync_StartAndPreDeployCommand_SendsFieldsOnExistingMutation()
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
            .PublishAsRailwayService(s =>
            {
                s.StartCommand = "/bin/sh -c \"exec ./api\"";
                s.PreDeployCommand = "dotnet MyApp.dll --migrate";
            });

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

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal("/bin/sh -c \"exec ./api\"", input.GetProperty("startCommand").GetString());
        Assert.Equal("dotnet MyApp.dll --migrate", input.GetProperty("preDeployCommand")[0].GetString());
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        Assert.DoesNotContain("null", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            handler.Operations,
            name => (name.Contains("start", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("preDeploy", StringComparison.OrdinalIgnoreCase)) &&
                    !string.Equals(name, "serviceInstanceUpdate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_OverlapSecondsOnly_SendsIntAndOmitsDraining()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].OverlapSeconds = 60;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal(System.Text.Json.JsonValueKind.Number, input.GetProperty("overlapSeconds").ValueKind);
        Assert.Equal(60, input.GetProperty("overlapSeconds").GetInt32());
        Assert.False(input.TryGetProperty("drainingSeconds", out _));
        Assert.DoesNotContain("null", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
        Assert.DoesNotContain("RAILWAY_DEPLOYMENT_OVERLAP_SECONDS", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        Assert.Equal(0, handler.Count("environmentPatchCommit"));
    }

    [Fact]
    public async Task Apply_DrainingSeconds_SendsIntOnServiceInstanceUpdate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].DrainingSeconds = 10;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.False(input.TryGetProperty("overlapSeconds", out _));
        Assert.Equal(System.Text.Json.JsonValueKind.Number, input.GetProperty("drainingSeconds").ValueKind);
        Assert.Equal(10, input.GetProperty("drainingSeconds").GetInt32());
        Assert.DoesNotContain("RAILWAY_DEPLOYMENT_DRAINING_SECONDS", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Apply_ZeroOverlapAndDraining_SendsZeroInts()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].OverlapSeconds = 0;
        plan.Services[0].DrainingSeconds = 0;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal(System.Text.Json.JsonValueKind.Number, input.GetProperty("overlapSeconds").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Number, input.GetProperty("drainingSeconds").ValueKind);
        Assert.Equal(0, input.GetProperty("overlapSeconds").GetInt32());
        Assert.Equal(0, input.GetProperty("drainingSeconds").GetInt32());
        Assert.DoesNotContain("null", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-60)]
    public async Task Apply_InvalidOverlapSeconds_FailsBeforeGraphQL(int seconds)
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].OverlapSeconds = seconds;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("overlapSeconds", exception.Message, StringComparison.Ordinal);
        Assert.Contains("greater than or equal to 0", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    public async Task Apply_InvalidDrainingSeconds_FailsBeforeGraphQL(int seconds)
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan();
        plan.Services[0].DrainingSeconds = seconds;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("drainingSeconds", exception.Message, StringComparison.Ordinal);
        Assert.Contains("greater than or equal to 0", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
    }

    [Fact]
    public async Task Apply_ManagedServiceOverlap_FailsBeforeGraphQL()
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan(includePostgres: true);
        plan.Services[0].Name = "postgres";
        plan.Services[0].OverlapSeconds = 60;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("managed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overlapSeconds", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
        Assert.Equal(0, handler.Count("serviceInstanceUpdate"));
    }

    [Fact]
    public async Task Apply_TemplatesAndBucket_DoNotSendOverlapOrDrainingOnManagedServices()
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

        var plan = GraphQLFixtures.CreatePlan(includePostgres: true, includeRedis: true, includeBucket: true);
        plan.Services[0].OverlapSeconds = 60;
        plan.Services[0].DrainingSeconds = 10;

        var apply = GraphQLFixtures.CreateApplyService(handler);
        await apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        var updateInput = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal(60, updateInput.GetProperty("overlapSeconds").GetInt32());
        Assert.Equal(10, updateInput.GetProperty("drainingSeconds").GetInt32());

        var managedBodies = handler.Bodies.Where(body =>
            body.Contains("\"operationName\":\"templateDeployV2\"", StringComparison.Ordinal) ||
            body.Contains("\"operationName\":\"bucketCreate\"", StringComparison.Ordinal) ||
            body.Contains("\"operationName\":\"bucketS3Credentials\"", StringComparison.Ordinal));
        Assert.All(managedBodies, body =>
        {
            Assert.DoesNotContain("overlapSeconds", body, StringComparison.Ordinal);
            Assert.DoesNotContain("drainingSeconds", body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task DeployAsync_OverlapAndDraining_SendsFieldsOnExistingMutation()
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
            .PublishAsRailwayService(s =>
            {
                s.OverlapSeconds = 60;
                s.DrainingSeconds = 10;
            });

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

        var input = GraphQLFixtures.GetServiceInstanceUpdateInput(handler.Bodies);
        Assert.Equal(System.Text.Json.JsonValueKind.Number, input.GetProperty("overlapSeconds").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Number, input.GetProperty("drainingSeconds").ValueKind);
        Assert.Equal(60, input.GetProperty("overlapSeconds").GetInt32());
        Assert.Equal(10, input.GetProperty("drainingSeconds").GetInt32());
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        Assert.DoesNotContain("null", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
        Assert.DoesNotContain("RAILWAY_DEPLOYMENT_OVERLAP_SECONDS", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
        Assert.DoesNotContain("RAILWAY_DEPLOYMENT_DRAINING_SECONDS", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            handler.Operations,
            name => (name.Contains("overlap", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("drain", StringComparison.OrdinalIgnoreCase)) &&
                    !string.Equals(name, "serviceInstanceUpdate", StringComparison.Ordinal));
    }

    private static void EnqueueProductionServiceTemplateAndBucket(ScriptedGraphQLHandler handler)
    {
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("template", GraphQLFixtures.TemplatePostgres);
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
