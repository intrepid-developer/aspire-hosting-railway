using System.Text.Json.Serialization;

namespace Aspire.Hosting.Railway;

/// <summary>Input for <c>projectCreate</c>.</summary>
public sealed class ProjectCreateInput
{
    /// <summary>Gets or sets the project name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets an optional workspace id.</summary>
    [JsonPropertyName("workspaceId")]
    public string? WorkspaceId { get; set; }
}

/// <summary>Input for <c>environmentCreate</c>.</summary>
public sealed class EnvironmentCreateInput
{
    /// <summary>Gets or sets the project id.</summary>
    [JsonPropertyName("projectId")]
    public required string ProjectId { get; set; }

    /// <summary>Gets or sets the environment name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>Gets or sets the source environment to duplicate, when creating staging from production.</summary>
    [JsonPropertyName("sourceEnvironmentId")]
    public string? SourceEnvironmentId { get; set; }

    /// <summary>Gets or sets whether the environment is ephemeral. Public PR-env APIs are not part of this release.</summary>
    [JsonPropertyName("ephemeral")]
    public bool? Ephemeral { get; set; }
}

/// <summary>Input for <c>serviceCreate</c>. Always include <see cref="EnvironmentId"/>.</summary>
public sealed class ServiceCreateInput
{
    /// <summary>Gets or sets the project id.</summary>
    [JsonPropertyName("projectId")]
    public required string ProjectId { get; set; }

    /// <summary>Gets or sets the environment id. Required for this integration.</summary>
    [JsonPropertyName("environmentId")]
    public required string EnvironmentId { get; set; }

    /// <summary>Gets or sets the service name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }
}

/// <summary>Input for <c>serviceInstanceUpdate</c>.</summary>
public sealed class ServiceInstanceUpdateInput
{
    /// <summary>Gets or sets the image source. Railway has no registry; push to GHCR or Docker Hub first.</summary>
    [JsonPropertyName("source")]
    public ServiceSourceInput? Source { get; set; }
}

/// <summary>Image or repo source for a service instance.</summary>
public sealed class ServiceSourceInput
{
    /// <summary>Gets or sets the container image reference.</summary>
    [JsonPropertyName("image")]
    public string? Image { get; set; }
}

/// <summary>Input for <c>variableCollectionUpsert</c>.</summary>
public sealed class VariableCollectionUpsertInput
{
    /// <summary>Gets or sets the project id.</summary>
    [JsonPropertyName("projectId")]
    public required string ProjectId { get; set; }

    /// <summary>Gets or sets the environment id.</summary>
    [JsonPropertyName("environmentId")]
    public required string EnvironmentId { get; set; }

    /// <summary>Gets or sets the optional service id. Omit for shared variables.</summary>
    [JsonPropertyName("serviceId")]
    public string? ServiceId { get; set; }

    /// <summary>Gets or sets the variables to upsert. Callers must not log this object when it contains secrets.</summary>
    [JsonPropertyName("variables")]
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Input for <c>serviceDomainCreate</c>.</summary>
public sealed class ServiceDomainCreateInput
{
    /// <summary>Gets or sets the service id.</summary>
    [JsonPropertyName("serviceId")]
    public required string ServiceId { get; set; }

    /// <summary>Gets or sets the environment id.</summary>
    [JsonPropertyName("environmentId")]
    public required string EnvironmentId { get; set; }
}

/// <summary>Input for <c>templateDeployV2</c>.</summary>
public sealed class TemplateDeployV2Input
{
    /// <summary>Gets or sets the project id.</summary>
    [JsonPropertyName("projectId")]
    public required string ProjectId { get; set; }

    /// <summary>Gets or sets the environment id.</summary>
    [JsonPropertyName("environmentId")]
    public required string EnvironmentId { get; set; }

    /// <summary>Gets or sets serialized config fetched from <c>template(code:)</c>. Never empty and never invented.</summary>
    [JsonPropertyName("serializedConfig")]
    public required string SerializedConfig { get; set; }
}

/// <summary>Input for <c>bucketCreate</c>.</summary>
public sealed class BucketCreateInput
{
    /// <summary>Gets or sets the project id.</summary>
    [JsonPropertyName("projectId")]
    public required string ProjectId { get; set; }

    /// <summary>Gets or sets the environment id.</summary>
    [JsonPropertyName("environmentId")]
    public required string EnvironmentId { get; set; }

    /// <summary>Gets or sets the bucket display name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the region. Immutable after create.</summary>
    [JsonPropertyName("region")]
    public string? Region { get; set; }
}

/// <summary>Payload for id/name resources.</summary>
public sealed class RailwayNamedResource
{
    /// <summary>Gets or sets the id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>Data wrapper for <c>projectCreate</c>.</summary>
public sealed class ProjectCreateData
{
    /// <summary>Gets or sets the created project.</summary>
    [JsonPropertyName("projectCreate")]
    public RailwayNamedResource? ProjectCreate { get; set; }
}

/// <summary>Data wrapper for <c>environmentCreate</c>.</summary>
public sealed class EnvironmentCreateData
{
    /// <summary>Gets or sets the created environment.</summary>
    [JsonPropertyName("environmentCreate")]
    public RailwayNamedResource? EnvironmentCreate { get; set; }
}

/// <summary>Data wrapper for <c>serviceCreate</c>.</summary>
public sealed class ServiceCreateData
{
    /// <summary>Gets or sets the created service.</summary>
    [JsonPropertyName("serviceCreate")]
    public RailwayNamedResource? ServiceCreate { get; set; }
}

/// <summary>Template document returned by <c>template(code:)</c>.</summary>
public sealed class RailwayTemplate
{
    /// <summary>Gets or sets the template id returned by Railway. Do not invent this value.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the template code (<c>postgres</c> or <c>redis</c>).</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>Gets or sets the serialized config that must be passed to <c>templateDeployV2</c>.</summary>
    [JsonPropertyName("serializedConfig")]
    public string? SerializedConfig { get; set; }
}

/// <summary>Data wrapper for the <c>template</c> query.</summary>
public sealed class TemplateData
{
    /// <summary>Gets or sets the template.</summary>
    [JsonPropertyName("template")]
    public RailwayTemplate? Template { get; set; }
}
