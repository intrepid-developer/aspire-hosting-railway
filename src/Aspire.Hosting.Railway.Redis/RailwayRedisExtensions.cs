using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Railway;

namespace Aspire.Hosting;

/// <summary>
/// Publishes official Aspire Redis resources as Railway-managed Redis.
/// </summary>
public static class RailwayRedisExtensions
{
    /// <summary>
    /// Railway template code for Redis. Deploy fetches <c>template(code: "redis")</c>
    /// and calls <c>templateDeployV2</c> with the returned <c>serializedConfig</c>.
    /// </summary>
    public const string TemplateCode = "redis";

    /// <summary>
    /// Railway variable referenced by consuming services on the private network.
    /// </summary>
    public const string PrivateReferenceVariable = "REDIS_URL";

    /// <summary>
    /// Marks a Redis resource so deploy uses the Railway Redis template instead of the
    /// local container image. Local <c>aspire run</c> is unchanged. In publish mode,
    /// <c>WithReference</c> emits <c>${{redis.REDIS_URL}}</c> rather than a Docker connection string.
    /// </summary>
    /// <param name="builder">The official Redis resource.</param>
    /// <returns>The same resource builder.</returns>
    public static IResourceBuilder<RedisResource> PublishAsRailwayRedis(
        this IResourceBuilder<RedisResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ApplicationBuilder.AddRailwayInfrastructureCore();

        builder.WithAnnotation(new RailwayManagedServiceAnnotation(
            kind: TemplateCode,
            serviceName: builder.Resource.Name,
            templateCode: TemplateCode,
            privateReferenceVariable: PrivateReferenceVariable));

        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            var expression = RailwayReferenceExpressions.PrivateServiceVariable(
                builder.Resource.Name,
                PrivateReferenceVariable);
            builder.WithAnnotation(new ConnectionStringRedirectAnnotation(
                new RailwayReferenceConnectionStringResource(builder.Resource.Name, expression)));
        }

        return builder;
    }
}
