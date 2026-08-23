using System.Text;

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
    /// Official Railway Postgres template variables used to compose an
    /// Npgsql keyword connection string. Confirmed on
    /// <see href="https://docs.railway.com/databases/postgresql"/>.
    /// </summary>
    internal const string PostgresHostVariable = "PGHOST";
    internal const string PostgresPortVariable = "PGPORT";
    internal const string PostgresUserVariable = "PGUSER";
    internal const string PostgresPasswordVariable = "PGPASSWORD";
    internal const string PostgresDatabaseVariable = "PGDATABASE";
    internal const string PostgresUriVariable = "DATABASE_URL";

    /// <summary>
    /// Official Railway Redis template variables used to compose a
    /// StackExchange.Redis connection string. Confirmed on
    /// <see href="https://docs.railway.com/databases/redis"/>.
    /// </summary>
    internal const string RedisHostVariable = "REDISHOST";
    internal const string RedisPortVariable = "REDISPORT";
    internal const string RedisPasswordVariable = "REDISPASSWORD";
    internal const string RedisUriVariable = "REDIS_URL";

    /// <summary>
    /// Composes <c>Host=;Port=;Username=;Password=;Database=;</c> from
    /// Railway <c>PG*</c> reference variables. The plan stores expressions
    /// only — never resolved passwords.
    /// </summary>
    public static string NpgsqlKeyword(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        return string.Concat(
            "Host=",
            PrivateServiceVariable(serviceName, PostgresHostVariable),
            ";Port=",
            PrivateServiceVariable(serviceName, PostgresPortVariable),
            ";Username=",
            PrivateServiceVariable(serviceName, PostgresUserVariable),
            ";Password=",
            PrivateServiceVariable(serviceName, PostgresPasswordVariable),
            ";Database=",
            PrivateServiceVariable(serviceName, PostgresDatabaseVariable));
    }

    /// <summary>
    /// Composes the Aspire / StackExchange.Redis
    /// <c>host:port,password=</c> form from Railway <c>REDIS*</c>
    /// reference variables. The plan stores expressions only.
    /// </summary>
    public static string StackExchangeRedisKeyword(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        return string.Concat(
            PrivateServiceVariable(serviceName, RedisHostVariable),
            ":",
            PrivateServiceVariable(serviceName, RedisPortVariable),
            ",password=",
            PrivateServiceVariable(serviceName, RedisPasswordVariable));
    }

    /// <summary>
    /// Returns a keyword-form Railway expression when
    /// <paramref name="kind"/> or <paramref name="privateReferenceVariable"/>
    /// identifies official Postgres or Redis.
    /// </summary>
    internal static bool TryKeywordConnectionString(
        string? kind,
        string serviceName,
        string? privateReferenceVariable,
        out string expression)
    {
        if (IsPostgresReference(kind, privateReferenceVariable))
        {
            expression = NpgsqlKeyword(serviceName);
            return true;
        }

        if (IsRedisReference(kind, privateReferenceVariable))
        {
            expression = StackExchangeRedisKeyword(serviceName);
            return true;
        }

        expression = "";
        return false;
    }

    internal static bool IsPostgresReference(string? kind, string? privateReferenceVariable) =>
        string.Equals(kind, "postgres", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(privateReferenceVariable, PostgresUriVariable, StringComparison.Ordinal);

    internal static bool IsRedisReference(string? kind, string? privateReferenceVariable) =>
        string.Equals(kind, "redis", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(privateReferenceVariable, RedisUriVariable, StringComparison.Ordinal);

    /// <summary>
    /// Rewrites each <c>${{name.VAR}}</c> to use a Railway service name from
    /// <paramref name="railwayServiceNames"/> when the names differ only by case
    /// (for example <c>postgres</c> vs <c>Postgres</c>). Works for a single
    /// reference and for composed keyword strings.
    /// </summary>
    public static string RewriteServiceName(string expression, IEnumerable<string> railwayServiceNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(railwayServiceNames);

        const string prefix = "${{";
        const string suffix = "}}";
        var names = railwayServiceNames as IReadOnlyCollection<string> ?? [.. railwayServiceNames];

        if (!expression.Contains(prefix, StringComparison.Ordinal))
        {
            return expression;
        }

        var builder = new StringBuilder(expression.Length);
        var cursor = 0;
        while (cursor < expression.Length)
        {
            var start = expression.IndexOf(prefix, cursor, StringComparison.Ordinal);
            if (start < 0)
            {
                builder.Append(expression, cursor, expression.Length - cursor);
                break;
            }

            builder.Append(expression, cursor, start - cursor);
            var end = expression.IndexOf(suffix, start + prefix.Length, StringComparison.Ordinal);
            if (end < 0)
            {
                builder.Append(expression, start, expression.Length - start);
                break;
            }

            var inner = expression[(start + prefix.Length)..end];
            var separator = inner.IndexOf('.');
            if (separator <= 0)
            {
                builder.Append(expression, start, end + suffix.Length - start);
                cursor = end + suffix.Length;
                continue;
            }

            var serviceName = inner[..separator];
            var variableName = inner[(separator + 1)..];
            var match = names.FirstOrDefault(name =>
                string.Equals(name, serviceName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match) &&
                !string.Equals(match, serviceName, StringComparison.Ordinal))
            {
                builder.Append(PrivateServiceVariable(match, variableName));
            }
            else
            {
                builder.Append(expression, start, end + suffix.Length - start);
            }

            cursor = end + suffix.Length;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Plan-safe marker written for <c>AddRailwayBucket</c> references.
    /// Apply replaces it with the resolved connection string at deploy
    /// time only. Never a Railway <c>${{service.VAR}}</c> (those services
    /// are no longer created) and never a secret value.
    /// </summary>
    internal const string BucketConnectionPlaceholderPrefix = "railway-bucket://";

    /// <summary>
    /// Returns a non-secret plan placeholder such as <c>railway-bucket://uploads</c>.
    /// </summary>
    internal static string BucketConnectionPlaceholder(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return BucketConnectionPlaceholderPrefix + resourceName;
    }

    /// <summary>
    /// Returns whether <paramref name="value"/> is a bucket connection
    /// placeholder that deploy must not resolve as a parameter.
    /// </summary>
    internal static bool IsBucketConnectionPlaceholder(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.StartsWith(BucketConnectionPlaceholderPrefix, StringComparison.Ordinal);
}
