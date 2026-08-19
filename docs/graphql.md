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
| `project` | Documented `project(id)` query. Lists `services` and `environments` so apply can adopt by name. |
| `projectCreate` | Creates a Railway project. Requires an account or workspace token. |
| `environmentCreate` | Creates an environment. Pass `sourceEnvironmentId` to duplicate production. `ephemeral` is reserved for later. |
| `serviceCreate` | Creates a service. Always pass `environmentId`. |
| `serviceInstanceUpdate` | Updates a service instance (image and similar settings). |
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
