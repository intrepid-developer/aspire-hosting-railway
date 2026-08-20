namespace Aspire.Hosting.Railway;

/// <summary>
/// Well-known Railway and Aspire parameter names used by this integration.
/// </summary>
public static class RailwayConstants
{
    /// <summary>
    /// Aspire parameter resource name for the account/workspace token.
    /// Resource names cannot contain underscores, so this is kebab-case.
    /// </summary>
    public const string TokenParameterName = "railway-token";

    /// <summary>
    /// Configuration / environment variable name preferred in AppHosts and CI (<c>RAILWAY_TOKEN</c>).
    /// </summary>
    public const string TokenConfigurationKey = "RAILWAY_TOKEN";

    /// <summary>
    /// Alternate environment variable accepted in CI (Railway's documented API token name).
    /// </summary>
    public const string ApiTokenEnvironmentVariableName = "RAILWAY_API_TOKEN";

    /// <summary>
    /// Aspire parameter resource name used to adopt an existing Railway project.
    /// </summary>
    public const string ProjectIdParameterName = "railway-project-id";

    /// <summary>
    /// Configuration / environment variable name for an existing Railway project id.
    /// </summary>
    public const string ProjectIdConfigurationKey = "RAILWAY_PROJECT_ID";

    /// <summary>
    /// Aspire parameter resource name used to adopt an existing Railway environment.
    /// </summary>
    public const string EnvironmentIdParameterName = "railway-environment-id";

    /// <summary>
    /// Configuration / environment variable name for an existing Railway environment id.
    /// </summary>
    public const string EnvironmentIdConfigurationKey = "RAILWAY_ENVIRONMENT_ID";

    /// <summary>
    /// Railway GraphQL v2 endpoint.
    /// </summary>
    public const string GraphQLEndpoint = "https://backboard.railway.com/graphql/v2";

    /// <summary>
    /// Public S3-compatible endpoint for Railway buckets. Buckets are not on private DNS.
    /// </summary>
    public const string BucketS3Endpoint = "https://storage.railway.app";

    /// <summary>
    /// Railway private DNS suffix. Host addresses are <c>{service}.railway.internal</c>.
    /// </summary>
    public const string PrivateDnsSuffix = "railway.internal";

    /// <summary>
    /// Documented maximum total replicas across all regions
    /// (<see href="https://docs.railway.com/cli/scale"/>).
    /// </summary>
    public const int MaxReplicas = 50;

    /// <summary>
    /// Official Railway compute region identifiers documented at
    /// <see href="https://docs.railway.com/deployments/regions"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> OfficialRegionIds =
    [
        "us-west2",
        "us-east4-eqdc4a",
        "europe-west4-drams3a",
        "asia-southeast1-eqsg3a"
    ];
}
