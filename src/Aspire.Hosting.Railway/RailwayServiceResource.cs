using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Materialized Railway compute target for a project or container. This resource is a child of
/// <see cref="RailwayEnvironmentResource"/> and is not added as a top-level model resource.
/// </summary>
public sealed class RailwayServiceResource : Resource, IResourceWithParent<RailwayEnvironmentResource>
{
    /// <summary>
    /// Initializes a new Railway service deployment target.
    /// </summary>
    /// <param name="name">Aspire resource name.</param>
    /// <param name="targetResource">The project or container being deployed.</param>
    /// <param name="parent">The Railway compute environment.</param>
    public RailwayServiceResource(string name, IResource targetResource, RailwayEnvironmentResource parent)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(targetResource);
        ArgumentNullException.ThrowIfNull(parent);

        TargetResource = targetResource;
        Parent = parent;
        RailwayServiceName = name.ToLowerInvariant();
    }

    /// <summary>
    /// Gets the Aspire resource this Railway service represents.
    /// </summary>
    public IResource TargetResource { get; }

    /// <inheritdoc />
    public RailwayEnvironmentResource Parent { get; }

    /// <summary>
    /// Gets or sets the Railway service name used for private DNS
    /// (<c>{name}.railway.internal</c>).
    /// </summary>
    public string RailwayServiceName { get; set; }

    /// <summary>
    /// Gets or sets an optional Railway region for this service.
    /// </summary>
    /// <remarks>
    /// Members are the official <c>Region.region</c> deploy keys from
    /// <see href="https://docs.railway.com/deployments/regions"/>. Airport codes
    /// (<c>sjc</c>, <c>iad</c>, <c>ams</c>, <c>sin</c>) and older ids
    /// (<c>us-west1</c>, <c>us-east4</c>, <c>europe-west4</c>) are not members.
    /// When set, deploy sends <c>multiRegionConfig</c> for this region using
    /// <c>WithReplicas</c> (or 1 when omitted). Replica count itself comes from
    /// Aspire <c>WithReplicas</c>.
    /// </remarks>
    public RailwayRegion? Region { get; set; }

    /// <summary>
    /// Gets or sets whether this service should sleep when idle. Deploy writes
    /// <c>sleepApplication</c> (there is no GraphQL field named <c>serverless</c>).
    /// </summary>
    /// <remarks>
    /// Sent only when set. Applies to all replicas of the service. There is no
    /// Aspire-core equivalent; configure this through <c>PublishAsRailwayService</c>.
    /// </remarks>
    public bool? Serverless { get; set; }

    /// <summary>
    /// Gets or sets per-replica vCPU for this service. Maps to
    /// <c>ServiceInstanceLimitsUpdateInput.vCPUs</c>.
    /// </summary>
    /// <remarks>
    /// Sent only when set. Must be greater than 0. There is no Aspire-core
    /// <c>WithCpu</c> in Aspire.Hosting 13.5.0; configure this through
    /// <c>PublishAsRailwayService</c>. Railway plan caps (for example 24 vCPU)
    /// are plan-specific and are not hardcoded here — over-plan values fail
    /// with the GraphQL error. Not sent for
    /// <c>PublishAsRailwayPostgres</c> / <c>PublishAsRailwayRedis</c> / buckets.
    /// </remarks>
    public double? Cpu { get; set; }

    /// <summary>
    /// Gets or sets per-replica memory in GB for this service. Maps to
    /// <c>ServiceInstanceLimitsUpdateInput.memoryGB</c>.
    /// </summary>
    /// <remarks>
    /// Sent only when set. Must be greater than 0. Units are GraphQL
    /// <c>memoryGB</c> floats, not config-as-code <c>memoryBytes</c>. There is
    /// no Aspire-core <c>WithMemory</c> in Aspire.Hosting 13.5.0; configure this
    /// through <c>PublishAsRailwayService</c>. Not sent for
    /// <c>PublishAsRailwayPostgres</c> / <c>PublishAsRailwayRedis</c> / buckets.
    /// </remarks>
    public double? MemoryGb { get; set; }

    /// <summary>
    /// Gets or sets the Railway deploy healthcheck timeout in seconds. Maps to
    /// <c>ServiceInstanceUpdateInput.healthcheckTimeout</c>.
    /// </summary>
    /// <remarks>
    /// Sent only when set. Must be greater than 0. Unset omits the field so
    /// Railway's default (300 seconds) applies. There is no Aspire-core timeout
    /// annotation; configure this through <c>PublishAsRailwayService</c>. The
    /// path comes from Aspire <c>WithHttpHealthCheck</c>. Not sent for
    /// <c>PublishAsRailwayPostgres</c> / <c>PublishAsRailwayRedis</c> / buckets.
    /// Railway probes until HTTP 200, then flips traffic. It is not continuous
    /// monitoring. Origin host is <c>healthcheck.railway.app</c>. Volume-backed
    /// services still have cutover downtime.
    /// </remarks>
    public int? HealthcheckTimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the Railway restart policy. Maps to
    /// <c>ServiceInstanceUpdateInput.restartPolicyType</c>.
    /// </summary>
    /// <remarks>
    /// Sent only when set. Members are the official GraphQL
    /// <c>RestartPolicyType</c> values from
    /// <see href="https://docs.railway.com/deployments/restart-policy"/>.
    /// Unset omits the field so Railway's dashboard default (On Failure)
    /// applies. Either this or <see cref="RestartPolicyMaxRetries"/> can be
    /// set alone. There is no Aspire-core annotation; configure this through
    /// <c>PublishAsRailwayService</c>. Not sent for
    /// <c>PublishAsRailwayPostgres</c> / <c>PublishAsRailwayRedis</c> /
    /// buckets. Free/trial plan caps (Always unavailable, On Failure capped
    /// at 10) are plan-specific and are not hardcoded — over-plan values fail
    /// with the GraphQL error. With multiple replicas, only the crashed
    /// replica restarts.
    /// </remarks>
    public RailwayRestartPolicy? RestartPolicy { get; set; }

    /// <summary>
    /// Gets or sets the maximum restart retries. Maps to
    /// <c>ServiceInstanceUpdateInput.restartPolicyMaxRetries</c>.
    /// </summary>
    /// <remarks>
    /// Sent only when set. Must be greater than 0. Unset omits the field so
    /// Railway's dashboard default (10 retries) applies. Either this or
    /// <see cref="RestartPolicy"/> can be set alone. There is no Aspire-core
    /// annotation; configure this through <c>PublishAsRailwayService</c>. Not
    /// sent for <c>PublishAsRailwayPostgres</c> / <c>PublishAsRailwayRedis</c>
    /// / buckets. Free/trial On Failure caps are not hardcoded.
    /// </remarks>
    public int? RestartPolicyMaxRetries { get; set; }

    /// <summary>
    /// Gets or sets the Railway start command. Maps to
    /// <c>ServiceInstanceUpdateInput.startCommand</c>.
    /// </summary>
    /// <remarks>
    /// Sent only when set. Empty or whitespace fails. Unset omits the field so
    /// the image ENTRYPOINT/CMD applies. On the image/Dockerfile v1 path this
    /// overrides ENTRYPOINT in exec form — there is no shell expansion unless
    /// the command is wrapped, for example
    /// <c>/bin/sh -c "exec … $PORT"</c>. See
    /// <see href="https://docs.railway.com/guides/start-command"/> and
    /// <see href="https://docs.railway.com/deployments/start-command"/>.
    /// Aspire <c>WithArgs</c> is not mapped. There is no Aspire-core
    /// annotation; configure this through <c>PublishAsRailwayService</c>. Not
    /// sent for <c>PublishAsRailwayPostgres</c> / <c>PublishAsRailwayRedis</c>
    /// / buckets.
    /// </remarks>
    public string? StartCommand { get; set; }

    /// <summary>
    /// Gets or sets a single pre-deploy command. Maps to GraphQL
    /// <c>ServiceInstanceUpdateInput.preDeployCommand</c> as a one-element
    /// array.
    /// </summary>
    /// <remarks>
    /// Sent only when set. Empty or whitespace fails. Unset omits the field.
    /// Railway runs this between build and deploy (for example migrations)
    /// on the private network with the app environment. A non-zero exit is
    /// not retried and the deploy stops. It runs in a separate container
    /// with no volume, so the filesystem does not persist. See
    /// <see href="https://docs.railway.com/deployments/pre-deploy-command"/>.
    /// Either this or <see cref="StartCommand"/> can be set alone. There is
    /// no Aspire-core annotation; configure this through
    /// <c>PublishAsRailwayService</c>. Not sent for
    /// <c>PublishAsRailwayPostgres</c> / <c>PublishAsRailwayRedis</c> /
    /// buckets. Config-as-code <c>deploy.preDeployCommand</c> is mapping
    /// only; the apply path is <c>serviceInstanceUpdate</c>.
    /// </remarks>
    public string? PreDeployCommand { get; set; }

    /// <summary>
    /// Gets or sets how long the previous replica stays up after the new
    /// deploy is active. Maps to
    /// <c>ServiceInstanceUpdateInput.overlapSeconds</c>.
    /// </summary>
    /// <remarks>
    /// Sent only when set. Must be greater than or equal to 0 (0 is no wait).
    /// Unset omits the field. Either this or <see cref="DrainingSeconds"/>
    /// can be set alone. This is in-deploy lifecycle (zero-downtime
    /// cutover), not <c>aspire destroy</c>. Volume-backed services cannot do
    /// zero-downtime; overlap does not invent a second volume mount. See
    /// <see href="https://docs.railway.com/guides/deployment-teardown"/> and
    /// <see href="https://docs.railway.com/deployments/deployment-teardown"/>.
    /// There is no Aspire-core annotation; configure this through
    /// <c>PublishAsRailwayService</c>. Not sent for
    /// <c>PublishAsRailwayPostgres</c> / <c>PublishAsRailwayRedis</c> /
    /// buckets. Config-as-code <c>deploy.overlapSeconds</c> and the
    /// <c>RAILWAY_DEPLOYMENT_OVERLAP_SECONDS</c> variable are mapping only;
    /// the apply path is <c>serviceInstanceUpdate</c>.
    /// </remarks>
    public int? OverlapSeconds { get; set; }

    /// <summary>
    /// Gets or sets how long Railway waits after SIGTERM before SIGKILL on
    /// the previous replica. Maps to
    /// <c>ServiceInstanceUpdateInput.drainingSeconds</c>.
    /// </summary>
    /// <remarks>
    /// Sent only when set. Must be greater than or equal to 0 (0 is
    /// immediate kill). Unset omits the field. Either this or
    /// <see cref="OverlapSeconds"/> can be set alone. This is in-deploy
    /// lifecycle (zero-downtime cutover), not <c>aspire destroy</c>. See
    /// <see href="https://docs.railway.com/guides/deployment-teardown"/> and
    /// <see href="https://docs.railway.com/deployments/deployment-teardown"/>.
    /// There is no Aspire-core annotation; configure this through
    /// <c>PublishAsRailwayService</c>. Not sent for
    /// <c>PublishAsRailwayPostgres</c> / <c>PublishAsRailwayRedis</c> /
    /// buckets. Config-as-code <c>deploy.drainingSeconds</c> and the
    /// <c>RAILWAY_DEPLOYMENT_DRAINING_SECONDS</c> variable are mapping only;
    /// the apply path is <c>serviceInstanceUpdate</c>.
    /// </remarks>
    public int? DrainingSeconds { get; set; }

    /// <summary>
    /// Gets or sets the Railway cron schedule. Maps to
    /// <c>ServiceInstanceUpdateInput.cronSchedule</c>.
    /// </summary>
    /// <remarks>
    /// Sent only when set. Empty or whitespace fails. Unset omits the field
    /// so the service stays always-on. Five-field crontab only (minute hour
    /// day month weekday), UTC. Railway's minimum frequency is every 5
    /// minutes; <c>* * * * *</c> and minute-field <c>*/1</c> through
    /// <c>*/4</c> fail. Timezone names are not converted to UTC. The
    /// service starts, runs the start command, and must exit. If it is
    /// still running at the next tick, Railway skips the new run and does
    /// not kill the previous one. Wrong fit for always-on HTTP APIs and
    /// bots; HTTP healthchecks are a poor fit but are not auto-blocked.
    /// Cannot be combined with replicas greater than 1 or
    /// <see cref="Serverless"/> <c>true</c>. There is no Aspire-core
    /// annotation; configure this through <c>PublishAsRailwayService</c>.
    /// Not sent for <c>PublishAsRailwayPostgres</c> /
    /// <c>PublishAsRailwayRedis</c> / buckets. Config-as-code
    /// <c>deploy.cronSchedule</c> is mapping only; the apply path is
    /// <c>serviceInstanceUpdate</c>. See
    /// <see href="https://docs.railway.com/cron-jobs"/> and
    /// <see href="https://docs.railway.com/guides/cron-workers-queues"/>.
    /// </remarks>
    public string? CronSchedule { get; set; }

    /// <summary>
    /// Gets the custom hostnames to bind on this service (for example
    /// <c>api.example.com</c>, apex <c>example.com</c>, or
    /// <c>*.example.com</c>). v1 is hostname strings only.
    /// </summary>
    /// <remarks>
    /// There is no Aspire-core custom-domain annotation; configure this
    /// through <c>PublishAsRailwayService</c>. Requires
    /// <c>WithExternalHttpEndpoints()</c> — private services get neither a
    /// Railway <c>*.up.railway.app</c> service domain nor a custom hostname.
    /// Empty or whitespace hostnames fail. Duplicates in this list fail.
    /// Hostnames are not secretly lowercased; adopt matches existing Railway
    /// domains case-insensitively. Apex, subdomain, and wildcard all use
    /// confirmed <c>customDomainCreate</c>. This integration does not talk to
    /// the user's DNS provider. Railway plan caps are not hardcoded; GraphQL
    /// errors are surfaced. Optional GraphQL <c>targetPort</c> comes from the
    /// Aspire HTTP endpoint when present — there is no separate AppHost
    /// setter. Not sent for <c>PublishAsRailwayPostgres</c> /
    /// <c>PublishAsRailwayRedis</c> / buckets. Destroy deletes domains this
    /// integration created; adopted hostnames are skipped. See
    /// <see href="https://docs.railway.com/networking/domains/working-with-domains"/>
    /// and
    /// <see href="https://docs.railway.com/integrations/api/manage-domains"/>.
    /// </remarks>
    public List<string> CustomDomains { get; } = [];

    /// <summary>
    /// Gets or sets a multi-region replica map of <see cref="RailwayRegion"/> to replica
    /// count. Maps to <c>ServiceInstanceUpdateInput.multiRegionConfig</c>.
    /// </summary>
    /// <remarks>
    /// Aspire has no core multi-region API, so this stays Railway-specific. When set,
    /// it is the source of truth for scale and wins over <c>WithReplicas</c> and
    /// <see cref="Region"/>. Do not send <c>numReplicas</c> in that case.
    /// </remarks>
    public Dictionary<RailwayRegion, int>? ReplicaRegions { get; set; }
}
