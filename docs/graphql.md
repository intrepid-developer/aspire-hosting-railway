# GraphQL

The integration talks only to Railway GraphQL v2:

```
https://backboard.railway.com/graphql/v2
```

The typed client lives in `src/Aspire.Hosting.Railway/GraphQL/`. Deploy calls `RailwayGraphQLApplyService`. Do not change the public AppHost surface when extending apply.

## Do not invent operations

Use confirmed operations only. Never invent mutation or query names. Never call `pluginCreate`. Deprecated and unused: `pluginCreate`, `templateDeploy` v1.

Unit tests stay offline. They inject a fake `HttpMessageHandler`. Do not fake GraphQL success in a way that hides a missing confirmed operation.

## Confirmed operations

| Operation | Role |
| --- | --- |
| `project` | Documented `project(id)` query. Lists `services`, `environments`, and `buckets` so apply can adopt by name. `buckets` is a field on this query (`id` + `name`), not a new operation. |
| `projectCreate` | Creates a Railway project. Requires an account or workspace token. |
| `environmentCreate` | Creates an environment. Pass `sourceEnvironmentId` to duplicate production. `ephemeral` is reserved for later. |
| `serviceCreate` | Creates a service. Always pass `environmentId`. |
| `serviceInstanceUpdate` | Always pass `environmentId`. Fields used: `source.image`, `multiRegionConfig`, `sleepApplication`, `numReplicas` (only when `WithReplicas` is set alone — never with `multiRegionConfig`), `healthcheckPath`, `healthcheckTimeout`, `restartPolicyType`, `restartPolicyMaxRetries`, `startCommand`, `preDeployCommand`, `overlapSeconds`, `drainingSeconds`, `cronSchedule`. Omit unset fields; do not send `null`. Do not add `vCPUs` / `memoryGB` here. No GraphQL field named `serverless`. `ServiceInstance` has no `multiRegionConfig` read field. |
| `serviceInstanceLimitsUpdate` | Per-replica CPU/RAM after the service id exists. Always `serviceId` + `environmentId`. Optional floats: `vCPUs`, `memoryGB`. Different mutation from `serviceInstanceUpdate`. Not sent for managed Postgres / Redis / buckets. |
| `serviceInstanceDeployV2` | Deploys a service instance from its current `source.image`. |
| `variableCollectionUpsert` | Upserts service or shared variables. |
| `serviceDomainCreate` | Railway-provided HTTP domain. Optional `targetPort` Int when the Aspire HTTP endpoint has one. Omit unset. |
| `domains` | `domains(environmentId, projectId, serviceId)` → `AllDomains` (`customDomains`, `serviceDomains`). Always pass all three ids. |
| `customDomain` | `customDomain(id, projectId)`. Re-query status after adopt. `verificationToken` lives on `CustomDomainStatus`, not `DNSRecords`. |
| `customDomainAvailable` | `customDomainAvailable(domain)` → `DomainAvailable { available, message }`. |
| `customDomainCreate` | `CustomDomainCreateInput`: `domain`, `environmentId`, `projectId`, `serviceId` required; optional `targetPort`. Omit unset; do not send `null`. Do not call `customDomainDelete` or `customDomainIssueCertificate` on this path. |
| `customDomainUpdate` | `customDomainUpdate(environmentId, id, targetPort)`. Only when an adopted domain's known target port differs. |
| `template` | Fetches a template by code. Use returned `id` as `templateId` and returned `serializedConfig`. Never invent template UUIDs. |
| `templateDeployV2` | Deploys that fetched template. |
| `workflowStatus` | Polls a workflow started by template deploy. Apply fails if `workflowId` is missing. |
| `bucketCreate` | Creates a Railway storage bucket. Region is immutable after create. After create, retry `bucketS3Credentials` until a `BucketInstance` exists. |
| `bucketS3Credentials` | Reads S3 credentials. `projectId` required. Select `bucketName` (not `bucket`). Never pass a service id. Do not persist the secret. |
| `environmentPatchCommitStaged` | Commits staged environment patches. |
| `regions` | Lists Railway regions. |
| `environment` | `environment(id: String!, projectId: String)`. Selects `volumeInstances` (`edges { node { id serviceId } }`). Match `node.serviceId` to the official Postgres template service id. Retry if not visible yet. Do not use `adminVolumeInstancesForVolume` or `volumeInstance(id)` unless the id is already known. Service has no `volumes` field. |
| `volumeInstanceBackupScheduleList` | `volumeInstanceBackupScheduleList(volumeInstanceId)` → list of `VolumeInstanceBackupSchedule` (not a connection). |
| `volumeInstanceBackupScheduleUpdate` | `volumeInstanceBackupScheduleUpdate(kinds: [VolumeInstanceBackupScheduleKind!]!, volumeInstanceId)` → `Boolean!`. Enum: `DAILY` / `WEEKLY` / `MONTHLY`. Replaces the kinds set — apply unions requested with already-present. |

Documents are in `RailwayGraphQLOperations`. Apply maps `RailwayRegion` to official `Region.region` strings (`us-west2`, `us-east4-eqdc4a`, `europe-west4-drams3a`, `asia-southeast1-eqsg3a`). Cap total replicas at 50. Config-as-code `deploy.*` names are mapping only.

Custom hostnames: after `serviceDomainCreate`, list `domains`, adopt case-insensitively or `customDomainAvailable` + `customDomainCreate`. Report DNS records as Railway returned them. See [working with domains](https://docs.railway.com/networking/domains/working-with-domains).

## Not in v1

- No project or environment delete. `destroy-{name}` is a stub and must not invent backup or volume deletes.
- Never `pluginCreate`. Never invent `cronCreate` / `scheduleCreate` / `WAL_ARCHIVE_*` / `bucketCreate` of `Postgres-PITR`.
- Do not call `customDomainDelete` or `customDomainIssueCertificate` on this path.
- Do not call `volumeInstanceBackupCreate` / `Delete` / `Lock` / `Restore`, `volumeInstancePITRRestore`, `enablePitrForHaCluster` / `disablePitrForHaCluster`.
- PITR enable is HA-only. Non-HA PITR enable is not a confirmed public mutation.
- Do not send scale / limits / healthcheck / restart / start / pre-deploy / teardown / cron / custom domains for `PublishAsRailwayPostgres` / `PublishAsRailwayRedis` / buckets.
- Do not use `environmentPatchCommit` / staged patches for those compute settings.
- `bucketInstanceDetails` is not used. Adopt buckets from `project.buckets`.
- Persist flatten-safe **ids** only. Tokens, bucket secrets, custom-domain verification tokens, and backup payloads stay out of plan and state.
