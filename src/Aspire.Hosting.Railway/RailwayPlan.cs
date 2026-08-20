using System.Text.Json.Serialization;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Publish-time plan written to <c>railway-plan.json</c>. Contains expressions and parameter
/// names only — never token values or resolved passwords.
/// </summary>
public sealed class RailwayPlan
{
    /// <summary>
    /// Gets or sets the plan schema version.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>
    /// Gets or sets the Aspire compute-environment resource name.
    /// </summary>
    [JsonPropertyName("computeEnvironment")]
    public string ComputeEnvironment { get; set; } = "";

    /// <summary>
    /// Gets or sets the Railway environment name (for example <c>production</c> or <c>staging</c>).
    /// </summary>
    [JsonPropertyName("railwayEnvironmentName")]
    public string RailwayEnvironmentName { get; set; } = "";

    /// <summary>
    /// Gets or sets Aspire parameter names that deploy will read. Values are never written.
    /// </summary>
    [JsonPropertyName("parameters")]
    public List<string> Parameters { get; set; } = [];

    /// <summary>
    /// Gets or sets whether an existing Railway project should be adopted.
    /// </summary>
    [JsonPropertyName("adoptExisting")]
    public bool AdoptExisting { get; set; }

    /// <summary>
    /// Gets or sets whether staging should duplicate production when the environment is created.
    /// </summary>
    [JsonPropertyName("duplicateProductionWhenCreatingStaging")]
    public bool DuplicateProductionWhenCreatingStaging { get; set; }

    /// <summary>
    /// Gets or sets whether an empty environment create is opted into (instead of duplicating production).
    /// </summary>
    [JsonPropertyName("createEmptyEnvironment")]
    public bool CreateEmptyEnvironment { get; set; }

    /// <summary>
    /// Gets or sets the container registry endpoint expression, if one was configured.
    /// </summary>
    [JsonPropertyName("containerRegistryEndpoint")]
    public string? ContainerRegistryEndpoint { get; set; }

    /// <summary>
    /// Gets or sets compute services that will be deployed from a container image.
    /// </summary>
    [JsonPropertyName("services")]
    public List<RailwayPlanService> Services { get; set; } = [];

    /// <summary>
    /// Gets or sets Railway-managed databases and buckets.
    /// </summary>
    [JsonPropertyName("managedServices")]
    public List<RailwayPlanManagedService> ManagedServices { get; set; } = [];
}

/// <summary>
/// A compute service entry in <see cref="RailwayPlan"/>.
/// </summary>
public sealed class RailwayPlanService
{
    /// <summary>
    /// Gets or sets the Aspire / Railway service name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the image source expression (parameter or resource name), never a resolved digest secret.
    /// </summary>
    [JsonPropertyName("image")]
    public string? Image { get; set; }

    /// <summary>
    /// Gets or sets environment entries as Railway reference expressions, parameter names, or literals.
    /// </summary>
    [JsonPropertyName("environment")]
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the official Railway <c>Region.region</c> id when a single
    /// region was requested. Plan JSON stores the GraphQL string
    /// (<c>us-west2</c>, <c>us-east4-eqdc4a</c>, <c>europe-west4-drams3a</c>,
    /// <c>asia-southeast1-eqsg3a</c>). AppHosts set <see cref="RailwayRegion"/>
    /// on <see cref="RailwayServiceResource"/>; unknown strings fail at apply.
    /// </summary>
    [JsonPropertyName("region")]
    public string? Region { get; set; }

    /// <summary>
    /// Gets or sets the replica count from Aspire <c>WithReplicas</c> /
    /// <c>ReplicaAnnotation</c>. Omitted when the annotation is absent.
    /// </summary>
    [JsonPropertyName("replicas")]
    public int? Replicas { get; set; }

    /// <summary>
    /// Gets or sets whether <c>sleepApplication</c> was requested. Plan JSON uses
    /// <c>serverless</c>; GraphQL writes <c>sleepApplication</c>.
    /// </summary>
    [JsonPropertyName("serverless")]
    public bool? Serverless { get; set; }

    /// <summary>
    /// Gets or sets per-replica vCPU from <c>PublishAsRailwayService</c>.
    /// Omitted when unset. GraphQL writes <c>vCPUs</c>.
    /// </summary>
    [JsonPropertyName("cpu")]
    public double? Cpu { get; set; }

    /// <summary>
    /// Gets or sets per-replica memory in GB from <c>PublishAsRailwayService</c>.
    /// Omitted when unset. GraphQL writes <c>memoryGB</c>.
    /// </summary>
    [JsonPropertyName("memoryGb")]
    public double? MemoryGb { get; set; }

    /// <summary>
    /// Gets or sets official <c>Region.region</c> id to replica count when
    /// multi-region scale was requested. Keys are GraphQL strings; AppHosts
    /// set <see cref="RailwayRegion"/> on <see cref="RailwayServiceResource"/>.
    /// </summary>
    [JsonPropertyName("replicaRegions")]
    public Dictionary<string, int>? ReplicaRegions { get; set; }

