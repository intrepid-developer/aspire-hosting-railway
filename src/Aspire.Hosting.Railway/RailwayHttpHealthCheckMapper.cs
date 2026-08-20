using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Reads the HTTP path from Aspire <c>WithHttpHealthCheck</c>. That API stores
/// the path in <see cref="HealthCheckAnnotation.Key"/> as
/// <c>{resource}_{endpoint}_{path}_{statusCode}_check</c>; there is no separate
/// path annotation in Aspire.Hosting 13.5.0.
/// </summary>
internal static class RailwayHttpHealthCheckMapper
{
    private const string KeySuffix = "_check";

    /// <summary>
    /// Returns the last HTTP health-check path on <paramref name="resource"/>,
    /// or <see langword="null"/> when none can be mapped. Custom
    /// <c>WithHealthCheck</c> keys that are not HTTP probes are ignored.
    /// Railway always probes for HTTP 200, so a non-200 Aspire statusCode is
    /// ignored. Keys that look like <c>WithHttpHealthCheck</c> but have no
    /// HTTP path fail honestly.
    /// </summary>
    public static string? TryGetPath(IResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        string? path = null;
        foreach (var annotation in resource.Annotations.OfType<HealthCheckAnnotation>())
        {
            if (TryParseHttpHealthCheckKey(resource.Name, annotation.Key, out var parsed))
            {
                path = parsed;
            }
        }

        return path;
    }

    /// <summary>
    /// Parses an Aspire <c>WithHttpHealthCheck</c> key. Returns
    /// <see langword="false"/> for custom <c>WithHealthCheck</c> keys.
    /// </summary>
    internal static bool TryParseHttpHealthCheckKey(string resourceName, string key, out string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        path = "";
        var prefix = resourceName + "_";
        if (!key.StartsWith(prefix, StringComparison.Ordinal) ||
            !key.EndsWith(KeySuffix, StringComparison.Ordinal))
        {
            return false;
        }

        var middle = key[prefix.Length..^KeySuffix.Length];
        var lastUnderscore = middle.LastIndexOf('_');
        if (lastUnderscore <= 0)
        {
            return false;
        }

        var statusPart = middle[(lastUnderscore + 1)..];
        if (!int.TryParse(statusPart, out _))
        {
            return false;
        }

        var endpointAndPath = middle[..lastUnderscore];
        var pathStart = endpointAndPath.IndexOf('/');
        if (pathStart < 0)
        {
            throw new InvalidOperationException(
                $"Resource '{resourceName}' health check '{key}' cannot be mapped to a Railway healthcheckPath. " +
                "Railway probes an HTTP path until status 200 (for example /health). " +
                "Use WithHttpHealthCheck(\"/health\").");
        }

        path = endpointAndPath[pathStart..];
        return !string.IsNullOrWhiteSpace(path);
    }
}
