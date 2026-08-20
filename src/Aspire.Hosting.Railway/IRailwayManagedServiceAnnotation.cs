using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Marks a resource as a Railway-managed service (template or bucket) so the compute environment
/// can discover it without the core package referencing satellite hosting packages.
/// </summary>
public interface IRailwayManagedServiceAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Gets the Railway service kind, such as <c>postgres</c>, <c>redis</c>, or <c>bucket</c>.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Gets the Railway template code used with <c>template(code:)</c>, or <see langword="null"/> for buckets.
    /// </summary>
    public string? TemplateCode { get; }

    /// <summary>
    /// Gets the Railway service name used in private reference variables.
    /// </summary>
    public string ServiceName { get; }

    /// <summary>
    /// Gets the Railway variable referenced from consuming services, for example <c>DATABASE_URL</c>.
    /// </summary>
    public string? PrivateReferenceVariable { get; }

    /// <summary>
    /// Gets requested volume backup schedule kinds (<c>DAILY</c>,
    /// <c>WEEKLY</c>, <c>MONTHLY</c>) for official Postgres. Null or empty
    /// omits the field so deploy leaves the dashboard as-is. Core apply
    /// reads this without referencing the PostgreSQL hosting package.
    /// </summary>
    public IReadOnlyList<string>? VolumeBackupScheduleKinds { get; }
}
