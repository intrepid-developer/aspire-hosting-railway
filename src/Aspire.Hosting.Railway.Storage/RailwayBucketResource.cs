using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Railway.Storage;

/// <summary>
/// Aspire resource for an object bucket. Locally this is backed by an S3-compatible container;
/// on deploy it becomes a Railway bucket (S3 endpoint <c>https://storage.railway.app</c>).
/// </summary>
public sealed class RailwayBucketResource : Resource, IResourceWithConnectionString, IResourceWithWaitSupport, IResourceWithoutLifetime
{
    /// <summary>
    /// Initializes a new bucket resource.
    /// </summary>
    /// <param name="name">Aspire resource name.</param>
    /// <param name="bucketName">Bucket name advertised to clients.</param>
    public RailwayBucketResource(string name, string bucketName)
        : base(name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        BucketName = bucketName;
    }

    /// <summary>
    /// Gets the bucket name used by S3 clients.
    /// </summary>
    public string BucketName { get; }

    /// <summary>
    /// Gets or sets the local emulator container, when running locally.
    /// </summary>
    public ContainerResource? Emulator { get; internal set; }

    /// <summary>
    /// Gets or sets the local S3 endpoint used in run mode.
    /// </summary>
    public EndpointReference? EmulatorEndpoint { get; internal set; }

    /// <inheritdoc />
    public ReferenceExpression ConnectionStringExpression =>
        EmulatorEndpoint is not null
            ? ReferenceExpression.Create($"Endpoint={EmulatorEndpoint.Property(EndpointProperty.Url)};AccessKeyId=s3mock;SecretAccessKey=s3mock;Bucket={BucketName};Region=us-east-1;ForcePathStyle=true")
            : ReferenceExpression.Create($"{BuildPublishConnectionString()}");

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, ReferenceExpression>> GetConnectionProperties()
    {
        if (EmulatorEndpoint is not null)
        {
            yield return new("Endpoint", ReferenceExpression.Create($"{EmulatorEndpoint.Property(EndpointProperty.Url)}"));
            yield return new("AccessKeyId", ReferenceExpression.Create($"s3mock"));
            yield return new("SecretAccessKey", ReferenceExpression.Create($"s3mock"));
            yield return new("Bucket", ReferenceExpression.Create($"{BucketName}"));
            yield return new("Region", ReferenceExpression.Create($"us-east-1"));
            yield return new("ForcePathStyle", ReferenceExpression.Create($"true"));
            yield break;
        }

        yield return new("Endpoint", ReferenceExpression.Create($"{RailwayReferenceExpressions.PrivateServiceVariable(Name, "ENDPOINT")}"));
        yield return new("AccessKeyId", ReferenceExpression.Create($"{RailwayReferenceExpressions.PrivateServiceVariable(Name, "ACCESS_KEY_ID")}"));
        yield return new("SecretAccessKey", ReferenceExpression.Create($"{RailwayReferenceExpressions.PrivateServiceVariable(Name, "SECRET_ACCESS_KEY")}"));
        yield return new("Bucket", ReferenceExpression.Create($"{RailwayReferenceExpressions.PrivateServiceVariable(Name, "BUCKET")}"));
        yield return new("Region", ReferenceExpression.Create($"{RailwayReferenceExpressions.PrivateServiceVariable(Name, "REGION")}"));
        yield return new("ForcePathStyle", ReferenceExpression.Create($"false"));
    }

    private string BuildPublishConnectionString() =>
        string.Join(';',
            $"Endpoint={RailwayReferenceExpressions.PrivateServiceVariable(Name, "ENDPOINT")}",
            $"AccessKeyId={RailwayReferenceExpressions.PrivateServiceVariable(Name, "ACCESS_KEY_ID")}",
            $"SecretAccessKey={RailwayReferenceExpressions.PrivateServiceVariable(Name, "SECRET_ACCESS_KEY")}",
            $"Bucket={RailwayReferenceExpressions.PrivateServiceVariable(Name, "BUCKET")}",
            $"Region={RailwayReferenceExpressions.PrivateServiceVariable(Name, "REGION")}",
            "ForcePathStyle=false");
}
