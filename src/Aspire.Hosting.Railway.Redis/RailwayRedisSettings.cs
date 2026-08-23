namespace Aspire.Hosting.Railway;

/// <summary>
/// Railway-specific settings for official Aspire Redis published with
/// <c>PublishAsRailwayRedis</c>.
/// </summary>
public sealed class RailwayRedisSettings
{
    /// <summary>
    /// Gets or sets the official Railway compute region for the Redis
    /// template service. Unset omits the field so the template follows
    /// the project default. Apply sends
    /// <c>serviceInstanceUpdate.region</c> (not a
    /// <c>templateDeployV2</c> field). Volume-backed services cannot use
    /// replicas. Airport / Tigris codes are not compute ids.
    /// </summary>
    public RailwayRegion? Region { get; set; }
}
