using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Typed Railway GraphQL v2 client. Unit tests should inject a fake <see cref="HttpMessageHandler"/>.
/// Deploy in this release does not call this client against live Railway.
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
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<RailwayGraphQLResponse<T>>(s_json, cancellationToken)
            .ConfigureAwait(false);

        return payload ?? new RailwayGraphQLResponse<T>();
    }

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
    public Task<RailwayGraphQLResponse<JsonElement>> ServiceDomainCreateAsync(
        ServiceDomainCreateInput input,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<JsonElement>(
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
    public Task<RailwayGraphQLResponse<JsonElement>> TemplateDeployV2Async(
        TemplateDeployV2Input input,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SerializedConfig);
        return SendAsync<JsonElement>(
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
    public Task<RailwayGraphQLResponse<JsonElement>> WorkflowStatusAsync(
        string workflowId,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<JsonElement>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.WorkflowStatus,
                OperationName = "workflowStatus",
                Variables = new { workflowId }
            },
            token,
            cancellationToken);

    /// <summary>Sends <c>bucketCreate</c>.</summary>
    public Task<RailwayGraphQLResponse<JsonElement>> BucketCreateAsync(
        BucketCreateInput input,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<JsonElement>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.BucketCreate,
                OperationName = "bucketCreate",
                Variables = new { input }
            },
            token,
            cancellationToken);

    /// <summary>Sends <c>bucketS3Credentials</c>. The secret is in the response; callers must not persist it to plan files.</summary>
    public Task<RailwayGraphQLResponse<JsonElement>> BucketS3CredentialsAsync(
        string bucketId,
        string environmentId,
        string token,
        CancellationToken cancellationToken = default) =>
        SendAsync<JsonElement>(
            new RailwayGraphQLRequest
            {
                Query = RailwayGraphQLOperations.BucketS3Credentials,
                OperationName = "bucketS3Credentials",
                Variables = new { bucketId, environmentId }
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
    /// Applies a Railway template. This release is a stub: later PRs must fetch
    /// <c>template(code)</c> serializedConfig and call <c>templateDeployV2</c>.
    /// </summary>
    /// <param name="templateCode">Railway template code such as <c>postgres</c> or <c>redis</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that always throws until GraphQL apply is implemented.</returns>
    public Task ApplyTemplateAsync(string templateCode, CancellationToken cancellationToken = default)
    {
        _ = templateCode;
        _ = cancellationToken;
        return Task.FromException(new NotImplementedException(
            "ApplyTemplateAsync will fetch template(code) serializedConfig and call templateDeployV2 in a later PR. Do not invent template UUIDs or report a fake success."));
    }
}
