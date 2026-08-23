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
    /// Railway environment creates the bucket record via <c>bucketCreate</c>, provisions
    /// the instance with an environment patch, then reads <c>bucketS3Credentials</c>.
    /// Region is a Tigris airport code (default <c>iad</c>) and buckets are not on private DNS.
    /// Apply does not create an image-less Railway service to hold bucket variables.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Aspire resource name, for example <c>uploads</c>.</param>
    /// <param name="bucketName">Optional bucket name. Defaults to <paramref name="name"/>.</param>
    /// <param name="configure">
    /// Optional callback. Set <see cref="RailwayBucketResource.Region"/>
    /// to a Tigris <see cref="RailwayBucketRegion"/>. Unset keeps
    /// <c>iad</c>. Region cannot be changed after create.
    /// </param>
    /// <returns>The bucket resource builder.</returns>
    public static IResourceBuilder<RailwayBucketResource> AddRailwayBucket(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string? bucketName = null,
        Action<RailwayBucketResource>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        builder.AddRailwayInfrastructureCore();

        var resolvedBucketName = string.IsNullOrWhiteSpace(bucketName) ? name : bucketName;
        var resource = new RailwayBucketResource(name, resolvedBucketName);
        configure?.Invoke(resource);
        resource.Annotations.Add(CreateManagedAnnotation(resource));

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

    /// <summary>
    /// Sets the Tigris region used when apply provisions the bucket
    /// instance. Unset keeps <c>iad</c>. Region is immutable after
    /// create; changing it means drop + recreate.
    /// </summary>
    /// <param name="builder">The bucket resource builder.</param>
    /// <param name="region">Official Tigris airport code.</param>
    /// <returns>The same resource builder.</returns>
    public static IResourceBuilder<RailwayBucketResource> WithRegion(
        this IResourceBuilder<RailwayBucketResource> builder,
        RailwayBucketRegion region)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Resource.Region = region;
        ReplaceManagedAnnotation(builder.Resource);
        return builder;
    }

    private static void ReplaceManagedAnnotation(RailwayBucketResource resource)
    {
        foreach (var existing in resource.Annotations.OfType<RailwayManagedServiceAnnotation>().ToList())
        {
            resource.Annotations.Remove(existing);
        }

        resource.Annotations.Add(CreateManagedAnnotation(resource));
    }

    private static RailwayManagedServiceAnnotation CreateManagedAnnotation(RailwayBucketResource resource) =>
        new(
            kind: "bucket",
            serviceName: resource.Name,
            templateCode: null,
            privateReferenceVariable: null,
            region: resource.Region is { } region
                ? RailwayBucketRegionMapper.ToRegionId(region)
                : null);
}
