using Amazon.S3;

using Aspire.Railway.Storage;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Client extensions that register <see cref="IAmazonS3"/> from Aspire connection properties.
/// </summary>
public static class RailwayBucketClientExtensions
{
    /// <summary>
    /// Registers <see cref="IAmazonS3"/> for the named Railway / local bucket connection.
    /// Reads <c>ConnectionStrings:{connectionName}</c> using the documented format:
    /// <c>Endpoint=...;AccessKeyId=...;SecretAccessKey=...;Bucket=...;Region=...;ForcePathStyle=true|false</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="connectionName">Connection name, matching the AppHost resource (for example <c>uploads</c>).</param>
    /// <returns>The same builder.</returns>
    public static TBuilder AddRailwayBucketClient<TBuilder>(this TBuilder builder, string connectionName)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        builder.Services.AddKeyedSingleton<IAmazonS3>(connectionName, (services, _) =>
            CreateClient(builder.Configuration, connectionName));

        if (!builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IAmazonS3) && descriptor.ServiceKey is null))
        {
            builder.Services.AddSingleton(services => services.GetRequiredKeyedService<IAmazonS3>(connectionName));
        }

        builder.Services.AddKeyedSingleton(connectionName, (services, _) =>
            new RailwayBucketSettings { BucketName = ResolveOptions(builder.Configuration, connectionName).Bucket ?? connectionName });

        if (!builder.Services.Any(descriptor => descriptor.ServiceType == typeof(RailwayBucketSettings) && descriptor.ServiceKey is null))
        {
            builder.Services.AddSingleton(services => services.GetRequiredKeyedService<RailwayBucketSettings>(connectionName));
        }

        return builder;
    }

    internal static IAmazonS3 CreateClient(IConfiguration configuration, string connectionName)
    {
        var options = ResolveOptions(configuration, connectionName);
        var endpoint = options.Endpoint ?? "https://storage.railway.app";
        var region = string.IsNullOrWhiteSpace(options.Region) ? "us-east-1" : options.Region;
        var forcePathStyle = options.ForcePathStyle ??
            !endpoint.Contains("storage.railway.app", StringComparison.OrdinalIgnoreCase);

        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = forcePathStyle,
            AuthenticationRegion = region
        };

        var accessKey = options.AccessKeyId ?? "placeholder-access-key";
        var secretKey = options.SecretAccessKey ?? "placeholder-secret-key";
        return new AmazonS3Client(accessKey, secretKey, config);
    }

    internal static RailwayBucketConnectionOptions ResolveOptions(IConfiguration configuration, string connectionName)
    {
        var options = RailwayBucketConnectionOptions.Parse(configuration.GetConnectionString(connectionName));
        var section = configuration.GetSection(connectionName);
        options.Endpoint ??= section["Endpoint"];
        options.AccessKeyId ??= section["AccessKeyId"];
        options.SecretAccessKey ??= section["SecretAccessKey"];
        options.Bucket ??= section["Bucket"];
        options.Region ??= section["Region"];
        if (options.ForcePathStyle is null && bool.TryParse(section["ForcePathStyle"], out var forcePathStyle))
        {
            options.ForcePathStyle = forcePathStyle;
        }

        return options;
    }
}
