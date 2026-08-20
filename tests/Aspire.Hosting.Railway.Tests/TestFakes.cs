using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Railway;

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

        if (string.Equals(operationName, "templateDeployV2", StringComparison.Ordinal))
        {
            GraphQLFixtures.ReadTemplateIdFromDeployBody(body);
        }

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
    public const string PostgresServiceId = "svc_postgres_placeholder";
    public const string UploadsServiceId = "svc_uploads_placeholder";
    public const string BucketId = "bucket_placeholder";
    public const string VolumeInstanceId = "volinst_placeholder";
    public const string DailyScheduleId = "volsched_daily_placeholder";
    public const string WeeklyScheduleId = "volsched_weekly_placeholder";
    public const string MonthlyScheduleId = "volsched_monthly_placeholder";

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

    public static string ServiceDelete => """{"data":{"serviceDelete":true}}""";

    public static string ServiceDomainDelete => """{"data":{"serviceDomainDelete":true}}""";

    public static string CustomDomainDelete => """{"data":{"customDomainDelete":true}}""";

    public static string EnvironmentDelete => """{"data":{"environmentDelete":true}}""";

    public static string ProjectWithProductionAndStaging =>
        ProjectCanvas(
            [(ApiServiceId, "api"), (PostgresServiceId, "Postgres")],
            buckets: [(BucketId, "uploads")],
            environments:
            [
                (ProductionEnvironmentId, "production"),
                (StagingEnvironmentId, "staging")
            ]);

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
        """{"data":{"bucketS3Credentials":{"accessKeyId":"placeholder-access-key","secretAccessKey":"placeholder-secret-key","endpoint":"https://storage.railway.app","region":"auto","bucketName":"uploads"}}}""";

    public static string ProjectEmpty => ProjectQuery();

    public static string ProjectWithExistingCanvas => ProjectQuery(
        (PostgresServiceId, "Postgres"),
        (ApiServiceId, "api"),
        (UploadsServiceId, "uploads"));

    public static string ProjectWithApi => ProjectQuery((ApiServiceId, "api"));

    /// <summary>
    /// Adopted canvas: a bucket named <c>Uploads</c> plus a same-name variable service.
    /// The service id must not be used as <c>bucketId</c>.
    /// </summary>
    public static string ProjectWithExistingBucket => ProjectCanvas(
        [(ApiServiceId, "api"), (UploadsServiceId, "uploads")],
        [(BucketId, "Uploads")]);

    /// <summary>Same-name service only — no <c>project.buckets</c> node named uploads.</summary>
    public static string ProjectWithUploadsServiceOnly => ProjectQuery(
        (ApiServiceId, "api"),
        (UploadsServiceId, "uploads"));

    public static string ProjectQuery(params (string Id, string Name)[] services) =>
        ProjectCanvas(services, buckets: []);

    public static string ProjectCanvas(
        IReadOnlyList<(string Id, string Name)> services,
        IReadOnlyList<(string Id, string Name)>? buckets = null,
        IReadOnlyList<(string Id, string Name)>? environments = null)
    {
        var serviceEdges = NamedEdges(services);
        var bucketEdges = NamedEdges(buckets);
        var environmentEdges = NamedEdges(environments ?? [(ProductionEnvironmentId, "production")]);

        var payload = new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["project"] = new JsonObject
                {
                    ["name"] = "railway",
                    ["services"] = new JsonObject { ["edges"] = serviceEdges },
                    ["environments"] = new JsonObject { ["edges"] = environmentEdges },
                    ["buckets"] = new JsonObject { ["edges"] = bucketEdges }
                }
            }
        };

        return payload.ToJsonString();
    }

    public static RailwayGraphQLDestroyService CreateDestroyService(ScriptedGraphQLHandler handler)
    {
        var client = new RailwayGraphQLClient(new HttpClient(handler));
        return new RailwayGraphQLDestroyService(client);
    }

    public static RailwayDestroyRequest CreateDestroyRequest(
        string? adoptedProjectId = null,
        string? adoptedEnvironmentId = null) =>
        new()
        {
            Token = Token,
            AdoptedProjectId = adoptedProjectId,
            AdoptedEnvironmentId = adoptedEnvironmentId
        };

    private static JsonArray NamedEdges(IReadOnlyList<(string Id, string Name)>? items)
    {
        var edges = new JsonArray();
        if (items is null)
        {
            return edges;
        }

        foreach (var (id, name) in items)
        {
            edges.Add(new JsonObject
            {
                ["node"] = new JsonObject
                {
                    ["id"] = id,
                    ["name"] = name
                }
            });
        }

        return edges;
    }

    public const string CustomDomainId = "cdom_placeholder";

    public static string ServiceDomainCreate =>
        """{"data":{"serviceDomainCreate":{"id":"domain_placeholder","domain":"api-placeholder.up.railway.app"}}}""";

    public static string DomainsEmpty =>
        """{"data":{"domains":{"customDomains":[],"serviceDomains":[]}}}""";

    public static string DomainsWithCustom =>
        """{"data":{"domains":{"customDomains":[{"id":"cdom_placeholder","domain":"api.example.com","targetPort":8080,"status":{"verified":false,"verificationToken":"verify-placeholder","verificationDnsHost":"_railway.example.com","certificateStatus":"CERTIFICATE_STATUS_TYPE_VALIDATING_OWNERSHIP","dnsRecords":[{"fqdn":"api.example.com","recordType":"DNS_RECORD_TYPE_CNAME","requiredValue":"api-placeholder.up.railway.app","purpose":"DNS_RECORD_PURPOSE_TRAFFIC_ROUTE","status":"DNS_RECORD_STATUS_REQUIRES_UPDATE"}]}}],"serviceDomains":[{"id":"domain_placeholder","domain":"api-placeholder.up.railway.app"}]}}}""";

    public static string CustomDomainAvailableTrue =>
        """{"data":{"customDomainAvailable":{"available":true,"message":"available"}}}""";

    public static string CustomDomainAvailableFalse =>
        """{"data":{"customDomainAvailable":{"available":false,"message":"domain is already in use"}}}""";

    public static string CustomDomainCreate =>
        """{"data":{"customDomainCreate":{"id":"cdom_placeholder","domain":"api.example.com","targetPort":8080,"status":{"verified":false,"verificationToken":"verify-placeholder","verificationDnsHost":"_railway.example.com","certificateStatus":"CERTIFICATE_STATUS_TYPE_VALIDATING_OWNERSHIP","dnsRecords":[{"fqdn":"api.example.com","recordType":"DNS_RECORD_TYPE_CNAME","requiredValue":"api-placeholder.up.railway.app","purpose":"DNS_RECORD_PURPOSE_TRAFFIC_ROUTE","status":"DNS_RECORD_STATUS_REQUIRES_UPDATE"}]}}}}""";

    public static string CustomDomainQuery =>
        """{"data":{"customDomain":{"id":"cdom_placeholder","domain":"api.example.com","targetPort":8080,"status":{"verified":false,"verificationToken":"verify-placeholder","verificationDnsHost":"_railway.example.com","certificateStatus":"CERTIFICATE_STATUS_TYPE_VALIDATING_OWNERSHIP","dnsRecords":[{"fqdn":"api.example.com","recordType":"DNS_RECORD_TYPE_CNAME","requiredValue":"api-placeholder.up.railway.app","purpose":"DNS_RECORD_PURPOSE_TRAFFIC_ROUTE","status":"DNS_RECORD_STATUS_REQUIRES_UPDATE"}]}}}}""";

    public static string EnvironmentVolumeInstances(params (string Id, string ServiceId)[] instances) =>
        EnvironmentVolumeInstancesPage(instances, hasNextPage: false);

    public static string EnvironmentVolumeInstancesEmpty => EnvironmentVolumeInstances();

    public static string EnvironmentVolumeInstancesPage(
        IReadOnlyList<(string Id, string ServiceId)> instances,
        bool hasNextPage,
        string? endCursor = null)
    {
        var edges = new JsonArray();
        foreach (var (id, serviceId) in instances)
        {
            edges.Add(new JsonObject
            {
                ["node"] = new JsonObject
                {
                    ["id"] = id,
                    ["serviceId"] = serviceId,
                    ["volumeId"] = "vol_placeholder",
                    ["environmentId"] = ProductionEnvironmentId,
                    ["mountPath"] = "/var/lib/postgresql/data"
                }
            });
        }

        var payload = new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["environment"] = new JsonObject
                {
                    ["volumeInstances"] = new JsonObject
                    {
                        ["edges"] = edges,
                        ["pageInfo"] = new JsonObject
                        {
                            ["hasNextPage"] = hasNextPage,
                            ["endCursor"] = endCursor
                        }
                    }
                }
            }
        };

        return payload.ToJsonString();
    }

    public static string VolumeInstanceBackupScheduleList(params (string Id, string Kind)[] schedules)
    {
        var items = new JsonArray();
        foreach (var (id, kind) in schedules)
        {
            items.Add(new JsonObject
            {
                ["id"] = id,
                ["kind"] = kind,
                ["name"] = kind,
                ["cron"] = "0 0 * * *",
                ["createdAt"] = "2026-08-20T00:00:00.000Z",
                ["retentionSeconds"] = 518400
            });
        }

        return new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["volumeInstanceBackupScheduleList"] = items
            }
        }.ToJsonString();
    }

    public static string VolumeInstanceBackupScheduleUpdate =>
        """{"data":{"volumeInstanceBackupScheduleUpdate":true}}""";

    public static JsonElement GetVolumeInstanceBackupScheduleUpdateVariables(IEnumerable<string> bodies)
    {
        var body = bodies.Single(item =>
            item.Contains("\"operationName\":\"volumeInstanceBackupScheduleUpdate\"", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("variables").Clone();
    }

    public static string CustomDomainUpdate =>
        """{"data":{"customDomainUpdate":{"id":"cdom_placeholder","domain":"api.example.com","targetPort":80,"status":{"verified":false,"verificationToken":"verify-placeholder","verificationDnsHost":"_railway.example.com","certificateStatus":"CERTIFICATE_STATUS_TYPE_ISSUING","dnsRecords":[{"fqdn":"api.example.com","recordType":"DNS_RECORD_TYPE_CNAME","requiredValue":"api-placeholder.up.railway.app","purpose":"DNS_RECORD_PURPOSE_TRAFFIC_ROUTE","status":"DNS_RECORD_STATUS_REQUIRES_UPDATE"}]}}}}""";

    public static JsonElement GetCustomDomainCreateInput(IEnumerable<string> bodies)
    {
        var body = bodies.Single(item => item.Contains("\"operationName\":\"customDomainCreate\"", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("variables").GetProperty("input").Clone();
    }

    public static JsonElement GetServiceDomainCreateInput(IEnumerable<string> bodies)
    {
        var body = bodies.Single(item => item.Contains("\"operationName\":\"serviceDomainCreate\"", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("variables").GetProperty("input").Clone();
    }

    public static string GraphQLError(string message) =>
        $$"""{"errors":[{"message":"{{message}}"}]}""";

    public static string ReadTemplateIdFromResponse(string templateQueryResponse)
    {
        using var document = JsonDocument.Parse(templateQueryResponse);
        return document.RootElement.GetProperty("data").GetProperty("template").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("template fixture is missing data.template.id.");
    }

    public static JsonElement GetServiceInstanceUpdateInput(IEnumerable<string> bodies)
    {
        var body = bodies.Single(item => item.Contains("\"operationName\":\"serviceInstanceUpdate\"", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("variables").GetProperty("input").Clone();
    }

    public static JsonElement GetServiceInstanceLimitsUpdateInput(IEnumerable<string> bodies)
    {
        var body = bodies.Single(item => item.Contains("\"operationName\":\"serviceInstanceLimitsUpdate\"", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("variables").GetProperty("input").Clone();
    }

    public static string ReadTemplateIdFromDeployBody(string templateDeployV2RequestBody)
    {
        using var document = JsonDocument.Parse(templateDeployV2RequestBody);
        if (!document.RootElement.TryGetProperty("variables", out var variables) ||
            !variables.TryGetProperty("input", out var input) ||
            !input.TryGetProperty("templateId", out var templateId))
        {
            throw new InvalidOperationException("templateDeployV2 variables.input.templateId is required.");
        }

        var value = templateId.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("templateDeployV2 variables.input.templateId is required.");
        }

        return value;
    }

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
            WorkflowTimeout = TimeSpan.FromSeconds(5),
            BucketCredentialsPollInterval = TimeSpan.Zero,
            BucketCredentialsTimeout = TimeSpan.FromSeconds(5),
            VolumeInstancePollInterval = TimeSpan.Zero,
            VolumeInstanceTimeout = TimeSpan.FromSeconds(5)
        });
    }
}

internal sealed class FakeChatConnectionStringResource : Resource, IResourceWithConnectionString
{
    public FakeChatConnectionStringResource(string name, ParameterResource key)
        : base(name)
    {
        ConnectionStringExpression = ReferenceExpression.Create($"Endpoint=https://api.example.test/v1;Key={key}");
    }

    public ReferenceExpression ConnectionStringExpression { get; }
}
