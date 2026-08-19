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

    /// <summary>
    /// Rewrites <c>${{name.VAR}}</c> to use a Railway service name from
    /// <paramref name="railwayServiceNames"/> when the names differ only by case
    /// (for example <c>postgres</c> vs <c>Postgres</c>).
    /// </summary>
    public static string RewriteServiceName(string expression, IEnumerable<string> railwayServiceNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(railwayServiceNames);

        const string prefix = "${{";
        const string suffix = "}}";
        if (!expression.StartsWith(prefix, StringComparison.Ordinal) ||
            !expression.EndsWith(suffix, StringComparison.Ordinal))
        {
            return expression;
        }

        var inner = expression[prefix.Length..^suffix.Length];
        var separator = inner.IndexOf('.');
        if (separator <= 0)
        {
            return expression;
        }

        var serviceName = inner[..separator];
        var variableName = inner[(separator + 1)..];
        var match = railwayServiceNames.FirstOrDefault(name =>
            string.Equals(name, serviceName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(match) || string.Equals(match, serviceName, StringComparison.Ordinal))
        {
            return expression;
        }

        return PrivateServiceVariable(match, variableName);
    }
}
