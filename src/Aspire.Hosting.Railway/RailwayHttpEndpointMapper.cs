using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Reads Aspire HTTP endpoints for Railway public networking.
/// <c>WithExternalHttpEndpoints()</c> is the public-HTTP signal (same as Azure).
/// There is no competing <c>s.Public</c> flag.
/// </summary>
internal static class RailwayHttpEndpointMapper
{
    /// <summary>
    /// Returns whether <paramref name="resource"/> has an external HTTP or HTTPS
    /// endpoint. Railway service domains and custom hostnames require this.
    /// </summary>
    public static bool HasExternalHttpEndpoint(IResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (!resource.TryGetEndpoints(out var endpoints) || endpoints is null)
        {
            return false;
        }

        return endpoints.Any(IsExternalHttp);
    }

    /// <summary>
    /// Returns the Aspire HTTP endpoint target port when an external HTTP or
    /// HTTPS endpoint has one. Used as optional GraphQL <c>targetPort</c> on
    /// <c>serviceDomainCreate</c> and <c>customDomainCreate</c>.
    /// </summary>
    public static int? TryGetExternalHttpTargetPort(IResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (!resource.TryGetEndpoints(out var endpoints) || endpoints is null)
        {
            return null;
        }

        foreach (var endpoint in endpoints)
        {
            if (IsExternalHttp(endpoint) && endpoint.TargetPort is { } port)
            {
                return port;
            }
        }

        return null;
    }

    private static bool IsExternalHttp(EndpointAnnotation endpoint) =>
        endpoint.IsExternal &&
        (string.Equals(endpoint.UriScheme, "http", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(endpoint.UriScheme, "https", StringComparison.OrdinalIgnoreCase));
}
