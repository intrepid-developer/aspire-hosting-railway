namespace Aspire.Hosting.Railway;

/// <summary>
/// Resolved private-registry username and password. In-memory only —
/// never written to <c>railway-plan.json</c> or deployment state.
/// </summary>
public sealed class RailwayRegistryCredentials
{
    /// <summary>Gets the registry username.</summary>
    public required string Username { get; init; }

    /// <summary>Gets the registry password or access token.</summary>
    public required string Password { get; init; }
}

/// <summary>
/// Image-host helpers for private-registry pull credentials.
/// </summary>
internal static class RailwayImageRegistry
{
    /// <summary>
    /// Hosts that Railway cannot pull from without credentials when the
    /// image is private. <c>ghcr.io</c> and similar. Docker Hub
    /// (<c>docker.io</c>) and <c>mcr.microsoft.com</c> stay optional.
    /// </summary>
    internal static bool RequiresCredentials(string? image)
    {
        var host = GetHost(image);
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (host.Equals("ghcr.io", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("registry.gitlab.com", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("quay.io", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return host.EndsWith(".pkg.dev", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".azurecr.io", StringComparison.OrdinalIgnoreCase) ||
               host.Contains(".dkr.ecr.", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".jfrog.io", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? GetHost(string? image)
    {
        if (string.IsNullOrWhiteSpace(image) || image.StartsWith('{'))
        {
            return null;
        }

        var reference = image;
        var slash = reference.IndexOf('/');
        if (slash < 0)
        {
            return null;
        }

        var candidate = reference[..slash];
        return candidate.Contains('.') || candidate.Contains(':')
            ? candidate
            : null;
    }

    internal static EnvironmentConfigInput CreateCredentialsPatch(
        string serviceId,
        RailwayRegistryCredentials credentials)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentNullException.ThrowIfNull(credentials);

        return new EnvironmentConfigInput
        {
            Services = new Dictionary<string, EnvironmentConfigService>(StringComparer.Ordinal)
            {
                [serviceId] = new EnvironmentConfigService
                {
                    Deploy = new EnvironmentConfigDeploy
                    {
                        RegistryCredentials = new EnvironmentConfigRegistryCredentials
                        {
                            Username = credentials.Username,
                            Password = credentials.Password
                        }
                    }
                }
            }
        };
    }

    internal static void EnsureCanPull(string serviceName, string image, RailwayRegistryCredentials? credentials)
    {
        if (!RequiresCredentials(image))
        {
            return;
        }

        if (credentials is not null &&
            !string.IsNullOrWhiteSpace(credentials.Username) &&
            !string.IsNullOrWhiteSpace(credentials.Password))
        {
            return;
        }

        var host = GetHost(image) ?? image;
        throw new InvalidOperationException(
            $"Railway cannot pull the private image for service '{serviceName}' from '{host}'. " +
            "Configure official Aspire container-registry credentials with WithUsername / WithPassword " +
            "parameter references and resolve them at deploy time (CI can bind GITHUB_TOKEN to the " +
            "password parameter). Private registry credentials require a Railway Pro plan. " +
            "Do not write username or password into railway-plan.json.");
    }
}
