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
| `project` | Documented `project(id)` query. Lists `services`, `environments`, and `buckets` so apply can adopt by name. `buckets` is a field on the same confirmed query (Relay connection of `id` + `name`), not a new operation. |
| `projectCreate` | Creates a Railway project. Requires an account or workspace token. |
| `environmentCreate` | Creates an environment. Pass `sourceEnvironmentId` to duplicate production. `ephemeral` is reserved for later. |
| `serviceCreate` | Creates a service. Always pass `environmentId`. |
| `serviceInstanceUpdate` | Updates a service instance. Always pass `environmentId` (official docs and this apply path; live schema marks the argument optional). Confirmed `ServiceInstanceUpdateInput` fields used here: `source.image`; `multiRegionConfig` (JSON map of official `Region.region` id → `{ numReplicas }`, mapped from AppHost `RailwayRegion`); `sleepApplication` (`railway.json` `deploy.sleepApplication` — there is no GraphQL field named `serverless`; applies to all replicas); `numReplicas` as the documented single-region path when only `WithReplicas` is set ([autoscale](https://docs.railway.com/guides/autoscale-horizontally)). Never send `numReplicas` and `multiRegionConfig` together. Do not add `vCPUs` / `memoryGB` onto this input. See [regions](https://docs.railway.com/deployments/regions), [multi-region configuration](https://docs.railway.com/reference/config-as-code#multi-region-configuration), and [scale](https://docs.railway.com/cli/scale). |
| `serviceInstanceLimitsUpdate` | Updates per-replica CPU and memory after the service id exists. Confirmed on the live schema (`ServiceInstanceLimitsUpdateInput`, 2026-08-20) and [manage services](https://docs.railway.com/guides/manage-services). Always pass `serviceId` and `environmentId`. Optional floats: `vCPUs`, `memoryGB`. Return is `Boolean!`. Railway staff confirmed this mutation on [Central Station](https://station.railway.com/questions/programmatically-setting-instance-memory-016d6ad4) (2026-02); older no-op reports were later marked fixed. Do not invent other limit mutations or fields. Related reads (`serviceInstanceLimits`, `serviceInstanceLimitOverride`) exist and are not used. Config-as-code mapping only: `deploy.limitOverride.containers` (`cpu` / `memoryBytes`) in [railway.schema.json](https://railway.com/railway.schema.json) — GraphQL uses GB and vCPU floats, not bytes. Not sent for managed Postgres / Redis / buckets. |
| `serviceInstanceDeployV2` | Deploys a service instance from its current `source.image`. |
| `variableCollectionUpsert` | Upserts service or shared variables. |
| `serviceDomainCreate` | Creates a Railway-provided HTTP domain. |
| `template` | Fetches a template by code. Use the returned `id` as `templateId` and the returned `serializedConfig`. Never invent template UUIDs. |
| `templateDeployV2` | Deploys that fetched template. |
| `workflowStatus` | Polls a workflow started by template deploy. Apply fails if `workflowId` is missing. |
| `bucketCreate` | Creates a Railway storage bucket. Region is immutable after create. |
| `bucketS3Credentials` | Reads S3 credentials. `projectId` is required. Select `bucketName` (not `bucket`). Do not persist the secret. |
| `environmentPatchCommitStaged` | Commits staged environment patches. |
| `regions` | Lists Railway regions. |

Documents are in `RailwayGraphQLOperations`. Confirmed operations do not include project or environment delete; `destroy-{name}` does not invent those mutations.

`serviceInstanceUpdate` is the only mutation that sets compute scale, region, and `sleepApplication`. `serviceInstanceLimitsUpdate` is the only mutation that sets per-replica `vCPUs` / `memoryGB`. Do not add those fields onto `ServiceInstanceUpdateInput`. `serviceCreate` does not take those fields. `multiRegionConfig` is a JSON scalar (`{ "us-west2": { "numReplicas": 2 } }`). `ServiceInstance` has no `multiRegionConfig` read field; do not invent a read-back query. AppHosts set `RailwayRegion`; apply maps members to official `Region.region` strings (`us-west2`, `us-east4-eqdc4a`, `europe-west4-drams3a`, `asia-southeast1-eqsg3a`), not `Query.regions.id` airport codes. Cap total replicas at 50 (product/CLI docs). Replicas cannot be used with [volumes](https://docs.railway.com/volumes/reference); do not send `numReplicas` / `multiRegionConfig` / `serviceInstanceLimitsUpdate` for `PublishAsRailwayPostgres` / `PublishAsRailwayRedis`. Do not invent other scale or limit mutations or fields. Do not use `environmentPatchCommit` / staged patches for CPU/RAM in v1.

`project.buckets` is a field on the documented `project(id)` query (Relay `edges { node { id name } }`, same shape as `services`). It was verified on Railway's GraphQL schema (`Project.buckets: ProjectBucketsConnection` of `Bucket { id name }`). There is no separate confirmed buckets-list query; apply does not invent one. `bucketInstanceDetails` is not a confirmed operation in this repo and is not used. After `bucketCreate`, apply retries the confirmed `bucketS3Credentials` query until a BucketInstance exists.
