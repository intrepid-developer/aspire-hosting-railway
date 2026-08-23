using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Railway;

namespace Aspire.Hosting;

/// <summary>
/// Official Aspire <see cref="IContainerRegistry"/> credential helpers
/// used at Railway deploy time. Username and password stay on the
/// registry resource as parameter references and are never written to
/// <c>railway-plan.json</c>.
/// </summary>
public static class ContainerRegistryCredentialExtensions
{
    /// <summary>
    /// Sets the registry username from an Aspire parameter reference.
    /// Resolved at <c>aspire deploy</c> only.
    /// </summary>
    /// <typeparam name="T">A resource that implements <see cref="IContainerRegistry"/>.</typeparam>
    /// <param name="builder">The container registry builder.</param>
    /// <param name="username">Parameter reference for the username.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<T> WithUsername<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<ParameterResource> username)
        where T : IResource, IContainerRegistry
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(username);

        return builder.WithAnnotation(new ContainerRegistryUsernameAnnotation(username.Resource));
    }

    /// <summary>
    /// Sets the registry password or token from an Aspire parameter
    /// reference. Resolved at <c>aspire deploy</c> only. CI can bind
    /// <c>GITHUB_TOKEN</c> to this parameter.
    /// </summary>
    /// <typeparam name="T">A resource that implements <see cref="IContainerRegistry"/>.</typeparam>
    /// <param name="builder">The container registry builder.</param>
    /// <param name="password">Secret parameter reference for the password or token.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<T> WithPassword<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<ParameterResource> password)
        where T : IResource, IContainerRegistry
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(password);

        return builder.WithAnnotation(new ContainerRegistryPasswordAnnotation(password.Resource));
    }
}
