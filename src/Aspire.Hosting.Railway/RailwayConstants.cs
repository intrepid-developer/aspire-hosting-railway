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
    /// Documented S3-compatible endpoint for Railway buckets
    /// (<see href="https://docs.railway.com/storage-buckets"/>).
    /// Virtual-hosted. Prefer <c>bucketS3Credentials.endpoint</c> when present.
    /// <c>storage.railway.app</c> is a legacy alias that still serves.
    /// Buckets are not on private DNS.
    /// </summary>
    public const string BucketS3Endpoint = "https://t3.storageapi.dev";

    /// <summary>
    /// Default Tigris bucket region when AppHost has no mapped bucket region.
    /// Airport codes only (<c>iad</c> / <c>sjc</c> / <c>ams</c> / <c>sin</c>),
    /// not compute <c>Region.region</c> strings.
    /// </summary>
    public const string DefaultBucketRegion = "iad";

    /// <summary>
    /// Official Tigris bucket region codes documented by Railway
    /// (<see href="https://docs.railway.com/cli/bucket"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> OfficialBucketRegionIds = RailwayBucketRegionMapper.OfficialIds;

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
    /// Official Railway compute deploy keys (<c>Region.region</c>) documented at
    /// <see href="https://docs.railway.com/deployments/regions"/>. AppHosts set
    /// <see cref="RailwayRegion"/>; these strings are the GraphQL / plan values.
    /// Not airport codes (<c>Query.regions.id</c>: sjc/iad/ams/sin) and not older
    /// ids (us-west1, us-east4, europe-west4).
    /// </summary>
    public static readonly IReadOnlyList<string> OfficialRegionIds = RailwayRegionMapper.OfficialRegionIds;

    /// <summary>
    /// Official GraphQL <c>RestartPolicyType</c> values confirmed on the live
    /// schema 2026-08-20. AppHosts set <see cref="RailwayRestartPolicy"/>;
    /// these strings are the GraphQL / plan values.
    /// </summary>
    public static readonly IReadOnlyList<string> OfficialRestartPolicyTypes =
        RailwayRestartPolicyMapper.OfficialTypes;

    /// <summary>
    /// Official GraphQL <c>VolumeInstanceBackupScheduleKind</c> values
    /// confirmed on the live schema 2026-08-20. AppHosts set booleans on
    /// <c>PublishAsRailwayPostgres</c>; these strings are the GraphQL /
    /// plan values. Product retention is mapping only.
    /// </summary>
    public static readonly IReadOnlyList<string> OfficialVolumeBackupScheduleKinds =
        RailwayVolumeBackupSchedule.OfficialKinds;
}
