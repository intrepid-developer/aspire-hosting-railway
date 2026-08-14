namespace Aspire.Hosting.Railway;

/// <summary>
/// Maps Aspire <c>--environment</c> names onto Railway environment names.
/// </summary>
public static class RailwayEnvironmentNameMapper
{
    /// <summary>
    /// Maps an Aspire environment name to a Railway environment name.
    /// Production becomes <c>production</c> and Staging becomes <c>staging</c> (lowercase).
    /// Other names are lowercased. Callers may override this on
    /// <see cref="RailwayEnvironmentResource.RailwayEnvironmentName"/>.
    /// </summary>
    /// <param name="aspireEnvironmentName">The Aspire / host environment name.</param>
    /// <returns>The Railway environment name.</returns>
    public static string Map(string? aspireEnvironmentName)
    {
        if (string.IsNullOrWhiteSpace(aspireEnvironmentName))
        {
            return "production";
        }

        return aspireEnvironmentName.Trim().ToLowerInvariant();
    }
}
