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
    /// Gets or sets an optional Railway region for this service.
    /// </summary>
    public string? Region { get; set; }
}
