# Publish and deploy

This integration uses Aspire 13.5 compute-environment + pipeline hooks (`IComputeEnvironmentResource`, `PipelineStepAnnotation`, `WellKnownPipelineSteps`). It does **not** use the obsolete `IDistributedApplicationPublisher` / `DeployingCallbackAnnotation` model.

## Pipeline steps

Per Railway environment resource named `{name}`:

| Step | What it does |
| --- | --- |
| `prepare-deployment-targets-{name}` | Materializes `RailwayServiceResource` children and `DeploymentTargetAnnotation`. Depends on `ValidateComputeEnvironments`; required by `BeforeStart`. |
| `publish-{name}` | Writes `railway-plan.json` plus a `.env.example` of captured parameter names. Required by the well-known `Publish` step. Parameter names and Railway expressions stay secret-safe; `WithEnvironment` string literals are written as-is. |
| `deploy-{name}` | Resolves the account/workspace token, applies the plan over GraphQL, persists ids, reports real progress or failures. Depends on `DeployPrereq` and `publish-{name}`. Image push steps run before this when the model has build-and-push resources. |
| `destroy-{name}` | Stub. Warns that teardown is not implemented. Confirmed operations do not include project or environment delete. |

A `validate-railway` step (registered once) fails publish-mode apps that call `PublishAsRailway*` / `AddRailwayBucket` without `AddRailwayEnvironment`.

## Plan vs apply

| | Publish (`RailwayPlanBuilder`) | Deploy (`RailwayGraphQLApplyService`) |
| --- | --- | --- |
| Network | None | Railway GraphQL v2 |
| Secrets | Stay out of the plan only when modeled as Aspire parameters (`AddParameter(secret: true)`). `WithEnvironment("API_KEY", value)` literals are written as-is | Token and resolved parameter values in memory only |
| Output | `railway-plan.json`, `.env.example` | Created or adopted Railway ids |

The plan is not unconditionally secret-safe. `CaptureEnvironmentValue` writes string literals from `WithEnvironment("API_KEY", value)` into `railway-plan.json` as-is. Secrets stay out of the plan only when they are Aspire parameters (`AddParameter(secret: true)`), which are stored as parameter **names**. Railway `${{service.VAR}}` expressions, the deploy token, and bucket credentials from `bucketS3Credentials` are not written to the plan. Deploy fills `RailwayApplyRequest.ResolvedServiceEnvironment` from Aspire parameters and connection strings, then upserts variables. An empty optional captured parameter is omitted instead of aborting deploy. A missing required parameter still fails.

`WithReference` on official Railway databases emits expressions such as `${{postgres.DATABASE_URL}}` (private) onto services that actually referenced the database — never the local Docker connection string. Non-Railway connection strings (for example another Aspire connection-string resource) are captured as secret parameter **names** in the plan and resolved on deploy.

Host addresses are host-only: `{service}.railway.internal` (lowercase). Endpoints and secrets are never concatenated into strings before Aspire resolves them.

Official DBs are created via `template(code: "postgres"|"redis")` then `templateDeployV2` with the fetched `templateId` and `serializedConfig` (never empty, never invented template UUIDs). Apply polls `workflowStatus` and fails if `workflowId` is missing.

## Adopt existing

```csharp
builder.AddRailwayEnvironment("railway").AsExisting();
// or pass parameters bound from RAILWAY_PROJECT_ID / RAILWAY_ENVIRONMENT_ID
```

`AsExisting()` binds `railway-project-id` / `railway-environment-id` from `RAILWAY_PROJECT_ID` / `RAILWAY_ENVIRONMENT_ID`. Both ids are required when adopt is set.

On adopt, and on later applies against an existing project id, apply lists `project.services` and `project.buckets` (the documented `project(id)` query — `buckets` is a field on that same confirmed operation, not a new query). Service names match case-insensitively (`Postgres` / `postgres`, `api`). Matching services skip `templateDeployV2` and `serviceCreate`; apply continues with `serviceInstanceUpdate`, variable upsert, and deploy.

Planned `Kind = bucket` resources match `project.buckets` by display name (case-insensitive). A match records the bucket id in `BucketIds` and skips `bucketCreate`. `bucketCreate` runs only when no matching bucket exists. A same-name service is not a bucket id and is never passed to `bucketS3Credentials`. After a real `bucketCreate`, apply retries `bucketS3Credentials` until a `BucketInstance` exists. Flatten-safe local state still stores bucket **ids** (not S3 secrets) so a local retry can skip create; CI / a new machine without that file relies on name-based adopt.

Re-deploy does not create a second project.

## Staging

If a `staging` environment does not exist on deploy, the default is to duplicate production (`environmentCreate` with `sourceEnvironmentId`) when production exists. Empty create is opt-in:

