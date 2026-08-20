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

## Confirmed operations

Names only. Field lists, omit/`null` rules, and “do not invent” live in [docs/graphql.md](docs/graphql.md).

| Area | Operations |
| --- | --- |
| Project / env | `project`, `projectCreate`, `environmentCreate`, `environmentDelete`, `environment`, `environmentPatchCommitStaged`, `regions` |
| Services | `serviceCreate`, `serviceDelete`, `serviceInstanceUpdate`, `serviceInstanceLimitsUpdate`, `serviceInstanceDeployV2`, `variableCollectionUpsert`, `serviceDomainCreate`, `serviceDomainDelete` |
| Domains | `domains`, `customDomain`, `customDomainAvailable`, `customDomainCreate`, `customDomainUpdate`, `customDomainDelete` (destroy only) |
| Templates | `template`, `templateDeployV2`, `workflowStatus` |
| Buckets | `bucketCreate`, `bucketS3Credentials` |
| Volumes (Postgres) | `volumeInstanceBackupScheduleList`, `volumeInstanceBackupScheduleUpdate` |

Never invent mutation or query names. Never `pluginCreate`. Never `cronCreate` / `scheduleCreate`. Never `customDomainIssueCertificate`. Never `projectDelete` / `bucketDelete` / `volumeDelete` / `volumeInstanceBackupDelete` (v1 destroy). Never `volumeInstanceBackupCreate` / `Lock` / `Restore`, `volumeInstancePITRRestore`, `enablePitrForHaCluster` / `disablePitrForHaCluster`. Do not invent `WAL_ARCHIVE_*` or `bucketCreate` of `Postgres-PITR`. PITR enable is HA-only; #30 stays open. `customDomainDelete` is destroy-only for hostnames we created.

## Apply contract

- Always pass `environmentId` on `serviceCreate` / `serviceInstanceUpdate` / `serviceInstanceLimitsUpdate` / `customDomainUpdate`. Always pass `serviceId` + `environmentId` on limits. Always pass `projectId` on `bucketS3Credentials` (select `bucketName`).
- Never send `numReplicas` and `multiRegionConfig` together. `numReplicas` only when `WithReplicas` is set alone. No GraphQL field named `serverless` (`sleepApplication`). `ServiceInstance` has no `multiRegionConfig` read field. Do not invent a limits read-back. Do not add `vCPUs` / `memoryGB` onto `ServiceInstanceUpdateInput`.
- Official `Region.region` ids only via `RailwayRegion` (`UsWest2` / `UsEast4` / `EuropeWest4` / `AsiaSoutheast1`). Reject airport codes and older ids on deserialized plans. Cap total replicas at 50.
- Omit unset fields; do not send `null`. Do not send scale / limits / healthcheck / restart / start / pre-deploy / teardown / cron / custom domains for volume-backed `PublishAsRailwayPostgres` / `PublishAsRailwayRedis` or buckets.
- Adopt buckets by name from `project.buckets`. Never pass a service id to `bucketS3Credentials`. After `bucketCreate`, retry until a `BucketInstance` exists. Match `environment.volumeInstances` by Postgres `serviceId`; retry if not visible; fail if none matches. Do not use `adminVolumeInstancesForVolume` or `volumeInstance(id)` unless the id is known. Service has no `volumes` field.
- `IDeploymentStateManager` ids must be flatten-safe objects (Aspire's file state manager does not round-trip JSON arrays). Persist created-vs-adopted flags in that same store (`CreatedProject`, `CreatedEnvironments`, `CreatedServices`, `CreatedCustomDomains`, `CreatedServiceDomains`). Tokens, bucket secrets, custom-domain verification tokens, and backup payloads stay out of plan files and state. TCP proxies out of scope.
- Destroy (`destroy-{name}` → `RailwayGraphQLDestroyService`, not apply): skip adopted; skip buckets (no public `bucketDelete`); never `projectDelete` in v1; `serviceDelete` only when no other Railway environment remains (live schema is project-wide for non-forks); `environmentDelete` only if we created the environment. Fail closed if state is empty and no `railway-project-id`.

| AppHost | Apply notes |
| --- | --- |
| `WithHttpHealthCheck` | Path only. Timeout is `HealthcheckTimeoutSeconds`. Do not invent `RAILWAY_HEALTHCHECK_TIMEOUT_SEC`. |
| `RailwayRestartPolicy` / `RestartPolicyMaxRetries` | Retries > 0 when set. No hardcoded free/trial caps. |
| `StartCommand` / `PreDeployCommand` | One-element array for pre-deploy. Empty/whitespace fails. Empty array omitted. Do not map `WithArgs`. |
| `OverlapSeconds` / `DrainingSeconds` | ≥ 0 (0 = no wait / immediate kill). In-deploy cutover, not `aspire destroy`. Do not invent `RAILWAY_DEPLOYMENT_*`. Config-as-code is mapping only; GraphQL wants Int. |
| `CronSchedule` | Five-field UTC. 5-minute floor. Reject `* * * * *` and `*/1`–`*/4`. No timezone conversion. Fail with replicas > 1 or `Serverless`. Service must exit. |
| `CustomDomains` | Needs `WithExternalHttpEndpoints()`. Empty/whitespace/duplicates fail. No secret lowercasing. Adopt case-insensitive. Omit unset `targetPort`. Report DNS as returned. Token is on `CustomDomainStatus`, not `dnsRecords.verificationToken`. Persist domain **ids** only. |
| `VolumeBackupDaily` / `Weekly` / `Monthly` | `PublishAsRailwayPostgres` only. Plan `volumeBackupScheduleKinds`. At least one true kind to send. Union with dashboard; skip if already a subset. Persist volume instance / schedule **ids** only. |
