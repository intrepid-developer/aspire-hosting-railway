using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Railway;

namespace Aspire.Hosting;

/// <summary>
/// Publishes official Aspire Postgres resources as Railway-managed Postgres.
/// </summary>
public static class RailwayPostgresExtensions
{
    /// <summary>
    /// Railway template code for Postgres. Deploy fetches <c>template(code: "postgres")</c>
    /// and calls <c>templateDeployV2</c> with the returned <c>serializedConfig</c>.
    /// </summary>
    public const string TemplateCode = "postgres";

    /// <summary>
    /// Railway variable referenced by consuming services on the private network.
    /// </summary>
    public const string PrivateReferenceVariable = "DATABASE_URL";

    /// <summary>
    /// Marks a Postgres server so deploy uses the Railway Postgres template instead of the
    /// local container image. Local <c>aspire run</c> is unchanged. In publish mode,
    /// <c>WithReference</c> emits <c>${{postgres.DATABASE_URL}}</c> rather than a Docker connection string.
    /// </summary>
    /// <param name="builder">The official Postgres server resource.</param>
    /// <returns>The same resource builder.</returns>
    public static IResourceBuilder<PostgresServerResource> PublishAsRailwayPostgres(
        this IResourceBuilder<PostgresServerResource> builder) =>
        PublishAsRailwayPostgres(builder, configure: null);

    /// <summary>
    /// Marks a Postgres server so deploy uses the Railway Postgres template,
    /// optionally requesting volume backup schedules. PITR enable is not
    /// part of this slice (HA-only mutations on the live schema).
    /// </summary>
    /// <param name="builder">The official Postgres server resource.</param>
    /// <param name="configure">
    /// Optional Railway-specific settings. At least one
    /// <c>VolumeBackup*</c> boolean must be true to write schedule kinds
    /// into the plan. All false / no callback omits the field so deploy
    /// leaves the dashboard as-is.
    /// </param>
    /// <returns>The same resource builder.</returns>
    public static IResourceBuilder<PostgresServerResource> PublishAsRailwayPostgres(
        this IResourceBuilder<PostgresServerResource> builder,
        Action<RailwayPostgresSettings>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ApplicationBuilder.AddRailwayInfrastructureCore();

        var settings = new RailwayPostgresSettings();
        configure?.Invoke(settings);

        builder.WithAnnotation(new RailwayManagedServiceAnnotation(
            kind: TemplateCode,
            serviceName: builder.Resource.Name,
            templateCode: TemplateCode,
            privateReferenceVariable: PrivateReferenceVariable,
            volumeBackupScheduleKinds: ToVolumeBackupScheduleKinds(settings)));

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

    private static IReadOnlyList<string>? ToVolumeBackupScheduleKinds(RailwayPostgresSettings settings)
    {
        var kinds = new List<string>();
        if (settings.VolumeBackupDaily)
        {
            kinds.Add("DAILY");
        }

        if (settings.VolumeBackupWeekly)
        {
            kinds.Add("WEEKLY");
        }

        if (settings.VolumeBackupMonthly)
        {
            kinds.Add("MONTHLY");
        }

        return kinds.Count == 0 ? null : kinds;
    }
}
