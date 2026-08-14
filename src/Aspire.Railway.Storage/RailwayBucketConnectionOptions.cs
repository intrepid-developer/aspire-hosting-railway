namespace Aspire.Railway.Storage;

/// <summary>
/// Connection properties used to construct <c>IAmazonS3</c>.
/// </summary>
/// <remarks>
/// Connection string format (semicolon-delimited):
/// <c>Endpoint=https://storage.railway.app;AccessKeyId=...;SecretAccessKey=...;Bucket=uploads;Region=auto;ForcePathStyle=false</c>
/// </remarks>
public sealed class RailwayBucketConnectionOptions
{
    /// <summary>Gets or sets the S3 API endpoint.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Gets or sets the access key id.</summary>
    public string? AccessKeyId { get; set; }

    /// <summary>Gets or sets the secret access key.</summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>Gets or sets the bucket name.</summary>
    public string? Bucket { get; set; }

    /// <summary>Gets or sets the region. Railway bucket regions are immutable.</summary>
    public string? Region { get; set; }

    /// <summary>
    /// Gets or sets whether to use path-style addressing. Local S3-compatible containers
    /// typically need <see langword="true"/>; Railway virtual-hosted buckets use <see langword="false"/>.
    /// </summary>
    public bool? ForcePathStyle { get; set; }

    /// <summary>
    /// Parses a semicolon-delimited connection string.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>Parsed options.</returns>
    public static RailwayBucketConnectionOptions Parse(string? connectionString)
    {
        var options = new RailwayBucketConnectionOptions();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return options;
        }

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator];
            var value = part[(separator + 1)..];
            if (key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
            {
                options.Endpoint = value;
            }
            else if (key.Equals("AccessKeyId", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("AccessKey", StringComparison.OrdinalIgnoreCase))
            {
                options.AccessKeyId = value;
            }
            else if (key.Equals("SecretAccessKey", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("SecretKey", StringComparison.OrdinalIgnoreCase))
            {
                options.SecretAccessKey = value;
            }
            else if (key.Equals("Bucket", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("BucketName", StringComparison.OrdinalIgnoreCase))
            {
                options.Bucket = value;
            }
            else if (key.Equals("Region", StringComparison.OrdinalIgnoreCase))
            {
                options.Region = value;
            }
            else if (key.Equals("ForcePathStyle", StringComparison.OrdinalIgnoreCase) &&
                     bool.TryParse(value, out var forcePathStyle))
            {
                options.ForcePathStyle = forcePathStyle;
            }
        }

        return options;
    }
}
