namespace Aspire.Hosting.Railway;

/// <summary>
/// Builds Railway variable-reference expressions. These are literals for Railway to resolve
/// at deploy time — they must not be concatenated with secrets or local endpoint URLs.
/// </summary>
public static class RailwayReferenceExpressions
{
    /// <summary>
    /// Creates a private Railway reference such as <c>${{postgres.DATABASE_URL}}</c>.
    /// </summary>
    /// <param name="serviceName">Railway service name.</param>
    /// <param name="variableName">Variable on that service.</param>
    /// <returns>The Railway reference expression.</returns>
    public static string PrivateServiceVariable(string serviceName, string variableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        return $"${{{{{serviceName}.{variableName}}}}}";
    }
}
