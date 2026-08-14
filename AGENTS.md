# Agent notes

This repository is a public Aspire 13.4 / `net10.0` hosting integration for Railway.

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

NuGet IDs use the `IntrepidDeveloper.` prefix. C# AppHost extensions live in `Aspire.Hosting`. Resource types live in `Aspire.Hosting.Railway` / `.PostgreSQL` / `.Redis` / `.Storage`.

## Hard rules

- Core must not reference `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.Redis`, or the Storage hosting package. Satellites implement `IRailwayManagedServiceAnnotation`.
- Never commit secrets, tokens, `.env` files, user-secrets, NuGet API keys, or real Railway project/environment IDs.
- Do not call live Railway from unit tests. Do not fake GraphQL success.
- Do not add Solo Buddy / Maldric / Helian content. Playground names: `api`, `postgres`, `redis`, `uploads`.
- Use Aspire 13.4 pipeline types (`PipelineStepAnnotation`, `WellKnownPipelineSteps`). Do not use obsolete `IDistributedApplicationPublisher` / `DeployingCallbackAnnotation`.
- Suppress `ASPIREPIPELINES001` / `ASPIRECOMPUTE002` inside the library only.

## Pipeline

Per environment: `prepare-deployment-targets-{name}`, `publish-{name}`, `deploy-{name}`, `destroy-{name}`.

Publish writes `railway-plan.json` (expressions and parameter names only). Deploy currently reports that GraphQL apply is not implemented. Later PRs should fill in the typed client in `GraphQL/` without changing the public AppHost surface.

Confirmed Railway operations: `projectCreate`, `environmentCreate`, `serviceCreate` (always pass `environmentId`), `serviceInstanceUpdate`, `serviceInstanceDeployV2`, `variableCollectionUpsert`, `serviceDomainCreate`, `template` + `templateDeployV2`, `workflowStatus`, `bucketCreate`, `bucketS3Credentials`, `environmentPatchCommitStaged`, `regions`.

Railway has no image registry. Image-based deploy must fail clearly unless the model has `IContainerRegistry` (GHCR / Docker Hub). Do not shell out to `railway up`.
