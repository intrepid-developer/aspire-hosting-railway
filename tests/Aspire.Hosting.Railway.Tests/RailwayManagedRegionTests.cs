using System.Text.Json;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Railway.Storage;

namespace Aspire.Hosting.Railway.Tests;

public sealed class RailwayManagedRegionTests
{
    [Fact]
    public void Plan_AddRailwayBucket_UnsetRegion_OmitsPlanField()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddRailwayBucket("uploads");
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var uploads = Assert.Single(plan.ManagedServices, managed => managed.Kind == "bucket");
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Null(uploads.Region);
        Assert.DoesNotContain("\"region\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_AddRailwayBucket_ConfigureAms_WritesTigrisCode()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddRailwayBucket("uploads", configure: bucket => bucket.Region = RailwayBucketRegion.Ams);
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var uploads = Assert.Single(plan.ManagedServices, managed => managed.Kind == "bucket");

        Assert.Equal("ams", uploads.Region);
        Assert.Contains("\"region\": \"ams\"", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("europe-west4", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_AddRailwayBucket_WithRegionAms_WritesTigrisCode()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddRailwayBucket("uploads").WithRegion(RailwayBucketRegion.Ams);
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var uploads = Assert.Single(plan.ManagedServices, managed => managed.Kind == "bucket");

        Assert.Equal("ams", uploads.Region);
        Assert.Contains("\"region\": \"ams\"", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_PublishAsRailwayPostgres_EuropeWest4_WritesComputeRegion()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres").PublishAsRailwayPostgres(postgres =>
        {
            postgres.Region = RailwayRegion.EuropeWest4;
            postgres.VolumeBackupDaily = true;
        });
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var postgres = Assert.Single(plan.ManagedServices, managed => managed.Kind == "postgres");

        Assert.Equal("europe-west4-drams3a", postgres.Region);
        Assert.Equal(["DAILY"], postgres.VolumeBackupScheduleKinds);
        Assert.Contains("europe-west4-drams3a", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("\"ams\"", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_PublishAsRailwayRedis_EuropeWest4_WritesComputeRegion()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddRedis("redis").PublishAsRailwayRedis(redis => redis.Region = RailwayRegion.EuropeWest4);
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var redis = Assert.Single(plan.ManagedServices, managed => managed.Kind == "redis");

        Assert.Equal("europe-west4-drams3a", redis.Region);
        Assert.Null(redis.VolumeBackupScheduleKinds);
    }

    [Theory]
    [InlineData("us-west2")]
    [InlineData("us-east4-eqdc4a")]
    [InlineData("europe-west4-drams3a")]
    [InlineData("asia-southeast1-eqsg3a")]
    public void ValidatePlan_ComputeIdOnBucket_FailsBeforeGraphQL(string region)
    {
        var plan = GraphQLFixtures.CreatePlan(includeBucket: true, includeApi: false);
        plan.ManagedServices[0].Region = region;

        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayManagedRegion.ValidatePlan(plan));

        Assert.Contains(region, exception.Message, StringComparison.Ordinal);
        Assert.Contains("iad", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ams", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("iad")]
    [InlineData("sjc")]
    [InlineData("ams")]
    [InlineData("sin")]
    public void ValidatePlan_TigrisCodeOnPostgres_FailsBeforeGraphQL(string region)
    {
        var plan = GraphQLFixtures.CreatePlan(includePostgres: true, includeApi: false);
        plan.ManagedServices[0].Region = region;

        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayManagedRegion.ValidatePlan(plan));

        Assert.Contains(region, exception.Message, StringComparison.Ordinal);
        Assert.Contains("us-west2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Airport codes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_BucketUnset_PatchesIad()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        GraphQLFixtures.EnqueueBucketCreateAndProvision(handler);
        handler.Enqueue("bucketS3Credentials", GraphQLFixtures.BucketCredentials);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan(includeApi: false, includeBucket: true);
        Assert.Null(plan.ManagedServices[0].Region);

        await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(includeApiImage: false),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        AssertBucketPatchRegion(handler, "iad");
        var createBody = handler.Bodies.Single(body =>
            body.Contains("\"operationName\":\"bucketCreate\"", StringComparison.Ordinal));
        Assert.DoesNotContain("\"region\"", createBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_BucketAms_PatchesAms()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        GraphQLFixtures.EnqueueBucketCreateAndProvision(handler);
        handler.Enqueue("bucketS3Credentials", GraphQLFixtures.BucketCredentials);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan(includeApi: false, includeBucket: true);
        plan.ManagedServices[0].Region = "ams";

        await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(includeApiImage: false),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        AssertBucketPatchRegion(handler, "ams");
        var stageBody = handler.Bodies.Single(body =>
            body.Contains("\"operationName\":\"environmentStageChanges\"", StringComparison.Ordinal) &&
            body.Contains("\"buckets\"", StringComparison.Ordinal));
        Assert.DoesNotContain("iad", stageBody, StringComparison.Ordinal);
        Assert.DoesNotContain("us-east4-eqdc4a", stageBody, StringComparison.Ordinal);
        Assert.DoesNotContain("europe-west4-drams3a", stageBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_AdoptedBucket_DoesNotRepatchRegion()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectWithExistingBucket);
        handler.Enqueue("bucketS3Credentials", GraphQLFixtures.BucketCredentials);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan(adoptExisting: true, includeApi: false, includeBucket: true);
        plan.ManagedServices[0].Region = "ams";

        await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(
                includeApiImage: false,
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(0, handler.Count("bucketCreate"));
        Assert.Equal(0, handler.Count("environmentStageChanges"));
        Assert.Equal(0, handler.Count("environmentPatchCommit"));
        Assert.Equal(1, handler.Count("bucketS3Credentials"));
    }

    [Fact]
    public async Task Apply_PostgresRegion_OnServiceInstanceUpdate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectWithExistingCanvas);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan(adoptExisting: true, includeApi: false, includePostgres: true);
        plan.ManagedServices[0].Region = "europe-west4-drams3a";

        await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(
                includeApiImage: false,
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        var update = Assert.Single(GraphQLFixtures.GetServiceInstanceUpdateVariables(handler.Bodies));
        Assert.Equal(GraphQLFixtures.PostgresServiceId, update.GetProperty("serviceId").GetString());
        Assert.Equal(GraphQLFixtures.ProductionEnvironmentId, update.GetProperty("environmentId").GetString());
        var input = update.GetProperty("input");
        Assert.Equal("europe-west4-drams3a", input.GetProperty("region").GetString());
        Assert.Equal(1, input.GetProperty("numReplicas").GetInt32());
        Assert.False(input.TryGetProperty("multiRegionConfig", out _));
        Assert.False(input.TryGetProperty("source", out _));
        Assert.DoesNotContain("\"ams\"", handler.Bodies.Single(body =>
            body.Contains("serviceInstanceUpdate", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal(0, handler.Count("templateDeployV2"));
    }

    [Fact]
    public async Task Apply_RedisRegion_OnServiceInstanceUpdate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("template", GraphQLFixtures.TemplateRedis);
        handler.Enqueue("templateDeployV2", GraphQLFixtures.TemplateDeployV2);
        handler.Enqueue("workflowStatus", GraphQLFixtures.WorkflowComplete);
        handler.Enqueue("project", GraphQLFixtures.ProjectQuery((GraphQLFixtures.RedisServiceId, "redis")));
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan(includeApi: false, includeRedis: true);
        plan.ManagedServices[0].Region = "europe-west4-drams3a";

        await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(includeApiImage: false),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(1, handler.Count("templateDeployV2"));
        Assert.Equal(1, handler.Count("serviceInstanceUpdate"));
        var update = Assert.Single(GraphQLFixtures.GetServiceInstanceUpdateVariables(handler.Bodies));
        Assert.Equal(GraphQLFixtures.RedisServiceId, update.GetProperty("serviceId").GetString());
        var input = update.GetProperty("input");
        Assert.Equal("europe-west4-drams3a", input.GetProperty("region").GetString());
        Assert.Equal(1, input.GetProperty("numReplicas").GetInt32());
        Assert.False(input.TryGetProperty("multiRegionConfig", out _));
        var deployBody = handler.Bodies.Single(body =>
            body.Contains("\"operationName\":\"templateDeployV2\"", StringComparison.Ordinal));
        Assert.DoesNotContain("region", deployBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_PostgresRegion_IsResentAfterVolumeBackupUpdate()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectWithExistingCanvas);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue(
            "environment",
            GraphQLFixtures.EnvironmentVolumeInstances((GraphQLFixtures.VolumeInstanceId, GraphQLFixtures.PostgresServiceId)));
        handler.Enqueue(
            "volumeInstanceBackupScheduleList",
            GraphQLFixtures.VolumeInstanceBackupScheduleList((GraphQLFixtures.WeeklyScheduleId, "WEEKLY")));
        handler.Enqueue("volumeInstanceBackupScheduleUpdate", GraphQLFixtures.VolumeInstanceBackupScheduleUpdate);
        handler.Enqueue(
            "volumeInstanceBackupScheduleList",
            GraphQLFixtures.VolumeInstanceBackupScheduleList(
                (GraphQLFixtures.DailyScheduleId, "DAILY"),
                (GraphQLFixtures.WeeklyScheduleId, "WEEKLY")));
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var plan = GraphQLFixtures.CreatePlan(adoptExisting: true, includeApi: false, includePostgres: true);
        plan.ManagedServices[0].Region = "europe-west4-drams3a";
        plan.ManagedServices[0].VolumeBackupScheduleKinds = ["DAILY"];

        await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(
                includeApiImage: false,
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(1, handler.Count("volumeInstanceBackupScheduleUpdate"));
        Assert.Equal(2, handler.Count("serviceInstanceUpdate"));
        var updates = GraphQLFixtures.GetServiceInstanceUpdateVariables(handler.Bodies);
        Assert.Equal(2, updates.Count);
        foreach (var update in updates)
        {
            Assert.Equal(GraphQLFixtures.PostgresServiceId, update.GetProperty("serviceId").GetString());
            Assert.Equal("europe-west4-drams3a", update.GetProperty("input").GetProperty("region").GetString());
            Assert.Equal(1, update.GetProperty("input").GetProperty("numReplicas").GetInt32());
            Assert.False(update.GetProperty("input").TryGetProperty("multiRegionConfig", out _));
        }

        var regionIndex = handler.Operations.IndexOf("serviceInstanceUpdate");
        var backupIndex = handler.Operations.IndexOf("volumeInstanceBackupScheduleUpdate");
        var resentIndex = handler.Operations.LastIndexOf("serviceInstanceUpdate");
        Assert.True(regionIndex < backupIndex);
        Assert.True(backupIndex < resentIndex);
    }

    [Fact]
    public async Task Apply_InvalidBucketRegion_FailsBeforeGraphQL()
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan(includeApi: false, includeBucket: true);
        plan.ManagedServices[0].Region = "europe-west4-drams3a";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
                plan,
                GraphQLFixtures.CreateRequest(includeApiImage: false),
                new RecordingReportingStep(),
                new MemoryDeploymentStateManager()));

        Assert.Contains("europe-west4-drams3a", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
    }

    private static void AssertBucketPatchRegion(ScriptedGraphQLHandler handler, string expectedRegion)
    {
        var stage = GraphQLFixtures.GetEnvironmentStageChangesVariables(handler.Bodies);
        Assert.Equal(expectedRegion, stage.GetProperty("input").GetProperty("buckets")
            .GetProperty(GraphQLFixtures.BucketId).GetProperty("region").GetString());
        var commit = GraphQLFixtures.GetEnvironmentPatchCommitVariables(handler.Bodies);
        Assert.Equal(expectedRegion, commit.GetProperty("patch").GetProperty("buckets")
            .GetProperty(GraphQLFixtures.BucketId).GetProperty("region").GetString());
    }
}
