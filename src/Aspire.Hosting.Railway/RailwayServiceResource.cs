using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Materialized Railway compute target for a project or container. This resource is a child of
/// <see cref="RailwayEnvironmentResource"/> and is not added as a top-level model resource.
/// </summary>
public sealed class RailwayServiceResource : Resource, IResourceWithParent<RailwayEnvironmentResource>
{
    /// <summary>
    /// Initializes a new Railway service deployment target.
    /// </summary>
    /// <param name="name">Aspire resource name.</param>
    /// <param name="targetResource">The project or container being deployed.</param>
    /// <param name="parent">The Railway compute environment.</param>
    public RailwayServiceResource(string name, IResource targetResource, RailwayEnvironmentResource parent)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(targetResource);
        ArgumentNullException.ThrowIfNull(parent);

        TargetResource = targetResource;
        Parent = parent;
        RailwayServiceName = name.ToLowerInvariant();
    }

    /// <summary>
    /// Gets the Aspire resource this Railway service represents.
    /// </summary>
    public IResource TargetResource { get; }

    /// <inheritdoc />
    public RailwayEnvironmentResource Parent { get; }

    /// <summary>
    /// Gets or sets the Railway service name used for private DNS
    /// (<c>{name}.railway.internal</c>).
    /// </summary>
    public string RailwayServiceName { get; set; }

    /// <summary>
    /// Gets or sets an optional Railway region for this service. Must be an official
    /// region id from <see href="https://docs.railway.com/deployments/regions"/>:
    /// <c>us-west2</c>, <c>us-east4-eqdc4a</c>, <c>europe-west4-drams3a</c>, or
    /// <c>asia-southeast1-eqsg3a</c>.
    /// </summary>
    /// <remarks>
    /// Must be a <c>Region.region</c> deploy key, not a <c>Query.regions.id</c> airport
    /// code (<c>sjc</c>, <c>iad</c>, <c>ams</c>, <c>sin</c>) and not an older id
    /// (<c>us-west1</c>, <c>us-east4</c>, <c>europe-west4</c>). When set, deploy sends
    /// <c>multiRegionConfig</c> for this region using <c>WithReplicas</c> (or 1 when
    /// omitted). Replica count itself comes from Aspire <c>WithReplicas</c>.
    /// </remarks>
    public string? Region { get; set; }

    /// <summary>
    /// Gets or sets whether this service should sleep when idle. Deploy writes
    /// <c>sleepApplication</c> (there is no GraphQL field named <c>serverless</c>).
    /// </summary>
    /// <remarks>
    /// Sent only when set. Applies to all replicas of the service. There is no
    /// Aspire-core equivalent; configure this through <c>PublishAsRailwayService</c>.
    /// </remarks>
    public bool? Serverless { get; set; }

    /// <summary>
    /// Gets or sets per-replica vCPU for this service. Maps to
    /// <c>ServiceInstanceLimitsUpdateInput.vCPUs</c>.
    /// </summary>
    /// <remarks>
    /// Sent only when set. Must be greater than 0. There is no Aspire-core
    /// <c>WithCpu</c> in Aspire.Hosting 13.5.0; configure this through
    /// <c>PublishAsRailwayService</c>. Railway plan caps (for example 24 vCPU)
    /// are plan-specific and are not hardcoded here — over-plan values fail
    /// with the GraphQL error. Not sent for
    /// <c>PublishAsRailwayPostgres</c> / <c>PublishAsRailwayRedis</c> / buckets.
    /// </remarks>
    public double? Cpu { get; set; }

    /// <summary>
    /// Gets or sets per-replica memory in GB for this service. Maps to
    /// <c>ServiceInstanceLimitsUpdateInput.memoryGB</c>.
    /// </summary>
    /// <remarks>
    /// Sent only when set. Must be greater than 0. Units are GraphQL
    /// <c>memoryGB</c> floats, not config-as-code <c>memoryBytes</c>. There is
    /// no Aspire-core <c>WithMemory</c> in Aspire.Hosting 13.5.0; configure this
    /// through <c>PublishAsRailwayService</c>. Not sent for
    /// <c>PublishAsRailwayPostgres</c> / <c>PublishAsRailwayRedis</c> / buckets.
    /// </remarks>
    public double? MemoryGb { get; set; }

    /// <summary>
    /// Gets or sets a multi-region replica map of official Railway region id to replica
    /// count. Maps to <c>ServiceInstanceUpdateInput.multiRegionConfig</c>.
    /// </summary>
    /// <remarks>
    /// Aspire has no core multi-region API, so this stays Railway-specific. When set,
    /// it is the source of truth for scale and wins over <c>WithReplicas</c> and
    /// <see cref="Region"/>. Do not send <c>numReplicas</c> in that case.
    /// </remarks>
    public Dictionary<string, int>? ReplicaRegions { get; set; }
}