```csharp
builder.AddRailwayEnvironment("railway")
    .WithProperties(env => env.CreateEmptyEnvironment = true);
```

`DuplicateProductionWhenCreatingStaging` defaults to `true` on `RailwayEnvironmentResource`. Creating staging without a known production environment id fails unless you deploy production first or opt into `CreateEmptyEnvironment`.

`EnsureEnvironmentAsync` treats `AdoptedEnvironmentId` (`railway-environment-id`) as the **target** environment, not a production source for duplication. Passing the production id on a staging deploy applies that deploy onto production. Adopt with `railway-environment-id` only when that environment already exists.

PR / ephemeral Railway environment APIs are not part of this release.

## Flatten-safe deployment state

Project, environment, service, bucket, and template ids are persisted in `IDeploymentStateManager` under `Railway:{computeEnvironmentName}`. Aspire's file state manager flattens with colon keys and does **not** round-trip JSON arrays. This integration therefore stores maps as JSON objects (for example template codes as `{ "postgres": "postgres" }`), not arrays.

A legacy `AppliedTemplateCodes` key that stored a JSON array string such as `["postgres"]` is still read and migrated on load. Preview.4 never read that key. Tokens and bucket secrets are never written to state.

## Image resolution

Railway has **no image registry**. Deploy of image-based services requires `IContainerRegistry` on the model:

```csharp
var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io", "intrepid-developer/playground");
var railway = builder.AddRailwayEnvironment("railway")
    .WithContainerRegistry(ghcr);
```

Pass the GHCR namespace as the third argument (`<owner>/<repository>`). The two-argument form has no owner/repo, so Aspire would push `ghcr.io/api` and GHCR rejects it.

If the registry is missing, deploy throws and tells you to add GHCR or Docker Hub. This integration does not shell out to `railway up`. Railpack has no .NET support; use an image or a Dockerfile.

Resolution order (`RailwayEnvironmentResource.ResolveDeployImageAsync`):

1. `ContainerImagePushOptions` + `IContainerRegistry` — `GetFullRemoteImageNameAsync` after push-option callbacks. This is what Aspire project resources need: they have no `ContainerImageAnnotation`, so the plan keeps a `{name.containerImage}` placeholder.
2. `TryGetContainerImageName` when that already looks like a real image (not a `{…}` placeholder).
3. The plan image, if it is already resolved.

`resolveContainerRegistry` uses `WithContainerRegistry` when present, otherwise the single `IContainerRegistry` in the model.

## Compute settings (replicas, region, sleepApplication, CPU/RAM, healthcheck)

Replica count is Aspire-core. Call `WithReplicas` on a project (`ProjectResource`). Publish copies `resource.GetReplicaCount()` into `railway-plan.json` when a `ReplicaAnnotation` is present. Implicit compute on `AddRailwayEnvironment` is enough; you do not need `PublishAsRailwayService` just to set replicas.

