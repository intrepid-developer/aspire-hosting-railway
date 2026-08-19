using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Railway;
using Aspire.Hosting.Railway.Storage;

namespace Aspire.Hosting;

/// <summary>
/// AppHost extensions for Railway / local S3-compatible buckets.
/// </summary>
public static class RailwayBucketExtensions
{
    /// <summary>
    /// Maintained Adobe S3Mock image used for local <c>aspire run</c>.
    /// This is not the deprecated CommunityToolkit MinIO package.
    /// </summary>
    public const string LocalS3Image = "adobe/s3mock";

    /// <summary>
    /// Pinned S3Mock tag.
    /// </summary>
    public const string LocalS3ImageTag = "4.9.1";

    /// <summary>
    /// Adds a bucket resource. Locally this starts an S3-compatible container; on deploy the
    /// Railway environment creates a bucket via <c>bucketCreate</c> and
    /// <c>bucketS3Credentials</c>. Region is immutable and buckets are not on private DNS.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Aspire resource name, for example <c>uploads</c>.</param>
    /// <param name="bucketName">Optional bucket name. Defaults to <paramref name="name"/>.</param>
    /// <returns>The bucket resource builder.</returns>
    public static IResourceBuilder<RailwayBucketResource> AddRailwayBucket(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string? bucketName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        builder.AddRailwayInfrastructureCore();

        var resolvedBucketName = string.IsNullOrWhiteSpace(bucketName) ? name : bucketName;
        var resource = new RailwayBucketResource(name, resolvedBucketName);
        resource.Annotations.Add(new RailwayManagedServiceAnnotation(
            kind: "bucket",
            serviceName: name,
            templateCode: null,
            privateReferenceVariable: null));

        var resourceBuilder = builder.AddResource(resource);

        if (builder.ExecutionContext.IsRunMode)
        {
            var emulator = builder.AddContainer($"{name}-s3", LocalS3Image, LocalS3ImageTag)
                .WithHttpEndpoint(targetPort: 9090, name: "s3")
                .WithEnvironment("initialBuckets", resolvedBucketName)
                .WithHttpHealthCheck("/", endpointName: "s3");

            resource.Emulator = emulator.Resource;
            resource.EmulatorEndpoint = emulator.GetEndpoint("s3");
            resourceBuilder.WaitFor(emulator);
        }

        return resourceBuilder;
    }
}
