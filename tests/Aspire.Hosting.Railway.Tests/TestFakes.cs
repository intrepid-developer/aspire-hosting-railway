using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Aspire.Hosting.Pipelines;

using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.Railway.Tests;

internal sealed class ScriptedGraphQLHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<string>> _responses = new(StringComparer.Ordinal);

    public List<string> Operations { get; } = [];
    public List<string> Bodies { get; } = [];

    public void Enqueue(string operationName, string responseJson)
    {
        if (!_responses.TryGetValue(operationName, out var queue))
        {
            queue = new Queue<string>();
            _responses[operationName] = queue;
        }

        queue.Enqueue(responseJson);
    }

    public int Count(string operationName) =>
        Operations.Count(name => string.Equals(name, operationName, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Bodies.Add(body);

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var operationName = document.RootElement.TryGetProperty("operationName", out var name)
            ? name.GetString() ?? ""
            : "";
        Operations.Add(operationName);

        if (!_responses.TryGetValue(operationName, out var queue) || queue.Count == 0)
        {
            throw new InvalidOperationException($"No scripted GraphQL response for '{operationName}'.");
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(queue.Dequeue(), Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class MemoryDeploymentStateManager : IDeploymentStateManager
{
    private readonly Dictionary<string, (JsonObject Data, long Version)> _sections = new(StringComparer.Ordinal);

    public string? StateFilePath => null;

    public Task<DeploymentStateSection> AcquireSectionAsync(string sectionName, CancellationToken cancellationToken = default)
    {
        if (_sections.TryGetValue(sectionName, out var existing))
        {
            return Task.FromResult(new DeploymentStateSection(sectionName, existing.Data.DeepClone().AsObject(), existing.Version));
        }

        return Task.FromResult(new DeploymentStateSection(sectionName, [], 0));
    }

    public Task SaveSectionAsync(DeploymentStateSection section, CancellationToken cancellationToken = default)
    {
        _sections[section.SectionName] = (section.Data.DeepClone().AsObject(), section.Version + 1);
        section.Version++;
        return Task.CompletedTask;
    }

    public Task DeleteSectionAsync(DeploymentStateSection section, CancellationToken cancellationToken = default)
    {
        _sections.Remove(section.SectionName);
        return Task.CompletedTask;
    }

    public Task ClearAllStateAsync(CancellationToken cancellationToken = default)
    {
        _sections.Clear();
        return Task.CompletedTask;
    }
}

internal sealed class RecordingReportingStep : IReportingStep
{
    public List<string> Tasks { get; } = [];
    public List<string> Completions { get; } = [];
    public List<CompletionState> CompletionStates { get; } = [];

    public Task<IReportingTask> CreateTaskAsync(string statusText, CancellationToken cancellationToken = default)
    {
        Tasks.Add(statusText);
        return Task.FromResult<IReportingTask>(new RecordingReportingTask(this, statusText));
    }

    public Task<IReportingTask> CreateTaskAsync(MarkdownString statusText, CancellationToken cancellationToken = default) =>
        CreateTaskAsync(statusText.Value, cancellationToken);

    public Task CompleteAsync(string completionText, CompletionState completionState, CancellationToken cancellationToken = default)
    {
        Completions.Add(completionText);
        CompletionStates.Add(completionState);
        return Task.CompletedTask;
    }

    public Task CompleteAsync(MarkdownString completionText, CompletionState completionState, CancellationToken cancellationToken = default) =>
        CompleteAsync(completionText.Value, completionState, cancellationToken);

    public void Log(LogLevel logLevel, string message, bool enableMarkdown)
    {
    }

    public void Log(LogLevel logLevel, string message)
    {
    }

    public void Log(LogLevel logLevel, MarkdownString message)
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class RecordingReportingTask(RecordingReportingStep step, string title) : IReportingTask
    {
        public Task UpdateAsync(string statusText, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(MarkdownString statusText, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CompleteAsync(string? completionMessage = null, CompletionState completionState = CompletionState.Completed, CancellationToken cancellationToken = default)
        {
            step.Completions.Add(completionMessage ?? title);
            step.CompletionStates.Add(completionState);
            return Task.CompletedTask;
        }

        public Task CompleteAsync(MarkdownString completionMessage, CompletionState completionState, CancellationToken cancellationToken = default) =>
            CompleteAsync(completionMessage.Value, completionState, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal static class GraphQLFixtures
{
    public const string Token = "placeholder-token";
    public const string ProjectId = "proj_placeholder";
    public const string ProductionEnvironmentId = "env_production_placeholder";
    public const string StagingEnvironmentId = "env_staging_placeholder";
    public const string ApiServiceId = "svc_api_placeholder";
    public const string UploadsServiceId = "svc_uploads_placeholder";
    public const string BucketId = "bucket_placeholder";

    public static string ProjectCreate =>
        """{"data":{"projectCreate":{"id":"proj_placeholder","name":"railway","environments":{"edges":[{"node":{"id":"env_production_placeholder","name":"production"}}]}}}}""";

    public static string EnvironmentCreateStaging =>
        """{"data":{"environmentCreate":{"id":"env_staging_placeholder","name":"staging"}}}""";

    public static string EnvironmentCreateEmpty =>
        """{"data":{"environmentCreate":{"id":"env_empty_placeholder","name":"staging"}}}""";

    public static string ServiceCreateApi =>
        """{"data":{"serviceCreate":{"id":"svc_api_placeholder","name":"api"}}}""";

    public static string ServiceCreateUploads =>
        """{"data":{"serviceCreate":{"id":"svc_uploads_placeholder","name":"uploads"}}}""";

    public static string ScalarSuccess => """{"data":true}""";

    public static string TemplatePostgres =>
        """{"data":{"template":{"id":"tpl_postgres_placeholder","code":"postgres","serializedConfig":{"services":{"postgres":{}}}}}}""";

    public static string TemplateRedis =>
        """{"data":{"template":{"id":"tpl_redis_placeholder","code":"redis","serializedConfig":{"services":{"redis":{}}}}}}""";

    public static string TemplateDeployV2 =>
        """{"data":{"templateDeployV2":{"projectId":"proj_placeholder","workflowId":"wf_placeholder"}}}""";

    public static string TemplateDeployV2WithoutWorkflow =>
        """{"data":{"templateDeployV2":{"projectId":"proj_placeholder"}}}""";

    public static string WorkflowComplete =>
        """{"data":{"workflowStatus":{"status":"Complete"}}}""";

    public static string WorkflowError =>
        """{"data":{"workflowStatus":{"status":"Error","error":"template workflow failed"}}}""";

    public static string BucketCreate =>
        """{"data":{"bucketCreate":{"id":"bucket_placeholder","name":"uploads"}}}""";

    public static string BucketCredentials =>
        """{"data":{"bucketS3Credentials":{"accessKeyId":"placeholder-access-key","secretAccessKey":"placeholder-secret-key","endpoint":"https://storage.railway.app","region":"auto","bucket":"uploads"}}}""";

    public static string ServiceDomainCreate =>
        """{"data":{"serviceDomainCreate":{"id":"domain_placeholder","domain":"api-placeholder.up.railway.app"}}}""";

    public static string GraphQLError(string message) =>
        $$"""{"errors":[{"message":"{{message}}"}]}""";

    public static RailwayPlan CreatePlan(
        string railwayEnvironmentName = "production",
        bool adoptExisting = false,
        bool duplicateStaging = true,
        bool createEmpty = false,
        bool includeApi = true,
        bool includePostgres = false,
        bool includeRedis = false,
        bool includeBucket = false)
    {
        var plan = new RailwayPlan
        {
            ComputeEnvironment = "railway",
            RailwayEnvironmentName = railwayEnvironmentName,
            AdoptExisting = adoptExisting,
            DuplicateProductionWhenCreatingStaging = duplicateStaging,
            CreateEmptyEnvironment = createEmpty
        };

        if (includeApi)
        {
            plan.Services.Add(new RailwayPlanService
            {
                Name = "api",
                Image = "ghcr.io/example/api:placeholder",
                Environment =
                {
                    ["ConnectionStrings__postgres"] = "${{postgres.DATABASE_URL}}"
                }
            });
        }

        if (includePostgres)
        {
            plan.ManagedServices.Add(new RailwayPlanManagedService
            {
                Name = "postgres",
                Kind = "postgres",
                TemplateCode = "postgres",
                PrivateReferenceVariable = "DATABASE_URL"
            });
        }

        if (includeRedis)
        {
            plan.ManagedServices.Add(new RailwayPlanManagedService
            {
                Name = "redis",
                Kind = "redis",
                TemplateCode = "redis",
                PrivateReferenceVariable = "REDIS_URL"
            });
        }

        if (includeBucket)
        {
            plan.ManagedServices.Add(new RailwayPlanManagedService
            {
                Name = "uploads",
                Kind = "bucket"
            });
        }

        return plan;
    }

    public static RailwayApplyRequest CreateRequest(
        string? adoptedProjectId = null,
        string? adoptedEnvironmentId = null,
        bool duplicateStaging = true,
        bool createEmpty = false,
        bool includeApiImage = true)
    {
        var request = new RailwayApplyRequest
        {
            Token = Token,
            AdoptedProjectId = adoptedProjectId,
            AdoptedEnvironmentId = adoptedEnvironmentId,
            DuplicateProductionWhenCreatingStaging = duplicateStaging,
            CreateEmptyEnvironment = createEmpty
        };

        if (includeApiImage)
        {
            request.ServiceImages["api"] = "ghcr.io/example/api:placeholder";
        }

        return request;
    }

    public static RailwayGraphQLApplyService CreateApplyService(ScriptedGraphQLHandler handler)
    {
        var client = new RailwayGraphQLClient(new HttpClient(handler));
        return new RailwayGraphQLApplyService(client, new RailwayApplyOptions
        {
            WorkflowPollInterval = TimeSpan.Zero,
            WorkflowTimeout = TimeSpan.FromSeconds(5)
        });
    }
}
