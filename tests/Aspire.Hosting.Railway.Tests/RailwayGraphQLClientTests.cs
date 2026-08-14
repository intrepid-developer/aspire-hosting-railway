using System.Net;
using System.Text;

using Aspire.Hosting.Railway;

namespace Aspire.Hosting.Railway.Tests;

public class RailwayGraphQLClientTests
{
    [Fact]
    public async Task ProjectCreate_PostsExpectedOperationShape()
    {
        var handler = new RecordingHandler("""{"data":{"projectCreate":{"id":"proj_placeholder","name":"demo"}}}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        var response = await client.ProjectCreateAsync(
            new ProjectCreateInput { Name = "demo" },
            "placeholder-token");

        Assert.Equal("proj_placeholder", response.Data?.ProjectCreate?.Id);
        Assert.Contains("projectCreate", handler.Body, StringComparison.Ordinal);
        Assert.Contains("ProjectCreateInput", handler.Body, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(RailwayGraphQLClient.DefaultEndpoint, handler.Uri?.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("placeholder-token", handler.AuthorizationParameter);
        Assert.DoesNotContain("placeholder-token", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceCreate_AlwaysIncludesEnvironmentId()
    {
        var handler = new RecordingHandler("""{"data":{"serviceCreate":{"id":"svc_placeholder","name":"api"}}}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.ServiceCreateAsync(
            new ServiceCreateInput
            {
                ProjectId = "proj_placeholder",
                EnvironmentId = "env_placeholder",
                Name = "api"
            },
            "placeholder-token");

        Assert.Contains("serviceCreate", handler.Body, StringComparison.Ordinal);
        Assert.Contains("environmentId", handler.Body, StringComparison.Ordinal);
        Assert.Contains("env_placeholder", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemplateAndBucketOperations_UseConfirmedFieldNames()
    {
        var handler = new RecordingHandler("""{"data":{}}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        await client.TemplateAsync("postgres", "placeholder-token");
        Assert.Contains("template", handler.Body, StringComparison.Ordinal);
        Assert.Contains("serializedConfig", handler.Body, StringComparison.Ordinal);

        await client.BucketCreateAsync(
            new BucketCreateInput
            {
                ProjectId = "proj_placeholder",
                EnvironmentId = "env_placeholder",
                Name = "uploads",
                Region = "us-west2"
            },
            "placeholder-token");
        Assert.Contains("bucketCreate", handler.Body, StringComparison.Ordinal);

        await client.EnvironmentCreateAsync(
            new EnvironmentCreateInput
            {
                ProjectId = "proj_placeholder",
                Name = "staging",
                SourceEnvironmentId = "env_production_placeholder"
            },
            "placeholder-token");
        Assert.Contains("environmentCreate", handler.Body, StringComparison.Ordinal);
        Assert.Contains("sourceEnvironmentId", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyTemplateAsync_FetchesSerializedConfigThenDeploys()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("template", GraphQLFixtures.TemplatePostgres);
        handler.Enqueue("templateDeployV2", GraphQLFixtures.TemplateDeployV2);
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        var response = await client.ApplyTemplateAsync(
            "postgres",
            GraphQLFixtures.ProjectId,
            GraphQLFixtures.ProductionEnvironmentId,
            "placeholder-token");

        Assert.Equal("wf_placeholder", response.Data?.TemplateDeployV2?.WorkflowId);
        Assert.Equal(new[] { "template", "templateDeployV2" }, handler.Operations);
        Assert.Contains("serializedConfig", handler.Bodies[1], StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-token", handler.Bodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyTemplateAsync_EmptySerializedConfig_DoesNotCallTemplateDeployV2()
    {
        var handler = new ScriptedGraphQLHandler();
        handler.Enqueue("template", """{"data":{"template":{"id":"tpl_placeholder","code":"postgres","serializedConfig":{}}}}""");
        var client = new RailwayGraphQLClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ApplyTemplateAsync(
            "postgres",
            GraphQLFixtures.ProjectId,
            GraphQLFixtures.ProductionEnvironmentId,
            "placeholder-token"));

        Assert.Contains("serializedConfig", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Count("templateDeployV2"));
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";
        public Uri? Uri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
