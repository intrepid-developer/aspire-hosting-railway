using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Stores a registry username <see cref="IValueProvider"/> on an
/// <see cref="IContainerRegistry"/> resource. Values are resolved at
/// deploy time only.
/// </summary>
internal sealed class ContainerRegistryUsernameAnnotation : IResourceAnnotation
{
    public ContainerRegistryUsernameAnnotation(IValueProvider username)
    {
        ArgumentNullException.ThrowIfNull(username);
        Username = username;
    }

    public IValueProvider Username { get; }
}

/// <summary>
/// Stores a registry password <see cref="IValueProvider"/> on an
/// <see cref="IContainerRegistry"/> resource. Values are resolved at
/// deploy time only.
/// </summary>
internal sealed class ContainerRegistryPasswordAnnotation : IResourceAnnotation
{
    public ContainerRegistryPasswordAnnotation(IValueProvider password)
    {
        ArgumentNullException.ThrowIfNull(password);
        Password = password;
    }

    public IValueProvider Password { get; }
}
