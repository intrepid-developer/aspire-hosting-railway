using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Stores <see cref="RailwayEnvironmentExtensions.PublishAsRailwayService{T}"/> customizations
/// until the environment materializes a <see cref="RailwayServiceResource"/>.
/// </summary>
public sealed class RailwayServiceCustomizationAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Initializes a new customization annotation.
    /// </summary>
    /// <param name="configure">Optional callback applied to the materialized Railway service.</param>
    public RailwayServiceCustomizationAnnotation(Action<RailwayServiceResource>? configure)
    {
        Configure = configure;
    }

    /// <summary>
    /// Gets the customization callback, if one was provided.
    /// </summary>
    public Action<RailwayServiceResource>? Configure { get; }

    /// <summary>
    /// Gets or sets an explicit Railway service name. When unset, the Aspire resource name is used.
    /// </summary>
    public string? ServiceName { get; set; }
}
