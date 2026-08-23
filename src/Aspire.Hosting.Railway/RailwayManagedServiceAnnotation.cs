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
    /// <param name="volumeBackupScheduleKinds">
    /// Optional GraphQL <c>VolumeInstanceBackupScheduleKind</c> strings
    /// (<c>DAILY</c>, <c>WEEKLY</c>, <c>MONTHLY</c>). Omit when empty.
    /// </param>
    /// <param name="region">
    /// Optional plan / GraphQL region string. Buckets: Tigris airport
    /// codes. Templates: official <c>Region.region</c> ids. Omit when unset.
    /// </param>
    public RailwayManagedServiceAnnotation(
        string kind,
        string serviceName,
        string? templateCode = null,
        string? privateReferenceVariable = null,
        IReadOnlyList<string>? volumeBackupScheduleKinds = null,
        string? region = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        Kind = kind;
        ServiceName = serviceName;
        TemplateCode = templateCode;
        PrivateReferenceVariable = privateReferenceVariable;
        VolumeBackupScheduleKinds = volumeBackupScheduleKinds is { Count: > 0 }
            ? volumeBackupScheduleKinds
            : null;
        Region = string.IsNullOrWhiteSpace(region) ? null : region;
    }

    /// <inheritdoc />
    public string Kind { get; }

    /// <inheritdoc />
    public string? TemplateCode { get; }

    /// <inheritdoc />
    public string ServiceName { get; }

    /// <inheritdoc />
    public string? PrivateReferenceVariable { get; }

    /// <inheritdoc />
    public IReadOnlyList<string>? VolumeBackupScheduleKinds { get; }

    /// <inheritdoc />
    public string? Region { get; }
}
