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
| `serviceInstanceUpdate` | Updates a service instance. This integration sets confirmed `ServiceInstanceUpdateInput` fields: `source.image`, `multiRegionConfig` (JSON map of region id → `{ numReplicas }`), `sleepApplication` (Railway serverless; official `railway.json` `deploy.sleepApplication`), and single-region fallback `numReplicas` when only `WithReplicas` is set. Legacy `region` is on the input type but unused when `multiRegionConfig` is sent. See [regions](https://docs.railway.com/deployments/regions), [multi-region configuration](https://docs.railway.com/reference/config-as-code#multi-region-configuration), and [scale](https://docs.railway.com/cli/scale). |
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

`serviceInstanceUpdate` is the only mutation that sets compute scale/region/serverless. `serviceCreate` does not take those fields. `multiRegionConfig` is a JSON scalar (`{ "us-west2": { "numReplicas": 2 } }`). `sleepApplication` is the confirmed serverless field. `numReplicas` is present but deprecated for scaling; the official autoscale guide still uses it for single-region only. Do not invent other scale mutations or fields.

`project.buckets` is a field on the documented `project(id)` query (Relay `edges { node { id name } }`, same shape as `services`). It was verified on Railway's GraphQL schema (`Project.buckets: ProjectBucketsConnection` of `Bucket { id name }`). There is no separate confirmed buckets-list query; apply does not invent one. `bucketInstanceDetails` is not a confirmed operation in this repo and is not used. After `bucketCreate`, apply retries the confirmed `bucketS3Credentials` query until a BucketInstance exists.