Deploy healthcheck path is also Aspire-core. Call `WithHttpHealthCheck("/health")`. Publish copies that HTTP path into `railway-plan.json`. Aspire stores the path in `HealthCheckAnnotation.Key` (`{resource}_{endpoint}_{path}_{statusCode}_check`); there is no separate path annotation in Aspire.Hosting 13.5.0. Railway always probes until HTTP 200 ([healthchecks](https://docs.railway.com/deployments/healthchecks)), so a non-200 Aspire `statusCode` is ignored. Custom `WithHealthCheck` keys that are not HTTP probes are not mapped. Implicit compute is enough; you do not need `PublishAsRailwayService` just to set the path.

Railway-specific settings use `PublishAsRailwayService`. Aspire.Hosting 13.5.0 has no `WithCpu` / `WithMemory` / healthcheck-timeout annotation.

```csharp
builder.AddProject<Projects.Api>("api")
    .WithReplicas(2)
    .WithHttpHealthCheck("/health")
    .WithComputeEnvironment(railway)
    .PublishAsRailwayService(s =>
    {
        s.Region = RailwayRegion.EuropeWest4;
        s.Cpu = 1;
        s.MemoryGb = 2;
        s.Serverless = true;
        s.HealthcheckTimeoutSeconds = 120; // optional; omit = do not send (Railway default 300)
    });

// Aspire has no core multi-region API
builder.AddProject<Projects.Api>("api")
    .WithComputeEnvironment(railway)
    .PublishAsRailwayService(s =>
    {
        s.ReplicaRegions = new()
        {
            [RailwayRegion.UsWest2] = 2,
            [RailwayRegion.EuropeWest4] = 1
        };
    });
```

`RailwayRegion` is the closed AppHost type ([Railway regions](https://docs.railway.com/deployments/regions), `Region.region`): `UsWest2` → `us-west2`, `UsEast4` → `us-east4-eqdc4a`, `EuropeWest4` → `europe-west4-drams3a`, `AsiaSoutheast1` → `asia-southeast1-eqsg3a`. Airport codes from `Query.regions.id` (`sjc`, `iad`, `ams`, `sin`) and older ids (`us-west1`, `us-east4`, `europe-west4`) are not members. Plan JSON still stores those official strings; unknown deserialized ids fail before GraphQL. Total replicas must be at least 1 and at most 50 ([scale](https://docs.railway.com/cli/scale), [scaling](https://docs.railway.com/deployments/scaling)) — not the 200 in `railway.schema.json`.

How apply maps plan fields onto GraphQL (`environmentId` is always passed):

| Plan fields | GraphQL input |
| --- | --- |
| `replicaRegions` set | `serviceInstanceUpdate.multiRegionConfig` (region id → `{ numReplicas }`). `numReplicas` is not sent. This map wins over `WithReplicas` + `region`. |
| `region` set (no map) | `serviceInstanceUpdate.multiRegionConfig` `{ [region]: { numReplicas: replicas ?? 1 } }` |
| `replicas` only (`WithReplicas`, no region / no map) | `serviceInstanceUpdate.numReplicas` — official single-region path ([autoscale](https://docs.railway.com/guides/autoscale-horizontally)); applies to the service's current Railway region |
| `serverless` set | `serviceInstanceUpdate.sleepApplication` (only when the user set it; there is no GraphQL field named `serverless`; applies to all replicas) |
| `cpu` and/or `memoryGb` set | `serviceInstanceLimitsUpdate` with `vCPUs` / `memoryGB` (floats). Always `serviceId` + `environmentId`. Unset fields are omitted. Not sent when both are absent. After `serviceInstanceUpdate`. |
| `healthcheckPath` set | `serviceInstanceUpdate.healthcheckPath` (String). From `WithHttpHealthCheck`. Omitted when unset. Do not send `null`. |
| `healthcheckTimeout` set | `serviceInstanceUpdate.healthcheckTimeout` (Int seconds). From `HealthcheckTimeoutSeconds`. Must be greater than 0. Omitted when unset (Railway default 300). Do not send `null`. |
| none of the above | today's image-only `source.image` update |

Config-as-code equivalent for CPU/RAM (mapping only, not the apply path): [`deploy.limitOverride.containers`](https://railway.com/railway.schema.json) with `cpu` and `memoryBytes`. GraphQL uses vCPU and GB floats, not bytes.

Never send `numReplicas` and `multiRegionConfig` on the same update. `serviceCreate` does not take these fields. Apply sends scale/region/sleep/healthcheck on `serviceInstanceUpdate` after the service id exists (create and later updates), then `serviceInstanceLimitsUpdate` when CPU and/or RAM were requested. Do not add `vCPUs` / `memoryGB` onto `ServiceInstanceUpdateInput`. Do not add healthcheck fields onto any other input. If the plan has region or a multi-region map, that update always includes `multiRegionConfig` so a later image-only update does not reset dashboard scale/region. `ServiceInstance` has no `multiRegionConfig` read field; apply does not invent a read-back query. Related read queries (`serviceInstanceLimits`, `serviceInstanceLimitOverride`) exist but are not used.

`Cpu` / `MemoryGb` must be greater than 0 when set. Dashboard plan caps (for example 24 vCPU) are plan-specific and are not hardcoded; if Railway rejects an over-plan value, deploy surfaces the GraphQL error.

Railway healthchecks are a deploy cutover probe, not continuous monitoring ([healthchecks](https://docs.railway.com/deployments/healthchecks)). The probe uses the service `PORT`. Origin host is `healthcheck.railway.app` — allow-list it if the app filters Host. Config-as-code mapping only: [`deploy.healthcheckPath` / `deploy.healthcheckTimeout`](https://docs.railway.com/reference/config-as-code) in [railway.schema.json](https://railway.com/railway.schema.json). GraphQL uses the same field names on `ServiceInstanceUpdateInput`. Do not use `environmentPatchCommit` / staged patches for this.

Replicas and CPU/RAM limits cannot be used with [volumes](https://docs.railway.com/volumes/reference). `PublishAsRailwayPostgres` / `PublishAsRailwayRedis` are volume-backed templates: publish fails honestly if `WithReplicas` or `PublishAsRailwayService` scale/region/cpu/memory is set on them. Apply never sends `numReplicas` / `multiRegionConfig` / `serviceInstanceLimitsUpdate` / `healthcheckPath` / `healthcheckTimeout` for those services. Buckets stay on the existing create/credentials path and do not get CPU/RAM or healthcheck fields. Volume-backed services still have a cutover gap even when a healthcheck is configured.

