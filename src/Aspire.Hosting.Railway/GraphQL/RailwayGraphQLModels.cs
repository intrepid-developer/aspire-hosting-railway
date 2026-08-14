using System.Text.Json;
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
    public JsonElement SerializedConfig { get; set; }
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

/// <summary>Relay edge wrapping a named Railway resource.</summary>
public sealed class RailwayNamedResourceEdge
{
    /// <summary>Gets or sets the node.</summary>
    [JsonPropertyName("node")]
    public RailwayNamedResource? Node { get; set; }
}

/// <summary>Relay connection of named Railway resources.</summary>
public sealed class RailwayNamedResourceConnection
{
    /// <summary>Gets or sets the edges.</summary>
    [JsonPropertyName("edges")]
    public List<RailwayNamedResourceEdge>? Edges { get; set; }
}

/// <summary>Project payload returned by <c>projectCreate</c>.</summary>
public sealed class RailwayProject
{
    /// <summary>Gets or sets the project id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the project name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets environments created with the project (typically <c>production</c>).</summary>
    [JsonPropertyName("environments")]
    public RailwayNamedResourceConnection? Environments { get; set; }
}

/// <summary>Data wrapper for <c>projectCreate</c>.</summary>
public sealed class ProjectCreateData
{
    /// <summary>Gets or sets the created project.</summary>
    [JsonPropertyName("projectCreate")]
    public RailwayProject? ProjectCreate { get; set; }
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
    public JsonElement SerializedConfig { get; set; }
}

/// <summary>Data wrapper for <c>templateDeployV2</c>.</summary>
public sealed class TemplateDeployV2Data
{
    /// <summary>Gets or sets the deploy result.</summary>
    [JsonPropertyName("templateDeployV2")]
    public TemplateDeployV2Result? TemplateDeployV2 { get; set; }
}

/// <summary>Result of <c>templateDeployV2</c>.</summary>
public sealed class TemplateDeployV2Result
{
    /// <summary>Gets or sets the project id.</summary>
    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }

    /// <summary>Gets or sets the workflow id used with <c>workflowStatus</c>.</summary>
    [JsonPropertyName("workflowId")]
    public string? WorkflowId { get; set; }
}

/// <summary>Data wrapper for <c>workflowStatus</c>.</summary>
public sealed class WorkflowStatusData
{
    /// <summary>Gets or sets the workflow status payload.</summary>
    [JsonPropertyName("workflowStatus")]
    public WorkflowStatusResult? WorkflowStatus { get; set; }
}

/// <summary>Status payload for a Railway workflow.</summary>
public sealed class WorkflowStatusResult
{
    /// <summary>Gets or sets the status string (for example <c>Complete</c> or <c>Error</c>).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Gets or sets an error message when the workflow failed.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>Data wrapper for <c>bucketCreate</c>.</summary>
public sealed class BucketCreateData
{
    /// <summary>Gets or sets the created bucket.</summary>
    [JsonPropertyName("bucketCreate")]
    public RailwayNamedResource? BucketCreate { get; set; }
}

/// <summary>Data wrapper for <c>bucketS3Credentials</c>.</summary>
public sealed class BucketS3CredentialsData
{
    /// <summary>Gets or sets the credentials. Callers must not persist the secret to plan files or deployment state.</summary>
    [JsonPropertyName("bucketS3Credentials")]
    public BucketS3Credentials? BucketS3Credentials { get; set; }
}

/// <summary>S3-compatible credentials for a Railway bucket.</summary>
public sealed class BucketS3Credentials
{
    /// <summary>Gets or sets the access key id.</summary>
    [JsonPropertyName("accessKeyId")]
    public string? AccessKeyId { get; set; }

    /// <summary>Gets or sets the secret access key. Never write this to plan files.</summary>
    [JsonPropertyName("secretAccessKey")]
    public string? SecretAccessKey { get; set; }

    /// <summary>Gets or sets the S3 endpoint.</summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    /// <summary>Gets or sets the region.</summary>
    [JsonPropertyName("region")]
    public string? Region { get; set; }

    /// <summary>Gets or sets the bucket name.</summary>
    [JsonPropertyName("bucket")]
    public string? Bucket { get; set; }
}

/// <summary>Data wrapper for <c>serviceDomainCreate</c>.</summary>
public sealed class ServiceDomainCreateData
{
    /// <summary>Gets or sets the created domain.</summary>
    [JsonPropertyName("serviceDomainCreate")]
    public RailwayServiceDomain? ServiceDomainCreate { get; set; }
}

/// <summary>Railway-provided HTTP domain.</summary>
public sealed class RailwayServiceDomain
{
    /// <summary>Gets or sets the domain id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the hostname.</summary>
    [JsonPropertyName("domain")]
    public string? Domain { get; set; }
}

/// <summary>Data wrapper for the <c>template</c> query.</summary>
public sealed class TemplateData
{
    /// <summary>Gets or sets the template.</summary>
    [JsonPropertyName("template")]
    public RailwayTemplate? Template { get; set; }
}
