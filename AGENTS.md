# Agent notes

This repository is a public Aspire 13.5 / `net10.0` hosting integration for Railway. Humans read [README.md](README.md) and [docs/](docs/). This file is the contract for coding agents.

## Layout

| Path | Role |
| --- | --- |
| `src/Aspire.Hosting.Railway` | Compute environment, pipeline, GraphQL client, `PublishAsRailwayService` |
| `src/Aspire.Hosting.Railway.PostgreSQL` | `PublishAsRailwayPostgres` |
| `src/Aspire.Hosting.Railway.Redis` | `PublishAsRailwayRedis` |
| `src/Aspire.Hosting.Railway.Storage` | `AddRailwayBucket` |
| `src/Aspire.Railway.Storage` | `AddRailwayBucketClient` → `IAmazonS3` |
| `tests/Aspire.Hosting.Railway.Tests` | Unit tests (no live Railway, no token required) |
| `samples/Playground.AppHost` | Compiling AppHost using the public APIs |
| `docs/` | Human + agent docs (getting started, publish/deploy, storage, GraphQL) |
| `CHANGELOG.md` | Preview.11 and later. Do not send readers to closed issues. |

NuGet IDs use the `IntrepidDeveloper.` prefix. C# AppHost extensions live in `Aspire.Hosting`. Resource types live in `Aspire.Hosting.Railway` / `.PostgreSQL` / `.Redis` / `.Storage`.

## Hard rules

- Aspire 13.5 compute-environment + pipeline hooks only (`IComputeEnvironmentResource`, `PipelineStepAnnotation`, `WellKnownPipelineSteps`). Do not use the obsolete publisher-callback model (`IDistributedApplicationPublisher`, `DeployingCallbackAnnotation`).
- Official resource types: `AddPostgres` / `AddRedis` + `PublishAsRailway*`. Buckets: `AddRailwayBucket` + `AddRailwayBucketClient` (`IAmazonS3`). Do not invent public AppHost APIs.
- Core must not reference `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.Redis`, or the Storage hosting package. Satellites implement `IRailwayManagedServiceAnnotation`.
- GraphQL only, confirmed operations only. Never invent mutation or query names. Never `pluginCreate`. Endpoint: `https://backboard.railway.com/graphql/v2`. See [docs/graphql.md](docs/graphql.md).
- Railway has no image registry. Image-based deploy must fail clearly unless the model has `IContainerRegistry` (GHCR / Docker Hub). Do not shell out to `railway up`.
- Public repo: never commit secrets, tokens, `.env` files, user-secrets, NuGet API keys, or real Railway project/environment IDs. Placeholders only.
- Do not add names or examples from other products; playground resources stay `api` / `postgres` / `redis` / `uploads`.
- Unit tests stay offline. Do not call live Railway. Do not fake GraphQL success.
- Suppress `ASPIREPIPELINES001` / `ASPIRECOMPUTE002` inside the library only.

## Pipeline

Per environment: `prepare-deployment-targets-{name}`, `publish-{name}`, `deploy-{name}`, `destroy-{name}`.

Publish writes `railway-plan.json`. Parameter names and Railway expressions stay secret-safe; `WithEnvironment` string literals are written as-is. Deploy calls `RailwayGraphQLApplyService` with the typed client in `GraphQL/` (confirmed operations only). Do not change the public AppHost surface when extending apply.

Confirmed Railway operations: `project` (documented `project(id)` query; lists `services`, `environments`, and `buckets`), `projectCreate`, `environmentCreate`, `serviceCreate` (always pass `environmentId`), `serviceInstanceUpdate` (always pass `environmentId`; input: `source.image`, `multiRegionConfig`, `sleepApplication`, `numReplicas` when only `WithReplicas` is set — never both `numReplicas` and `multiRegionConfig`; `region` on the input type). There is no GraphQL field named `serverless`. `ServiceInstance` has no `multiRegionConfig` read field. Official deploy region ids only (`Region.region`); reject airport codes and older ids. Cap total replicas at 50. Do not send `numReplicas` / `multiRegionConfig` for volume-backed `PublishAsRailwayPostgres` / `PublishAsRailwayRedis`. `serviceInstanceDeployV2`, `variableCollectionUpsert`, `serviceDomainCreate`, `template` + `templateDeployV2`, `workflowStatus`, `bucketCreate`, `bucketS3Credentials` (`projectId` required; select `bucketName`), `environmentPatchCommitStaged`, `regions`. Adopt planned buckets by name from `project.buckets`; never pass a service id to `bucketS3Credentials`. After `bucketCreate`, retry credentials until a BucketInstance exists. Persist flatten-safe bucket **ids** only — not S3 secrets.

`IDeploymentStateManager` ids must be flatten-safe objects (Aspire's file state manager does not round-trip JSON arrays). Tokens and bucket secrets stay out of plan files and state.
