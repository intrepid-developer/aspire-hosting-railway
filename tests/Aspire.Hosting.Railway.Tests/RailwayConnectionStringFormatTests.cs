using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Railway;

namespace Aspire.Hosting.Railway.Tests;

public class RailwayConnectionStringFormatTests
{
    [Fact]
    public void Plan_AddProject_PostgresReference_IsNpgsqlKeywordExpression()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var db = builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddTestProject("api").WithReference(db);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var json = RailwayPlanBuilder.ToJson(plan);
        var api = Assert.Single(plan.Services, service => service.Name == "api");

        var expected = RailwayReferenceExpressions.NpgsqlKeyword("postgres");
        Assert.Equal(expected, api.Environment["ConnectionStrings__postgres"]);
        Assert.Contains("Host=${{postgres.PGHOST}}", api.Environment["ConnectionStrings__postgres"], StringComparison.Ordinal);
        Assert.Contains("Username=${{postgres.PGUSER}}", api.Environment["ConnectionStrings__postgres"], StringComparison.Ordinal);
        Assert.Contains("Password=${{postgres.PGPASSWORD}}", api.Environment["ConnectionStrings__postgres"], StringComparison.Ordinal);
        Assert.Contains("Database=${{postgres.PGDATABASE}}", api.Environment["ConnectionStrings__postgres"], StringComparison.Ordinal);
        Assert.DoesNotContain("postgresql://", api.Environment["ConnectionStrings__postgres"], StringComparison.Ordinal);
        Assert.DoesNotContain("${{postgres.DATABASE_URL}}", api.Environment["ConnectionStrings__postgres"], StringComparison.Ordinal);
        Assert.Contains("Host=${{postgres.PGHOST}}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("RAILWAY_TOKEN=", json, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, json, StringComparison.Ordinal);
        Assert.DoesNotContain("postgresql://", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_AddProject_RedisReference_IsStackExchangeKeywordExpression()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var cache = builder.AddRedis("redis").PublishAsRailwayRedis();
        builder.AddTestProject("api").WithReference(cache);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var json = RailwayPlanBuilder.ToJson(plan);
        var api = Assert.Single(plan.Services, service => service.Name == "api");

        Assert.Equal(
            RailwayReferenceExpressions.StackExchangeRedisKeyword("redis"),
            api.Environment["ConnectionStrings__redis"]);
        Assert.Contains("${{redis.REDISHOST}}:${{redis.REDISPORT}},password=${{redis.REDISPASSWORD}}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("redis://", json, StringComparison.Ordinal);
        Assert.DoesNotContain("${{redis.REDIS_URL}}", json, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_AddProject_ChildDatabaseReference_IsKeywordShaped()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var database = builder.AddPostgres("postgres").PublishAsRailwayPostgres().AddDatabase("catalog");
        builder.AddTestProject("api").WithReference(database);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var api = Assert.Single(plan.Services, service => service.Name == "api");

        Assert.Equal(
            RailwayReferenceExpressions.NpgsqlKeyword("postgres"),
            api.Environment["ConnectionStrings__catalog"]);
        Assert.False(api.Environment.ContainsKey("ConnectionStrings__postgres"));
    }

    [Fact]
    public void Plan_Container_KeepsPostgresAndRedisUriExpressions()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var db = builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        var cache = builder.AddRedis("redis").PublishAsRailwayRedis();
        builder.AddContainer("api", "nginx").WithReference(db).WithReference(cache);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var json = RailwayPlanBuilder.ToJson(plan);
        var api = Assert.Single(plan.Services, service => service.Name == "api");

        Assert.Equal("${{postgres.DATABASE_URL}}", api.Environment["ConnectionStrings__postgres"]);
        Assert.Equal("${{redis.REDIS_URL}}", api.Environment["ConnectionStrings__redis"]);
        Assert.Contains("${{postgres.DATABASE_URL}}", json, StringComparison.Ordinal);
        Assert.Contains("${{redis.REDIS_URL}}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PGHOST", json, StringComparison.Ordinal);
        Assert.DoesNotContain("REDISHOST", json, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, json, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_ProjectMetadataAnnotation_OnContainer_UsesKeywordForm()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var db = builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddContainer("api", "nginx")
            .WithAnnotation(new TestProjectMetadata())
            .WithReference(db);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var api = Assert.Single(plan.Services, service => service.Name == "api");

        Assert.Equal(
            RailwayReferenceExpressions.NpgsqlKeyword("postgres"),
            api.Environment["ConnectionStrings__postgres"]);
    }

    [Fact]
    public void Plan_AddProject_WithExplicitDatabaseUrl_KeepsUriOnDatabaseUrl()
    {
        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var db = builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddTestProject("api")
            .WithReference(db)
            .WithEnvironment(
                "DATABASE_URL",
                RailwayReferenceExpressions.PrivateServiceVariable("postgres", "DATABASE_URL"));

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");
        var json = RailwayPlanBuilder.ToJson(plan);
        var api = Assert.Single(plan.Services, service => service.Name == "api");

        Assert.Equal(
            RailwayReferenceExpressions.NpgsqlKeyword("postgres"),
            api.Environment["ConnectionStrings__postgres"]);
        Assert.Equal("${{postgres.DATABASE_URL}}", api.Environment["DATABASE_URL"]);
        Assert.Contains("ConnectionStrings__postgres", json, StringComparison.Ordinal);
        Assert.Contains("DATABASE_URL", json, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, json, StringComparison.Ordinal);
    }

    [Fact]
    public void RailwayReference_RewritesComposedKeywordServiceNameCasing()
    {
        var rewritten = RailwayReferenceExpressions.RewriteServiceName(
            RailwayReferenceExpressions.NpgsqlKeyword("postgres"),
            ["Postgres", "api"]);

        Assert.Equal(RailwayReferenceExpressions.NpgsqlKeyword("Postgres"), rewritten);
        Assert.Contains("${{Postgres.PGHOST}}", rewritten, StringComparison.Ordinal);
        Assert.Contains("${{Postgres.PGPASSWORD}}", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void PrefersKeyword_ProjectResourceAndMetadata()
    {
        var builder = TestAppBuilder.CreatePublish();
        builder.AddRailwayEnvironment("railway");
        var project = builder.AddTestProject("api");
        var container = builder.AddContainer("node", "nginx");
        var annotated = builder.AddContainer("annotated", "nginx")
            .WithAnnotation(new TestProjectMetadata());

        using var app = builder.Build();

        Assert.True(RailwayConnectionStringConsumer.PrefersKeywordConnectionString(project.Resource));
        Assert.False(RailwayConnectionStringConsumer.PrefersKeywordConnectionString(container.Resource));
        Assert.True(RailwayConnectionStringConsumer.PrefersKeywordConnectionString(annotated.Resource));
    }

    [Fact]
    public async Task Apply_AddProjectPlan_UpsertsNpgsqlKeywordExpression()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("template", GraphQLFixtures.TemplatePostgres);
        handler.Enqueue("templateDeployV2", GraphQLFixtures.TemplateDeployV2);
        handler.Enqueue("workflowStatus", GraphQLFixtures.WorkflowComplete);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        GraphQLFixtures.EnqueueRegistryCredentials(handler);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var db = builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddTestProject("api").WithReference(db);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");

        await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var upsert = handler.Bodies.Single(body => body.Contains("variableCollectionUpsert", StringComparison.Ordinal));
        Assert.Contains("ConnectionStrings__postgres", upsert, StringComparison.Ordinal);
        Assert.Contains("Host=${{postgres.PGHOST}}", upsert, StringComparison.Ordinal);
        Assert.Contains("Password=${{postgres.PGPASSWORD}}", upsert, StringComparison.Ordinal);
        Assert.DoesNotContain("${{postgres.DATABASE_URL}}", upsert, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, upsert, StringComparison.Ordinal);
        Assert.DoesNotContain("postgresql://", upsert, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_ContainerPlan_UpsertsDatabaseUrlUri()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("template", GraphQLFixtures.TemplatePostgres);
        handler.Enqueue("templateDeployV2", GraphQLFixtures.TemplateDeployV2);
        handler.Enqueue("workflowStatus", GraphQLFixtures.WorkflowComplete);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        GraphQLFixtures.EnqueueRegistryCredentials(handler);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var db = builder.AddPostgres("postgres").PublishAsRailwayPostgres();
        builder.AddContainer("api", "nginx").WithReference(db);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");

        await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var upsert = handler.Bodies.Single(body => body.Contains("variableCollectionUpsert", StringComparison.Ordinal));
        Assert.Contains("ConnectionStrings__postgres", upsert, StringComparison.Ordinal);
        Assert.Contains("${{postgres.DATABASE_URL}}", upsert, StringComparison.Ordinal);
        Assert.DoesNotContain("PGHOST", upsert, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, upsert, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_AddProjectPlan_UpsertsRedisKeywordExpression()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("projectCreate", GraphQLFixtures.ProjectCreate);
        handler.Enqueue("template", GraphQLFixtures.TemplateRedis);
        handler.Enqueue("templateDeployV2", GraphQLFixtures.TemplateDeployV2);
        handler.Enqueue("workflowStatus", GraphQLFixtures.WorkflowComplete);
        handler.Enqueue("serviceCreate", GraphQLFixtures.ServiceCreateApi);
        GraphQLFixtures.EnqueueRegistryCredentials(handler);
        handler.Enqueue("serviceInstanceUpdate", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("variableCollectionUpsert", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("serviceInstanceDeployV2", GraphQLFixtures.ScalarSuccess);
        handler.Enqueue("environmentPatchCommitStaged", GraphQLFixtures.ScalarSuccess);

        var builder = TestAppBuilder.CreatePublish();
        var railway = builder.AddRailwayEnvironment("railway");
        var cache = builder.AddRedis("redis").PublishAsRailwayRedis();
        builder.AddTestProject("api").WithReference(cache);

        using var app = builder.Build();
        var plan = RailwayPlanBuilder.Create(TestAppBuilder.GetModel(app), railway.Resource, "Production");

        await GraphQLFixtures.CreateApplyService(handler).ApplyAsync(
            plan,
            GraphQLFixtures.CreateRequest(),
            new RecordingReportingStep(),
            new MemoryDeploymentStateManager());

        var upsert = handler.Bodies.Single(body => body.Contains("variableCollectionUpsert", StringComparison.Ordinal));
        Assert.Contains("ConnectionStrings__redis", upsert, StringComparison.Ordinal);
        Assert.Contains("${{redis.REDISHOST}}:${{redis.REDISPORT}},password=${{redis.REDISPASSWORD}}", upsert, StringComparison.Ordinal);
        Assert.DoesNotContain("${{redis.REDIS_URL}}", upsert, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphQLFixtures.Token, upsert, StringComparison.Ordinal);
    }

    private sealed class TestProjectMetadata : IProjectMetadata
    {
        public string ProjectPath => "Api.csproj";
    }
}
