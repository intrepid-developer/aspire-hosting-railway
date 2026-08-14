namespace Aspire.Hosting.Railway;

/// <summary>
/// Well-known Railway and Aspire parameter names used by this integration.
/// </summary>
public static class RailwayConstants
{
    /// <summary>
    /// Aspire parameter name for an account or workspace token. Project tokens cannot call <c>projectCreate</c>.
    /// </summary>
    public const string TokenParameterName = "RAILWAY_TOKEN";

    /// <summary>
    /// Alternate environment variable accepted in CI (Railway's documented API token name).
    /// </summary>
    public const string ApiTokenEnvironmentVariableName = "RAILWAY_API_TOKEN";

    /// <summary>
    /// Aspire parameter used to adopt an existing Railway project.
    /// </summary>
    public const string ProjectIdParameterName = "RAILWAY_PROJECT_ID";

    /// <summary>
    /// Aspire parameter used to adopt an existing Railway environment.
    /// </summary>
    public const string EnvironmentIdParameterName = "RAILWAY_ENVIRONMENT_ID";

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
}
