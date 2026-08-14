namespace Aspire.Railway.Storage;

/// <summary>
/// Bucket name resolved from Aspire connection properties.
/// </summary>
public sealed class RailwayBucketSettings
{
    /// <summary>
    /// Gets the bucket name.
    /// </summary>
    public required string BucketName { get; init; }
}
