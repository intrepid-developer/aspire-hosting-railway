namespace Aspire.Railway.Storage;

/// <summary>
/// Resolves Railway bucket S3 endpoint and addressing style for
/// <c>AddRailwayBucketClient</c>. Documented host is
/// <c>t3.storageapi.dev</c> (virtual-hosted).
/// <c>storage.railway.app</c> remains a legacy alias for the same style.
/// Prefer connection-string <c>UrlStyle</c> when present.
/// </summary>
internal static class RailwayBucketS3Addressing
{
    internal const string DefaultEndpoint = "https://t3.storageapi.dev";
    internal const string DocumentedHost = "t3.storageapi.dev";
    internal const string LegacyHost = "storage.railway.app";

    internal const string UrlStyleVirtual = "virtual";
    internal const string UrlStylePath = "path";

    /// <summary>
    /// Maps Railway <c>urlStyle</c> to <c>ForcePathStyle</c>.
    /// <c>virtual</c> is virtual-hosted; <c>path</c> is path-style.
    /// Unknown or unset values return <see langword="null"/>.
    /// </summary>
    internal static bool? ForcePathStyleFromUrlStyle(string? urlStyle)
    {
        if (string.IsNullOrWhiteSpace(urlStyle))
        {
            return null;
        }

        if (urlStyle.Equals(UrlStyleVirtual, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (urlStyle.Equals(UrlStylePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return null;
    }

    /// <summary>
    /// Railway-documented and legacy hosts are virtual-hosted.
    /// Local emulators and unknown hosts default to path-style.
    /// An empty endpoint is treated as the documented Railway host.
    /// </summary>
    internal static bool IsVirtualHostedRailwayEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return true;
        }

        var host = HostOf(endpoint);
        return host.Equals(DocumentedHost, StringComparison.OrdinalIgnoreCase) ||
               host.Equals(LegacyHost, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Prefer an explicit <paramref name="forcePathStyle"/>, then
    /// <paramref name="urlStyle"/>, then the Railway-host heuristic.
    /// </summary>
    internal static bool ResolveForcePathStyle(
        string? endpoint,
        bool? forcePathStyle = null,
        string? urlStyle = null)
    {
        if (forcePathStyle is { } explicitStyle)
        {
            return explicitStyle;
        }

        if (ForcePathStyleFromUrlStyle(urlStyle) is { } fromUrlStyle)
        {
            return fromUrlStyle;
        }

        return !IsVirtualHostedRailwayEndpoint(endpoint);
    }

    private static string HostOf(string endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        var trimmed = endpoint.Trim();
        var slash = trimmed.IndexOf('/');
        if (slash >= 0)
        {
            trimmed = trimmed[..slash];
        }

        var colon = trimmed.IndexOf(':');
        return colon >= 0 ? trimmed[..colon] : trimmed;
    }
}
