using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Connection-string resource whose value is a Railway reference expression, never a local Docker string.
/// </summary>
public sealed class RailwayReferenceConnectionStringResource : Resource, IResourceWithConnectionString
{
    /// <summary>
    /// Initializes a connection string that is a Railway <c>${{service.VAR}}</c> reference.
    /// </summary>
    /// <param name="name">Resource name.</param>
    /// <param name="railwayExpression">Railway reference expression.</param>
    public RailwayReferenceConnectionStringResource(string name, string railwayExpression)
        : base(name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(railwayExpression);
        ConnectionStringExpression = ReferenceExpression.Create($"{railwayExpression}");
    }

    /// <inheritdoc />
    public ReferenceExpression ConnectionStringExpression { get; }
}
