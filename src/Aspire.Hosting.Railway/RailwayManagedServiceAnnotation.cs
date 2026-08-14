namespace Aspire.Hosting.Railway;

/// <summary>
/// Default <see cref="IRailwayManagedServiceAnnotation"/> used by satellite packages.
/// </summary>
public sealed class RailwayManagedServiceAnnotation : IRailwayManagedServiceAnnotation
{
    /// <summary>
    /// Initializes a new annotation that marks a resource as Railway-managed.
    /// </summary>
    /// <param name="kind">Service kind such as <c>postgres</c>, <c>redis</c>, or <c>bucket</c>.</param>
    /// <param name="serviceName">Railway service name used in <c>${{service.VAR}}</c> references.</param>
    /// <param name="templateCode">Railway template code, or <see langword="null"/> for buckets.</param>
    /// <param name="privateReferenceVariable">Variable name referenced by consumers, if any.</param>
    public RailwayManagedServiceAnnotation(
        string kind,
        string serviceName,
        string? templateCode = null,
        string? privateReferenceVariable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        Kind = kind;
        ServiceName = serviceName;
        TemplateCode = templateCode;
        PrivateReferenceVariable = privateReferenceVariable;
    }

    /// <inheritdoc />
    public string Kind { get; }

    /// <inheritdoc />
    public string? TemplateCode { get; }

    /// <inheritdoc />
    public string ServiceName { get; }

    /// <inheritdoc />
    public string? PrivateReferenceVariable { get; }
}
