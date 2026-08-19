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
