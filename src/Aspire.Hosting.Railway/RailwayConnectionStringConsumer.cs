using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Chooses Npgsql / StackExchange.Redis keyword connection strings for
/// .NET project consumers, and Railway URI variables for containers and
/// other <c>DATABASE_URL</c> / <c>REDIS_URL</c> processes.
/// </summary>
internal static class RailwayConnectionStringConsumer
{
    /// <summary>
    /// Returns whether <paramref name="resource"/> should receive a
    /// keyword-form connection string (Aspire.Npgsql /
    /// Aspire.StackExchange.Redis). <see cref="ProjectResource"/> and
    /// <see cref="IProjectMetadata"/> are the .NET project signals.
    /// Containers, executables, and Node apps keep the URI.
    /// </summary>
    internal static bool PrefersKeywordConnectionString(IResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return resource is ProjectResource ||
               resource.Annotations.OfType<IProjectMetadata>().Any();
    }

    /// <summary>
    /// Returns the secret-safe Railway expression for a managed database
    /// reference. Keyword form composes official template variables
    /// (<c>PGHOST</c> / <c>REDISHOST</c> and siblings). URI form stays
    /// <c>${{service.DATABASE_URL}}</c> / <c>${{service.REDIS_URL}}</c>.
    /// </summary>
    internal static string PrivateReferenceExpression(
        IRailwayManagedServiceAnnotation managed,
        IResource consumer)
    {
        ArgumentNullException.ThrowIfNull(managed);
        ArgumentNullException.ThrowIfNull(consumer);

        if (PrefersKeywordConnectionString(consumer) &&
            RailwayReferenceExpressions.TryKeywordConnectionString(
                managed.Kind,
                managed.ServiceName,
                managed.PrivateReferenceVariable,
                out var keyword))
        {
            return keyword;
        }

        if (string.IsNullOrWhiteSpace(managed.PrivateReferenceVariable))
        {
            throw new InvalidOperationException(
                $"Railway {managed.Kind} '{managed.ServiceName}' has no private reference variable.");
        }

        return RailwayReferenceExpressions.PrivateServiceVariable(
            managed.ServiceName,
            managed.PrivateReferenceVariable);
    }
}
