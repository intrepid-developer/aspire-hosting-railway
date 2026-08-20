using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Typed Railway GraphQL v2 client. Unit tests should inject a fake <see cref="HttpMessageHandler"/>.
/// </summary>
public sealed class RailwayGraphQLClient
{
    /// <summary>
    /// Named <see cref="HttpClient"/> registered by <c>AddRailwayInfrastructureCore</c>.
    /// </summary>
    public const string HttpClientName = "Railway.GraphQL";

    /// <summary>
    /// Railway GraphQL v2 endpoint.
    /// </summary>
    public const string DefaultEndpoint = RailwayConstants.GraphQLEndpoint;

    private static readonly JsonSerializerOptions s_json = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a client that posts to <see cref="DefaultEndpoint"/>.
    /// </summary>
    /// <param name="httpClient">HTTP client, typically configured with a fake handler in tests.</param>
    public RailwayGraphQLClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri(DefaultEndpoint);
    }

    /// <summary>
    /// Posts a GraphQL operation. The token is sent only as an Authorization header.
    /// </summary>
    /// <typeparam name="T">Response data type.</typeparam>
    /// <param name="request">The GraphQL request.</param>
    /// <param name="token">Account or workspace token. Never written to plan files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized GraphQL response.</returns>
    public async Task<RailwayGraphQLResponse<T>> SendAsync<T>(
        RailwayGraphQLRequest request,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(DefaultEndpoint));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Content = JsonContent.Create(request, options: s_json);

        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                FormatHttpFailure(response.StatusCode, errorBody),
                inner: null,
                statusCode: response.StatusCode);
        }

        var payload = await response.Content
            .ReadFromJsonAsync<RailwayGraphQLResponse<T>>(s_json, cancellationToken)
            .ConfigureAwait(false);

        return payload ?? new RailwayGraphQLResponse<T>();
    }

    /// <summary>
    /// Throws when the GraphQL envelope contains errors or has no data. Does not invent a success.
    /// </summary>
    /// <typeparam name="T">Response data type.</typeparam>
    /// <param name="response">The GraphQL response.</param>
    /// <param name="operationName">Operation name used in the exception message.</param>
    public static void ThrowIfFailed<T>(RailwayGraphQLResponse<T> response, string operationName)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        if (response.Errors is { Count: > 0 })
        {
            var messages = string.Join("; ", response.Errors.Select(error => error.Message).Where(static message => !string.IsNullOrWhiteSpace(message)));
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(messages)
                    ? $"Railway GraphQL {operationName} failed."
                    : $"Railway GraphQL {operationName} failed: {messages}");
        }

        if (response.Data is null)
        {
            throw new InvalidOperationException($"Railway GraphQL {operationName} returned no data.");
        }
    }

    /// <summary>
    /// Builds an HTTP failure message that includes Railway's GraphQL error text when present.
    /// </summary>
    internal static string FormatHttpFailure(System.Net.HttpStatusCode statusCode, string? body)
    {
        var railwayError = TryReadGraphQLErrorText(body);
        return string.IsNullOrWhiteSpace(railwayError)
            ? $"Railway GraphQL returned HTTP {(int)statusCode} ({statusCode})."
            : $"Railway GraphQL returned HTTP {(int)statusCode}: {railwayError}";
    }

    private static string? TryReadGraphQLErrorText(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray()
                    .Select(error => error.TryGetProperty("message", out var message) ? message.GetString() : null)
                    .Where(static message => !string.IsNullOrWhiteSpace(message))
                    .ToArray();
                if (messages.Length > 0)
                {
                    return string.Join("; ", messages);
                }
            }
        }
        catch (JsonException)
        {
            // Fall through and return a trimmed copy of the raw body.
        }

        var trimmed = body.Trim();
        return trimmed.Length <= 1024 ? trimmed : trimmed[..1024];
    }

    /// <summary>Returns whether <paramref name="serializedConfig"/> is a fetched, non-empty template document.</summary>
    public static bool HasSerializedConfig(JsonElement serializedConfig) =>
        serializedConfig.ValueKind switch
        {
            JsonValueKind.Object => serializedConfig.EnumerateObject().Any(),
            JsonValueKind.Array => serializedConfig.GetArrayLength() > 0,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(serializedConfig.GetString()),
            _ => false
        };

    /// <summary>Sends the documented <c>project(id)</c> query (services, environments, and buckets).</summary>
    public Task<RailwayGraphQLResponse<ProjectData>> ProjectAsync(
        string id,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<ProjectData>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.Project,
                OperationName = "project",
                Variables = new { id }
            },
            token,
            cancellationToken);

    /// <summary>Sends <c>projectCreate</c>.</summary>
    public Task<RailwayGraphQLResponse<ProjectCreateData>> ProjectCreateAsync(
        ProjectCreateInput input,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<ProjectCreateData>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.ProjectCreate,
                OperationName = "projectCreate",
                Variables = new { input }
            },
            token,
            cancellationToken);

    /// <summary>Sends <c>environmentCreate</c>.</summary>
    public Task<RailwayGraphQLResponse<EnvironmentCreateData>> EnvironmentCreateAsync(
        EnvironmentCreateInput input,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<EnvironmentCreateData>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.EnvironmentCreate,
                OperationName = "environmentCreate",
                Variables = new { input }
            },
            token,
            cancellationToken);

    /// <summary>Sends <c>serviceCreate</c>. <see cref="ServiceCreateInput.EnvironmentId"/> is required.</summary>
    public Task<RailwayGraphQLResponse<ServiceCreateData>> ServiceCreateAsync(
        ServiceCreateInput input,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.EnvironmentId);
        return SendAsync<ServiceCreateData>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.ServiceCreate,
                OperationName = "serviceCreate",
                Variables = new { input }
            },
            token,
            cancellationToken);
    }

    /// <summary>Sends <c>serviceInstanceUpdate</c>.</summary>
    public Task<RailwayGraphQLResponse<JsonElement>> ServiceInstanceUpdateAsync(
        string serviceId,
        string environmentId,
        ServiceInstanceUpdateInput input,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<JsonElement>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.ServiceInstanceUpdate,
                OperationName = "serviceInstanceUpdate",
                Variables = new { serviceId, environmentId, input }
            },
            token,
            cancellationToken);

    /// <summary>
    /// Sends <c>serviceInstanceLimitsUpdate</c>.
    /// <see cref="ServiceInstanceLimitsUpdateInput.ServiceId"/> and
    /// <see cref="ServiceInstanceLimitsUpdateInput.EnvironmentId"/> are required.
    /// </summary>
    public Task<RailwayGraphQLResponse<JsonElement>> ServiceInstanceLimitsUpdateAsync(
        ServiceInstanceLimitsUpdateInput input,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ServiceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.EnvironmentId);
        return SendAsync<JsonElement>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.ServiceInstanceLimitsUpdate,
                OperationName = "serviceInstanceLimitsUpdate",
                Variables = new { input }
            },
            token,
            cancellationToken);
    }

    /// <summary>Sends <c>serviceInstanceDeployV2</c>.</summary>
    public Task<RailwayGraphQLResponse<JsonElement>> ServiceInstanceDeployV2Async(
        string serviceId,
        string environmentId,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<JsonElement>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.ServiceInstanceDeployV2,
                OperationName = "serviceInstanceDeployV2",
                Variables = new { serviceId, environmentId }
            },
            token,
            cancellationToken);

    /// <summary>Sends <c>variableCollectionUpsert</c>.</summary>
    public Task<RailwayGraphQLResponse<JsonElement>> VariableCollectionUpsertAsync(
        VariableCollectionUpsertInput input,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<JsonElement>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.VariableCollectionUpsert,
                OperationName = "variableCollectionUpsert",
                Variables = new { input }
            },
            token,
            cancellationToken);

    /// <summary>Sends <c>serviceDomainCreate</c>.</summary>
    public Task<RailwayGraphQLResponse<ServiceDomainCreateData>> ServiceDomainCreateAsync(
        ServiceDomainCreateInput input,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<ServiceDomainCreateData>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.ServiceDomainCreate,
                OperationName = "serviceDomainCreate",
                Variables = new { input }
            },
            token,
            cancellationToken);

    /// <summary>Sends the <c>template</c> query.</summary>
    public Task<RailwayGraphQLResponse<TemplateData>> TemplateAsync(
        string code,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<TemplateData>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.Template,
                OperationName = "template",
                Variables = new { code }
            },
            token,
            cancellationToken);

    /// <summary>Sends <c>templateDeployV2</c>.</summary>
    public Task<RailwayGraphQLResponse<TemplateDeployV2Data>> TemplateDeployV2Async(
        TemplateDeployV2Input input,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.TemplateId))
        {
            throw new ArgumentException(
                "templateId must be the id returned by template(code). Never invent template UUIDs.",
                nameof(input));
        }

        if (!HasSerializedConfig(input.SerializedConfig))
        {
            throw new ArgumentException(
                "serializedConfig must be the document returned by template(code). Never invent template UUIDs or send an empty config.",
                nameof(input));
        }

        return SendAsync<TemplateDeployV2Data>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.TemplateDeployV2,
                OperationName = "templateDeployV2",
                Variables = new { input }
            },
            token,
            cancellationToken);
    }

    /// <summary>Sends <c>workflowStatus</c>.</summary>
    public Task<RailwayGraphQLResponse<WorkflowStatusData>> WorkflowStatusAsync(
        string workflowId,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<WorkflowStatusData>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.WorkflowStatus,
                OperationName = "workflowStatus",
                Variables = new { workflowId }
            },
            token,
            cancellationToken);

    /// <summary>Sends <c>bucketCreate</c>.</summary>
    public Task<RailwayGraphQLResponse<BucketCreateData>> BucketCreateAsync(
        BucketCreateInput input,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<BucketCreateData>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.BucketCreate,
                OperationName = "bucketCreate",
                Variables = new { input }
            },
            token,
            cancellationToken);

    /// <summary>Sends <c>bucketS3Credentials</c>. The secret is in the response; callers must not persist it to plan files.</summary>
    public Task<RailwayGraphQLResponse<BucketS3CredentialsData>> BucketS3CredentialsAsync(
        string bucketId,
        string environmentId,
        string projectId,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<BucketS3CredentialsData>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.BucketS3Credentials,
                OperationName = "bucketS3Credentials",
                Variables = new { bucketId, environmentId, projectId }
            },
            token,
            cancellationToken);

    /// <summary>Sends <c>environmentPatchCommitStaged</c>.</summary>
    public Task<RailwayGraphQLResponse<JsonElement>> EnvironmentPatchCommitStagedAsync(
        string environmentId,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<JsonElement>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.EnvironmentPatchCommitStaged,
                OperationName = "environmentPatchCommitStaged",
                Variables = new { environmentId }
            },
            token,
            cancellationToken);

    /// <summary>Sends <c>regions</c>.</summary>
    public Task<RailwayGraphQLResponse<JsonElement>> RegionsAsync(
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<JsonElement>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.Regions,
                OperationName = "regions"
            },
            token,
            cancellationToken);

    /// <summary>
    /// Fetches <c>template(code)</c> and deploys it with <c>templateDeployV2</c> using the returned
    /// <c>id</c> (as <c>templateId</c>) and <c>serializedConfig</c>. Does not invent template UUIDs
    /// or send an empty config.
    /// </summary>
    /// <param name="templateCode">Railway template code such as <c>postgres</c> or <c>redis</c>.</param>
    /// <param name="projectId">Railway project id.</param>
    /// <param name="environmentId">Railway environment id.</param>
    /// <param name="token">Account or workspace token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The <c>templateDeployV2</c> response.</returns>
    public async Task<RailwayGraphQLResponse<TemplateDeployV2Data>> ApplyTemplateAsync(
        string templateCode,
        string projectId,
        string environmentId,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var templateResponse = await TemplateAsync(templateCode, token, cancellationToken).ConfigureAwait(false);
        ThrowIfFailed(templateResponse, "template");

        var template = templateResponse.Data?.Template;
        if (template is null || !HasSerializedConfig(template.SerializedConfig))
        {
            throw new InvalidOperationException(
                $"template(code: \"{templateCode}\") did not return serializedConfig. Cannot call templateDeployV2 with an empty or invented config.");
        }

        if (string.IsNullOrWhiteSpace(template.Id))
        {
            throw new InvalidOperationException(
                $"template(code: \"{templateCode}\") did not return id. Cannot call templateDeployV2 without templateId.");
        }

        var deployResponse = await TemplateDeployV2Async(
            new TemplateDeployV2Input
            {
                ProjectId = projectId,
                EnvironmentId = environmentId,
                TemplateId = template.Id,
                SerializedConfig = template.SerializedConfig
            },
            token,
            cancellationToken).ConfigureAwait(false);

        ThrowIfFailed(deployResponse, "templateDeployV2");
        return deployResponse;
    }
}