    /// <summary>
    /// Gets or sets the Railway deploy healthcheck path copied from Aspire
    /// <c>WithHttpHealthCheck</c> / <c>HealthCheckAnnotation</c>. Omitted when
    /// no HTTP health check is present. GraphQL writes <c>healthcheckPath</c>.
    /// Railway probes until HTTP 200; a non-200 Aspire statusCode is ignored.
    /// </summary>
    [JsonPropertyName("healthcheckPath")]
    public string? HealthcheckPath { get; set; }

    /// <summary>
    /// Gets or sets the Railway deploy healthcheck timeout in seconds from
    /// <c>PublishAsRailwayService</c>. Omitted when unset (Railway default 300).
    /// GraphQL writes <c>healthcheckTimeout</c>.
    /// </summary>
    [JsonPropertyName("healthcheckTimeout")]
    public int? HealthcheckTimeout { get; set; }

    /// <summary>
    /// Gets or sets the GraphQL <c>RestartPolicyType</c> string when a restart
    /// policy was requested. Plan JSON stores <c>ON_FAILURE</c>,
    /// <c>ALWAYS</c>, or <c>NEVER</c>. AppHosts set
    /// <see cref="RailwayRestartPolicy"/> on <see cref="RailwayServiceResource"/>;
    /// unknown strings fail at apply. Omitted when unset (Railway default On
    /// Failure). GraphQL writes <c>restartPolicyType</c>.
    /// </summary>
    [JsonPropertyName("restartPolicyType")]
    public string? RestartPolicyType { get; set; }

    /// <summary>
    /// Gets or sets the maximum restart retries from
    /// <c>PublishAsRailwayService</c>. Omitted when unset (Railway default 10).
    /// GraphQL writes <c>restartPolicyMaxRetries</c>.
    /// </summary>
    [JsonPropertyName("restartPolicyMaxRetries")]
    public int? RestartPolicyMaxRetries { get; set; }

    /// <summary>
    /// Gets or sets the Railway start command from
    /// <c>PublishAsRailwayService</c>. Omitted when unset so the image
    /// ENTRYPOINT/CMD applies. GraphQL writes <c>startCommand</c>.
    /// </summary>
    [JsonPropertyName("startCommand")]
    public string? StartCommand { get; set; }

    /// <summary>
    /// Gets or sets the Railway pre-deploy command steps from
    /// <c>PublishAsRailwayService</c> <c>PreDeployCommand</c> (one-element
    /// array). Omitted when unset or empty. GraphQL writes
    /// <c>preDeployCommand</c> (<c>[String!]</c>).
    /// </summary>
    [JsonPropertyName("preDeployCommand")]
    public List<string>? PreDeployCommand { get; set; }

    /// <summary>
    /// Gets or sets overlap seconds from
    /// <c>PublishAsRailwayService</c> <c>OverlapSeconds</c>. Omitted when
    /// unset. GraphQL writes <c>overlapSeconds</c> (Int). In-deploy
    /// lifecycle, not <c>aspire destroy</c>.
    /// </summary>
    [JsonPropertyName("overlapSeconds")]
    public int? OverlapSeconds { get; set; }

    /// <summary>
    /// Gets or sets draining seconds from
    /// <c>PublishAsRailwayService</c> <c>DrainingSeconds</c>. Omitted when
    /// unset. GraphQL writes <c>drainingSeconds</c> (Int). In-deploy
    /// lifecycle, not <c>aspire destroy</c>.
    /// </summary>
    [JsonPropertyName("drainingSeconds")]
    public int? DrainingSeconds { get; set; }

    /// <summary>
    /// Gets or sets the Railway cron schedule from
    /// <c>PublishAsRailwayService</c> <c>CronSchedule</c>. Omitted when
    /// unset (always-on). GraphQL writes <c>cronSchedule</c> (String).
    /// Five-field crontab, UTC, minimum every 5 minutes.
    /// </summary>
    [JsonPropertyName("cronSchedule")]
    public string? CronSchedule { get; set; }
}

/// <summary>
/// A managed Postgres, Redis, or bucket entry in <see cref="RailwayPlan"/>.
/// </summary>
public sealed class RailwayPlanManagedService
{
    /// <summary>
    /// Gets or sets the Aspire resource name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the kind (<c>postgres</c>, <c>redis</c>, <c>bucket</c>).
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    /// <summary>
    /// Gets or sets the Railway template code when this entry is a template service.
    /// </summary>
    [JsonPropertyName("templateCode")]
    public string? TemplateCode { get; set; }

    /// <summary>
    /// Gets or sets the private Railway reference variable name, if any.
    /// </summary>
    [JsonPropertyName("privateReferenceVariable")]
    public string? PrivateReferenceVariable { get; set; }
}
