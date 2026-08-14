namespace Aspire.Hosting.Railway;

/// <summary>
/// Confirmed Railway GraphQL v2 operation documents. Do not invent extra mutations.
/// Deprecated: <c>pluginCreate</c>, <c>templateDeploy</c> v1.
/// </summary>
public static class RailwayGraphQLOperations
{
    /// <summary>Creates a Railway project. Requires an account or workspace token.</summary>
    public const string ProjectCreate = """
        mutation projectCreate($input: ProjectCreateInput!) {
          projectCreate(input: $input) {
            id
            name
            environments {
              edges {
                node {
                  id
                  name
                }
              }
            }
          }
        }
        """;

    /// <summary>Creates an environment. Pass <c>sourceEnvironmentId</c> to duplicate; <c>ephemeral</c> is reserved for later PRs.</summary>
    public const string EnvironmentCreate = """
        mutation environmentCreate($input: EnvironmentCreateInput!) {
          environmentCreate(input: $input) {
            id
            name
          }
        }
        """;

    /// <summary>Creates a service. Always pass <c>environmentId</c>.</summary>
    public const string ServiceCreate = """
        mutation serviceCreate($input: ServiceCreateInput!) {
          serviceCreate(input: $input) {
            id
            name
          }
        }
        """;

    /// <summary>Updates a service instance (image, start command, and similar settings).</summary>
    public const string ServiceInstanceUpdate = """
        mutation serviceInstanceUpdate($serviceId: String!, $environmentId: String!, $input: ServiceInstanceUpdateInput!) {
          serviceInstanceUpdate(serviceId: $serviceId, environmentId: $environmentId, input: $input)
        }
        """;

    /// <summary>Deploys a service instance from its current source image.</summary>
    public const string ServiceInstanceDeployV2 = """
        mutation serviceInstanceDeployV2($serviceId: String!, $environmentId: String!) {
          serviceInstanceDeployV2(serviceId: $serviceId, environmentId: $environmentId)
        }
        """;

    /// <summary>Upserts a collection of service or shared variables.</summary>
    public const string VariableCollectionUpsert = """
        mutation variableCollectionUpsert($input: VariableCollectionUpsertInput!) {
          variableCollectionUpsert(input: $input)
        }
        """;

    /// <summary>Creates a Railway-provided HTTP domain.</summary>
    public const string ServiceDomainCreate = """
        mutation serviceDomainCreate($input: ServiceDomainCreateInput!) {
          serviceDomainCreate(input: $input) {
            id
            domain
          }
        }
        """;

    /// <summary>Fetches a template by code. Use the returned <c>serializedConfig</c>; never invent template UUIDs.</summary>
    public const string Template = """
        query template($code: String) {
          template(code: $code) {
            id
            code
            serializedConfig
          }
        }
        """;

    /// <summary>Deploys a template using the fetched serialized config.</summary>
    public const string TemplateDeployV2 = """
        mutation templateDeployV2($input: TemplateDeployV2Input!) {
          templateDeployV2(input: $input) {
            projectId
            workflowId
          }
        }
        """;

    /// <summary>Polls a workflow started by template deploy.</summary>
    public const string WorkflowStatus = """
        query workflowStatus($workflowId: String!) {
          workflowStatus(workflowId: $workflowId) {
            status
            error
          }
        }
        """;

    /// <summary>Creates a Railway storage bucket. Region is immutable after create.</summary>
    public const string BucketCreate = """
        mutation bucketCreate($input: BucketCreateInput!) {
          bucketCreate(input: $input) {
            id
            name
          }
        }
        """;

    /// <summary>Reads S3 credentials for a bucket. Endpoint is https://storage.railway.app. Callers must not persist the secret.</summary>
    public const string BucketS3Credentials = """
        query bucketS3Credentials($bucketId: String!, $environmentId: String!) {
          bucketS3Credentials(bucketId: $bucketId, environmentId: $environmentId) {
            accessKeyId
            secretAccessKey
            endpoint
            region
            bucket
          }
        }
        """;

    /// <summary>Commits staged environment patches.</summary>
    public const string EnvironmentPatchCommitStaged = """
        mutation environmentPatchCommitStaged($environmentId: String!) {
          environmentPatchCommitStaged(environmentId: $environmentId)
        }
        """;

    /// <summary>Lists Railway regions.</summary>
    public const string Regions = """
        query regions {
          regions {
            name
          }
        }
        """;
}
