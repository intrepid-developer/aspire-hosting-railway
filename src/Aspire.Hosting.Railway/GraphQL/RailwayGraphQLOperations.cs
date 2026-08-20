namespace Aspire.Hosting.Railway;

/// <summary>
/// Confirmed Railway GraphQL v2 operation documents plus the documented
/// <c>project(id)</c> query. Do not invent extra mutations.
/// Deprecated: <c>pluginCreate</c>, <c>templateDeploy</c> v1.
/// </summary>
public static class RailwayGraphQLOperations
{
    /// <summary>
    /// Lists a project's services, environments, and buckets. Documented in
    /// Railway's GraphQL overview as <c>project(id)</c>; this is the same
    /// confirmed query, with the schema field <c>buckets</c> selected the same
    /// way as <c>services</c> (Relay connection of <c>id</c> + <c>name</c>).
    /// Apply adopts existing services and buckets by name. This is not a new
    /// operation name.
    /// </summary>
    public const string Project = """
        query project($id: String!) {
          project(id: $id) {
            name
            services {
              edges {
                node {
                  id
                  name
                }
              }
            }
            environments {
              edges {
                node {
                  id
                  name
                }
              }
            }
            buckets {
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

    /// <summary>
    /// Updates a service instance. Always pass <c>environmentId</c> (official docs and
    /// this apply path; the live schema marks it optional). Confirmed input fields:
    /// <c>source.image</c>, <c>multiRegionConfig</c>, <c>sleepApplication</c>,
    /// <c>numReplicas</c> when only <c>WithReplicas</c> is set, plus optional
    /// <c>healthcheckPath</c> (String), <c>healthcheckTimeout</c> (Int seconds),
    /// <c>restartPolicyType</c> (<c>RestartPolicyType</c>: <c>ALWAYS</c> |
    /// <c>NEVER</c> | <c>ON_FAILURE</c>), <c>restartPolicyMaxRetries</c>
    /// (Int), <c>startCommand</c> (String), <c>preDeployCommand</c>
    /// (<c>[String!]</c>), <c>overlapSeconds</c> (Int),
    /// <c>drainingSeconds</c> (Int), and <c>cronSchedule</c> (String)
    /// verified on the live schema 2026-08-20. Never send
    /// <c>numReplicas</c> and <c>multiRegionConfig</c> together. Omit unset
    /// healthcheck, restart-policy, start-command, pre-deploy, teardown,
    /// and cron fields; do not send <c>null</c>. ServiceInstance has no
    /// <c>multiRegionConfig</c> read field.
    /// </summary>
    public const string ServiceInstanceUpdate = """
        mutation serviceInstanceUpdate($serviceId: String!, $environmentId: String!, $input: ServiceInstanceUpdateInput!) {
          serviceInstanceUpdate(serviceId: $serviceId, environmentId: $environmentId, input: $input)
        }
        """;

    /// <summary>
    /// Updates per-replica CPU and memory. Always pass <c>environmentId</c> and
    /// <c>serviceId</c> on <c>ServiceInstanceLimitsUpdateInput</c>. Confirmed
    /// optional fields: <c>vCPUs</c> and <c>memoryGB</c> (floats). This is a
    /// different mutation from <c>serviceInstanceUpdate</c>; do not add those
    /// fields onto <c>ServiceInstanceUpdateInput</c>. Official
    /// <see href="https://docs.railway.com/guides/manage-services"/> and live
    /// schema 2026-08-20. Config-as-code equivalent is
    /// <c>deploy.limitOverride.containers</c> (<c>cpu</c> / <c>memoryBytes</c>)
    /// and is not the apply path.
    /// </summary>
    public const string ServiceInstanceLimitsUpdate = """
        mutation serviceInstanceLimitsUpdate($input: ServiceInstanceLimitsUpdateInput!) {
          serviceInstanceLimitsUpdate(input: $input)
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

    /// <summary>
    /// Creates a Railway-provided HTTP domain. Confirmed
    /// <c>ServiceDomainCreateInput.targetPort</c> (Int, live schema
    /// 2026-08-20) is optional; omit when unset.
    /// </summary>
    public const string ServiceDomainCreate = """
        mutation serviceDomainCreate($input: ServiceDomainCreateInput!) {
          serviceDomainCreate(input: $input) {
            id
            domain
          }
        }
        """;

    /// <summary>
    /// Lists Railway-provided and custom domains for a service. Confirmed
    /// <c>domains(environmentId, projectId, serviceId)</c> (live schema
    /// 2026-08-20) returns <c>AllDomains</c>. Always pass all three ids.
    /// </summary>
    public const string Domains = """
        query domains($environmentId: String!, $projectId: String!, $serviceId: String!) {
          domains(environmentId: $environmentId, projectId: $projectId, serviceId: $serviceId) {
            customDomains {
              id
              domain
              targetPort
              status {
                verified
                verificationToken
                verificationDnsHost
                certificateStatus
                dnsRecords {
                  fqdn
                  recordType
                  requiredValue
                  purpose
                  status
                }
              }
            }
            serviceDomains {
              id
              domain
            }
          }
        }
        """;

    /// <summary>
    /// Reads one custom domain by id. Confirmed
    /// <c>customDomain(id, projectId)</c> (live schema 2026-08-20). Used to
    /// re-query status after adopt. <c>verificationToken</c> lives on
    /// <c>CustomDomainStatus</c>, not on <c>DNSRecords</c>.
    /// </summary>
    public const string CustomDomain = """
        query customDomain($id: String!, $projectId: String!) {
          customDomain(id: $id, projectId: $projectId) {
            id
            domain
            targetPort
            status {
              verified
              verificationToken
              verificationDnsHost
              certificateStatus
              dnsRecords {
                fqdn
                recordType
                requiredValue
                purpose
                status
              }
            }
          }
        }
        """;

    /// <summary>
    /// Checks whether a custom hostname can be added. Confirmed
    /// <c>customDomainAvailable(domain)</c> (live schema 2026-08-20) returns
    /// <c>DomainAvailable { available, message }</c>.
    /// </summary>
    public const string CustomDomainAvailable = """
        query customDomainAvailable($domain: String!) {
          customDomainAvailable(domain: $domain) {
            available
            message
          }
        }
        """;

    /// <summary>
    /// Creates a custom hostname. Confirmed
    /// <c>customDomainCreate(input: CustomDomainCreateInput!)</c> (live
    /// schema 2026-08-20). Required input: <c>domain</c>,
    /// <c>environmentId</c>, <c>projectId</c>, <c>serviceId</c>. Optional
    /// <c>targetPort</c> Int; omit when unset. Do not send <c>null</c>. Do
    /// not call <c>customDomainDelete</c> or
    /// <c>customDomainIssueCertificate</c> on this path.
    /// </summary>
    public const string CustomDomainCreate = """
        mutation customDomainCreate($input: CustomDomainCreateInput!) {
          customDomainCreate(input: $input) {
            id
            domain
            targetPort
            status {
              verified
              verificationToken
              verificationDnsHost
              certificateStatus
              dnsRecords {
                fqdn
                recordType
                requiredValue
                purpose
                status
              }
            }
          }
        }
        """;

    /// <summary>
    /// Updates an adopted custom domain target port. Confirmed
    /// <c>customDomainUpdate(environmentId, id, targetPort)</c> (live schema
    /// 2026-08-20). Call only when the Aspire HTTP target port is known and
    /// differs from the adopted domain. Always pass <c>environmentId</c>.
    /// </summary>
    public const string CustomDomainUpdate = """
        mutation customDomainUpdate($environmentId: String!, $id: String!, $targetPort: Int) {
          customDomainUpdate(environmentId: $environmentId, id: $id, targetPort: $targetPort) {
            id
            domain
            targetPort
            status {
              verified
              verificationToken
              verificationDnsHost
              certificateStatus
              dnsRecords {
                fqdn
                recordType
                requiredValue
                purpose
                status
              }
            }
          }
        }
        """;

    /// <summary>Fetches a template by code. Pass the returned <c>id</c> as <c>templateId</c> and use <c>serializedConfig</c>; never invent template UUIDs.</summary>
    public const string Template = """
        query template($code: String) {
          template(code: $code) {
            id
            code
            serializedConfig
          }
        }
        """;

    /// <summary>Deploys a template using the fetched <c>templateId</c> and serialized config.</summary>
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

    /// <summary>
    /// Reads S3 credentials for a bucket. <c>projectId</c> is required by Railway.
    /// The payload field is <c>bucketName</c> (not <c>bucket</c>). Endpoint is
    /// https://storage.railway.app. Callers must not persist the secret.
    /// </summary>
    public const string BucketS3Credentials = """
        query bucketS3Credentials($bucketId: String!, $environmentId: String!, $projectId: String!) {
          bucketS3Credentials(bucketId: $bucketId, environmentId: $environmentId, projectId: $projectId) {
            accessKeyId
            secretAccessKey
            endpoint
            region
            bucketName
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
