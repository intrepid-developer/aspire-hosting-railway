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
    /// When set, deploy sends <c>multiRegionConfig</c> for this region using
    /// <c>WithReplicas</c> (or 1 when <c>WithReplicas</c> is omitted). Replica count
    /// itself comes from Aspire <c>WithReplicas</c>, not from this type.
    /// </remarks>
    public string? Region { get; set; }

    /// <summary>
    /// Gets or sets whether this service should sleep when idle (Railway serverless).
    /// Maps to <c>ServiceInstanceUpdateInput.sleepApplication</c>.
    /// </summary>
    /// <remarks>
    /// Sent only when set. There is no Aspire-core equivalent; configure this through
    /// <c>PublishAsRailwayService</c>.
    /// </remarks>
    public bool? Serverless { get; set; }

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
