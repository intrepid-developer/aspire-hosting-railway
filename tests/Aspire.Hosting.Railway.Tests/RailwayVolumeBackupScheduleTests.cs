using System.Text.Json;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Railway;

namespace Aspire.Hosting.Railway.Tests;

public sealed class RailwayVolumeBackupScheduleTests
{
    [Fact]
    public void Plan_NoArgPublishAsRailwayPostgres_OmitsVolumeBackupScheduleKinds()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var postgres = Assert.Single(plan.ManagedServices, managed => managed.Kind == "postgres");
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Null(postgres.VolumeBackupScheduleKinds);
        Assert.DoesNotContain("volumeBackupScheduleKinds", json, StringComparison.Ordinal);
        Assert.Equal("postgres", postgres.TemplateCode);
        Assert.Equal("DATABASE_URL", postgres.PrivateReferenceVariable);
    }

    [Fact]
    public void Plan_AllFalseCallback_OmitsVolumeBackupScheduleKinds()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres").PublishAsRailwayPostgres(s =>
        {
            s.VolumeBackupDaily = false;
            s.VolumeBackupWeekly = false;
            s.VolumeBackupMonthly = false;
        });
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var postgres = Assert.Single(plan.ManagedServices, managed => managed.Kind == "postgres");

        Assert.Null(postgres.VolumeBackupScheduleKinds);
        Assert.DoesNotContain("volumeBackupScheduleKinds", RailwayPlanBuilder.ToJson(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_DailyAndWeekly_CopiedAndMonthlyOmitted()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres").PublishAsRailwayPostgres(s =>
        {
            s.VolumeBackupDaily = true;
            s.VolumeBackupWeekly = true;
        });
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var postgres = Assert.Single(plan.ManagedServices, managed => managed.Kind == "postgres");
        var json = RailwayPlanBuilder.ToJson(plan);

        Assert.Equal(["DAILY", "WEEKLY"], postgres.VolumeBackupScheduleKinds);
        using var document = JsonDocument.Parse(json);
        var managed = document.RootElement.GetProperty("managedServices")[0];
        Assert.Equal("DAILY", managed.GetProperty("volumeBackupScheduleKinds")[0].GetString());
        Assert.Equal("WEEKLY", managed.GetProperty("volumeBackupScheduleKinds")[1].GetString());
        Assert.Equal(2, managed.GetProperty("volumeBackupScheduleKinds").GetArrayLength());
        Assert.DoesNotContain("MONTHLY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("WAL_ARCHIVE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("EnablePitr", json, StringComparison.Ordinal);
        Assert.DoesNotContain("proj_", json, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_RedisBucketsAndAppServices_DoNotGetVolumeBackupScheduleKinds()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres").PublishAsRailwayPostgres(s => s.VolumeBackupDaily = true);
        builder.AddRedis("redis").PublishAsRailwayRedis();
        builder.AddRailwayBucket("uploads");
        builder.AddContainer("api", "nginx")
            .WithExternalHttpEndpoints()
            .PublishAsRailwayService(s => s.CustomDomains.Add("api.example.com"));

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var json = RailwayPlanBuilder.ToJson(plan);

        var postgres = Assert.Single(plan.ManagedServices, managed => managed.Kind == "postgres");
        Assert.Equal(["DAILY"], postgres.VolumeBackupScheduleKinds);

        var redis = Assert.Single(plan.ManagedServices, managed => managed.Kind == "redis");
        var uploads = Assert.Single(plan.ManagedServices, managed => managed.Kind == "bucket");
        Assert.Null(redis.VolumeBackupScheduleKinds);
        Assert.Null(uploads.VolumeBackupScheduleKinds);
        Assert.Single(plan.Services);

        using (var document = JsonDocument.Parse(json))
        {
            foreach (var managed in document.RootElement.GetProperty("managedServices").EnumerateArray())
            {
                var kind = managed.GetProperty("kind").GetString();
                if (string.Equals(kind, "postgres", StringComparison.Ordinal))
                {
                    Assert.Equal("DAILY", managed.GetProperty("volumeBackupScheduleKinds")[0].GetString());
                }
                else
                {
                    Assert.False(managed.TryGetProperty("volumeBackupScheduleKinds", out _));
                }
            }

            Assert.False(document.RootElement.GetProperty("services")[0]
                .TryGetProperty("volumeBackupScheduleKinds", out _));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("daily")]
    [InlineData("HOURLY")]
    [InlineData("WAL_ARCHIVE_DAILY")]
    public void Plan_InvalidKindString_FailsBeforeGraphQL(string kind)
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddContainer("api", "nginx");

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        plan.ManagedServices[0].VolumeBackupScheduleKinds = [kind];

        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayVolumeBackupSchedule.ValidatePlan(plan));

        Assert.Contains("VolumeInstanceBackupScheduleKind", exception.Message, StringComparison.Ordinal);
        Assert.Contains("DAILY", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatePlan_BucketKinds_FailsHonestly()
    {
        var plan = GraphQLFixtures.CreatePlan(includeBucket: true, includeApi: false);
        plan.ManagedServices[0].VolumeBackupScheduleKinds = ["DAILY"];

        var exception = Assert.Throws<InvalidOperationException>(
            () => RailwayVolumeBackupSchedule.ValidatePlan(plan));

        Assert.Contains("bucket", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("volumeBackupScheduleKinds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_ListsThenUpdatesWithUnion()
    {
        var handler = new ScriptedGraphQLHandler();
        EnqueueAdoptedPostgresWithSchedules(
            handler,
            listThen: GraphQLFixtures.VolumeInstanceBackupScheduleList(
                (GraphQLFixtures.WeeklyScheduleId, "WEEKLY")),
            update: true,
            listAfter: GraphQLFixtures.VolumeInstanceBackupScheduleList(
                (GraphQLFixtures.DailyScheduleId, "DAILY"),
                (GraphQLFixtures.WeeklyScheduleId, "WEEKLY")));

        var plan = GraphQLFixtures.CreatePlan(adoptExisting: true, includePostgres: true);
        plan.ManagedServices[0].VolumeBackupScheduleKinds = ["DAILY", "WEEKLY"];
        var reporter = new RecordingReportingStep();
        var state = new MemoryDeploymentStateManager();

        var result = await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            reporter,
            state);

        Assert.Equal(2, handler.Count("volumeInstanceBackupScheduleList"));
        Assert.Equal(1, handler.Count("volumeInstanceBackupScheduleUpdate"));
        Assert.Equal(0, handler.Count("volumeInstanceBackupCreate"));
        Assert.Equal(0, handler.Count("pluginCreate"));
        Assert.Equal(0, handler.Count("enablePitrForHaCluster"));
        Assert.Equal(GraphQLFixtures.VolumeInstanceId, result.VolumeInstanceIds["postgres"]);
        Assert.Equal(GraphQLFixtures.DailyScheduleId, result.VolumeBackupScheduleIds["postgres-DAILY"]);
        Assert.Equal(GraphQLFixtures.WeeklyScheduleId, result.VolumeBackupScheduleIds["postgres-WEEKLY"]);

        var variables = GraphQLFixtures.GetVolumeInstanceBackupScheduleUpdateVariables(handler.Bodies);
        Assert.Equal(GraphQLFixtures.VolumeInstanceId, variables.GetProperty("volumeInstanceId").GetString());
        Assert.Equal("DAILY", variables.GetProperty("kinds")[0].GetString());
        Assert.Equal("WEEKLY", variables.GetProperty("kinds")[1].GetString());
        Assert.Equal(2, variables.GetProperty("kinds").GetArrayLength());
        Assert.False(variables.TryGetProperty("input", out _));
        Assert.DoesNotContain("null", handler.Bodies.Single(body =>
            body.Contains("volumeInstanceBackupScheduleUpdate", StringComparison.Ordinal)));
        Assert.Contains("VolumeInstanceBackupScheduleKind", RailwayGraphQLOperations.VolumeInstanceBackupScheduleUpdate, StringComparison.Ordinal);
        Assert.Contains(reporter.Completions, text =>
            text.Contains("DAILY", StringComparison.Ordinal) &&
            text.Contains("WEEKLY", StringComparison.Ordinal));

        var snapshot = await RailwayDeploymentStateStore.LoadAsync(state, "railway", "production", CancellationToken.None);
        Assert.Equal(GraphQLFixtures.VolumeInstanceId, snapshot.VolumeInstanceIds["postgres"]);
        Assert.Equal(GraphQLFixtures.DailyScheduleId, snapshot.VolumeBackupScheduleIds["postgres-DAILY"]);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(snapshot.VolumeInstanceIds), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Apply_SkipWhenRequestedKindsAlreadyPresent()
    {
        var handler = new ScriptedGraphQLHandler();
        EnqueueAdoptedPostgresWithSchedules(
            handler,
            listThen: GraphQLFixtures.VolumeInstanceBackupScheduleList(
                (GraphQLFixtures.DailyScheduleId, "DAILY"),
                (GraphQLFixtures.WeeklyScheduleId, "WEEKLY"),
                (GraphQLFixtures.MonthlyScheduleId, "MONTHLY")),
            update: false);

        var plan = GraphQLFixtures.CreatePlan(adoptExisting: true, includePostgres: true);
        plan.ManagedServices[0].VolumeBackupScheduleKinds = ["DAILY", "WEEKLY"];

        var result = await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(1, handler.Count("environment"));
        Assert.Equal(1, handler.Count("volumeInstanceBackupScheduleList"));
        Assert.Equal(0, handler.Count("volumeInstanceBackupScheduleUpdate"));
        Assert.Equal(GraphQLFixtures.VolumeInstanceId, result.VolumeInstanceIds["postgres"]);
        Assert.Equal(GraphQLFixtures.DailyScheduleId, result.VolumeBackupScheduleIds["postgres-DAILY"]);
        Assert.Equal(GraphQLFixtures.WeeklyScheduleId, result.VolumeBackupScheduleIds["postgres-WEEKLY"]);
    }

    [Fact]
    public async Task Apply_NoVolumeInstance_FailsHonestly()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectWithExistingCanvas);
        handler.Enqueue("environment", GraphQLFixtures.EnvironmentVolumeInstancesEmpty);

        var plan = GraphQLFixtures.CreatePlan(adoptExisting: true, includePostgres: true);
        plan.ManagedServices[0].VolumeBackupScheduleKinds = ["DAILY"];
        var client = new RailwayGraphQLClient(new HttpClient(handler));
        var apply = new RailwayGraphQLApplyService(client, new RailwayApplyOptions
        {
            VolumeInstancePollInterval = TimeSpan.Zero,
            VolumeInstanceTimeout = TimeSpan.Zero
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apply.ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager()));

        Assert.Contains("volume instance", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(GraphQLFixtures.PostgresServiceId, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Count("volumeInstanceBackupScheduleUpdate"));
        Assert.Equal(0, handler.Count("volumeInstanceBackupCreate"));
        Assert.Equal(0, handler.Count("pluginCreate"));
    }

    [Fact]
    public async Task Apply_RetriesUntilVolumeInstanceIsVisible()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectWithExistingCanvas);
        handler.Enqueue("environment", GraphQLFixtures.EnvironmentVolumeInstancesEmpty);
        handler.Enqueue(
            "environment",
            GraphQLFixtures.EnvironmentVolumeInstances((GraphQLFixtures.VolumeInstanceId, GraphQLFixtures.PostgresServiceId)));
        handler.Enqueue("volumeInstanceBackupScheduleList", GraphQLFixtures.VolumeInstanceBackupScheduleList());
        handler.Enqueue("volumeInstanceBackupScheduleUpdate", GraphQLFixtures.VolumeInstanceBackupScheduleUpdate);
        handler.Enqueue(
            "volumeInstanceBackupScheduleList",
            GraphQLFixtures.VolumeInstanceBackupScheduleList((GraphQLFixtures.DailyScheduleId, "DAILY")));
        EnqueueComputeAndCommit(handler);

        var plan = GraphQLFixtures.CreatePlan(adoptExisting: true, includePostgres: true);
        plan.ManagedServices[0].VolumeBackupScheduleKinds = ["DAILY"];

        var result = await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(2, handler.Count("environment"));
        Assert.Equal(1, handler.Count("volumeInstanceBackupScheduleUpdate"));
        Assert.Equal(GraphQLFixtures.VolumeInstanceId, result.VolumeInstanceIds["postgres"]);
    }

    [Fact]
    public async Task Apply_NewTemplate_ResolvesServiceIdThenSchedules()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("template", GraphQLFixtures.TemplatePostgres);
        handler.Enqueue("templateDeployV2", GraphQLFixtures.TemplateDeployV2);
        handler.Enqueue("workflowStatus", GraphQLFixtures.WorkflowComplete);
        handler.Enqueue("project", GraphQLFixtures.ProjectWithExistingCanvas);
        handler.Enqueue(
            "environment",
            GraphQLFixtures.EnvironmentVolumeInstances((GraphQLFixtures.VolumeInstanceId, GraphQLFixtures.PostgresServiceId)));
        handler.Enqueue("volumeInstanceBackupScheduleList", GraphQLFixtures.VolumeInstanceBackupScheduleList());
        handler.Enqueue("volumeInstanceBackupScheduleUpdate", GraphQLFixtures.VolumeInstanceBackupScheduleUpdate);
        handler.Enqueue(
            "volumeInstanceBackupScheduleList",
            GraphQLFixtures.VolumeInstanceBackupScheduleList((GraphQLFixtures.DailyScheduleId, "DAILY")));
        EnqueueComputeAndCommit(handler);

        var plan = GraphQLFixtures.CreatePlan(includePostgres: true);
        plan.ManagedServices[0].VolumeBackupScheduleKinds = ["DAILY"];

        var result = await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(GraphQLFixtures.PostgresServiceId, result.ServiceIds["postgres"]);
        Assert.Equal(1, handler.Count("templateDeployV2"));
        Assert.Equal(1, handler.Count("volumeInstanceBackupScheduleUpdate"));
        Assert.Equal(0, handler.Count("volumeInstance"));
        Assert.Equal(0, handler.Count("adminVolumeInstancesForVolume"));
    }

    [Fact]
    public async Task Apply_InvalidDeserializedKinds_FailsBeforeGraphQL()
    {
        var handler = new ScriptedGraphQLHandler();
        var plan = GraphQLFixtures.CreatePlan(includePostgres: true);
        plan.ManagedServices[0].VolumeBackupScheduleKinds = ["hourly"];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
                plan,
                GraphQLFixtures.CreateRequest(),
                new RecordingReportingStep(),
                new MemoryDeploymentStateManager()));

        Assert.Contains("hourly", exception.Message, StringComparison.Ordinal);
        Assert.Contains("DAILY", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Operations);
    }

    [Fact]
    public async Task Apply_WithoutKinds_DoesNotQueryVolumeSchedules()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("project", GraphQLFixtures.ProjectWithExistingCanvas);
        EnqueueComputeAndCommit(handler);

        await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            GraphQLFixtures.CreatePlan(adoptExisting: true, includePostgres: true),
            GraphQLFixtures.CreateRequest(
                adoptedProjectId: GraphQLFixtures.ProjectId,
                adoptedEnvironmentId: GraphQLFixtures.ProductionEnvironmentId),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        Assert.Equal(0, handler.Count("environment"));
        Assert.Equal(0, handler.Count("volumeInstanceBackupScheduleList"));
        Assert.Equal(0, handler.Count("volumeInstanceBackupScheduleUpdate"));
    }

    [Fact]
    public async Task Client_VolumeBackupScheduleOperations_UseConfirmedNamesAndNeverNull()
    {
        var handler = new RecordingHandler("""{"data":{"volumeInstanceBackupScheduleUpdate":true}}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.VolumeInstanceBackupScheduleUpdateAsync(
            ["DAILY", "WEEKLY"],
            "volinst_placeholder",
            "placeholder-token");

        Assert.Contains("\"operationName\":\"volumeInstanceBackupScheduleUpdate\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("[VolumeInstanceBackupScheduleKind!]!", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"kinds\":[\"DAILY\",\"WEEKLY\"]", handler.Body, StringComparison.Ordinal);
        Assert.Contains("volinst_placeholder", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("null", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("pluginCreate", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("volumeInstanceBackupCreate", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("enablePitrForHaCluster", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("WAL_ARCHIVE", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("bucketCreate", handler.Body, StringComparison.Ordinal);

        Assert.Contains("$volumeInstanceId: String!", RailwayGraphQLOperations.VolumeInstanceBackupScheduleList, StringComparison.Ordinal);
        Assert.Contains("kind", RailwayGraphQLOperations.VolumeInstanceBackupScheduleList, StringComparison.Ordinal);
        Assert.DoesNotContain("edges", RailwayGraphQLOperations.VolumeInstanceBackupScheduleList, StringComparison.Ordinal);
        Assert.Contains("volumeInstances", RailwayGraphQLOperations.Environment, StringComparison.Ordinal);
        Assert.Contains("serviceId", RailwayGraphQLOperations.Environment, StringComparison.Ordinal);
        Assert.Contains("edges", RailwayGraphQLOperations.Environment, StringComparison.Ordinal);
        Assert.Contains("node", RailwayGraphQLOperations.Environment, StringComparison.Ordinal);
        Assert.DoesNotContain("adminVolumeInstancesForVolume", RailwayGraphQLOperations.Environment, StringComparison.Ordinal);
        Assert.DoesNotContain("pluginCreate", RailwayGraphQLOperations.Environment, StringComparison.Ordinal);
        Assert.DoesNotContain("pluginCreate", RailwayGraphQLOperations.VolumeInstanceBackupScheduleUpdate, StringComparison.Ordinal);
        Assert.DoesNotContain("mutation volumeInstanceBackupCreate", RailwayGraphQLOperations.VolumeInstanceBackupScheduleUpdate, StringComparison.Ordinal);
        Assert.DoesNotContain("mutation enablePitrForHaCluster", RailwayGraphQLOperations.VolumeInstanceBackupScheduleUpdate, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_Environment_OmitsUnsetPagination()
    {
        var handler = new RecordingHandler(GraphQLFixtures.EnvironmentVolumeInstancesEmpty);
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.EnvironmentAsync(
            "env_placeholder",
            "proj_placeholder",
            "placeholder-token");

        using var document = JsonDocument.Parse(handler.Body);
        var variables = document.RootElement.GetProperty("variables");
        Assert.Equal("env_placeholder", variables.GetProperty("id").GetString());
        Assert.Equal("proj_placeholder", variables.GetProperty("projectId").GetString());
        Assert.False(variables.TryGetProperty("after", out _));
        Assert.False(variables.TryGetProperty("first", out _));
        Assert.DoesNotContain(":null", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_ThenFlattenUnflatten_KeepsVolumeInstanceIds()
    {
        var state = new MemoryDeploymentStateManager();
        var result = new RailwayApplyResult
        {
            ProjectId = GraphQLFixtures.ProjectId,
            EnvironmentId = GraphQLFixtures.ProductionEnvironmentId
        };
        result.VolumeInstanceIds["postgres"] = GraphQLFixtures.VolumeInstanceId;
        result.VolumeBackupScheduleIds["postgres-DAILY"] = GraphQLFixtures.DailyScheduleId;

        await RailwayDeploymentStateStore.SaveAsync(state, "railway", "production", result, CancellationToken.None);
        await RailwayDeploymentStateStoreTests.FlattenUnflattenSectionAsync(state, "Railway:railway");

        var snapshot = await RailwayDeploymentStateStore.LoadAsync(state, "railway", "production", CancellationToken.None);
        Assert.Equal(GraphQLFixtures.VolumeInstanceId, snapshot.VolumeInstanceIds["postgres"]);
        Assert.Equal(GraphQLFixtures.DailyScheduleId, snapshot.VolumeBackupScheduleIds["postgres-DAILY"]);
        var section = await state.AcquireSectionAsync("Railway:railway");
        Assert.IsType<System.Text.Json.Nodes.JsonObject>(
            section.Data[RailwayDeploymentStateStore.VolumeInstancesKey]?["production"]);
        Assert.IsNotType<System.Text.Json.Nodes.JsonArray>(
            section.Data[RailwayDeploymentStateStore.VolumeInstancesKey]?["production"]);
    }

    private static void EnqueueAdoptedPostgresWithSchedules(
        ScriptedGraphQLHandler handler,
        string listThen,
        bool update,
        string? listAfter = null)
    {
        handler.Enqueue("project", GraphQLFixtures.ProjectWithExistingCanvas);
        handler.Enqueue(
            "environment",
            GraphQLFixtures.EnvironmentVolumeInstances((GraphQLFixtures.VolumeInstanceId, GraphQLFixtures.PostgresServiceId)));
        handler.Enqueue("volumeInstanceBackupScheduleList", listThen);
        if (update)
        {
            handler.Enqueue("volumeInstanceBackupScheduleUpdate", GraphQLFixtures.VolumeInstanceBackupScheduleUpdate);
            handler.Enqueue("volumeInstanceBackupScheduleList", listAfter ?? listThen);
        }

        EnqueueComputeAndCommit(handler);
    }

    private static void EnqueueComputeAndCommit(ScriptedGraphQLHandler handler)
    {
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
