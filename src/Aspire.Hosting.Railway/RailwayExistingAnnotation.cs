using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Adopts an existing Railway project and environment instead of creating a new project.
/// </summary>
public sealed class RailwayExistingAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Initializes a new annotation that points at an existing Railway canvas.
    /// </summary>
    /// <param name="projectId">Parameter whose value is the Railway project id.</param>
    /// <param name="environmentId">Parameter whose value is the Railway environment id.</param>
    public RailwayExistingAnnotation(ParameterResource projectId, ParameterResource environmentId)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentNullException.ThrowIfNull(environmentId);

        ProjectId = projectId;
        EnvironmentId = environmentId;
    }

    /// <summary>
    /// Gets the parameter that supplies the existing Railway project id.
    /// </summary>
    public ParameterResource ProjectId { get; }

    /// <summary>
    /// Gets the parameter that supplies the existing Railway environment id.
    /// </summary>
    public ParameterResource EnvironmentId { get; }
}
