# Publish and deploy

This integration uses Aspire 13.5 compute-environment + pipeline hooks (`IComputeEnvironmentResource`, `PipelineStepAnnotation`, `WellKnownPipelineSteps`). It does **not** use the obsolete `IDistributedApplicationPublisher` / `DeployingCallbackAnnotation` model.

## Pipeline steps

Per Railway environment resource named `{name}`:

| Step | What it does |
| --- | --- |
| `prepare-deployment-targets-{name}` | Materializes `RailwayServiceResource` children and `DeploymentTargetAnnotation`. Depends on `ValidateComputeEnvironments`; required by `BeforeStart`. |
| `publish-{name}` | Writes `railway-plan.json` plus a `.env.example` of captured parameter names. Required by the well-known `Publish` step. Parameter names and Railway expressions stay secret-safe; `WithEnvironment` string literals are written as-is. |
| `deploy-{name}` | Resolves the account/workspace token, applies the plan over GraphQL, persists ids, reports real progress or failures. Depends on `DeployPrereq` and `publish-{name}`. Image push steps run before this when the model has build-and-push resources. |
| `destroy-{name}` | Tears down Railway resources this integration created for the mapped environment. Prints an inventory, skips adopted resources and buckets, never calls `projectDelete` in v1. This is **not** deployment overlap/drain (`OverlapSeconds` / `DrainingSeconds` on `PublishAsRailwayService`). |

A `validate-railway` step (registered once) fails publish-mode apps that call `PublishAsRailway*` / `AddRailwayBucket` without `AddRailwayEnvironment`.

## Plan vs apply

| | Publish (`RailwayPlanBuilder`) | Deploy (`RailwayGraphQLApplyService`) |
| --- | --- | --- |
| Network | None | Railway GraphQL v2 |
| Secrets | Stay out of the plan only when modeled as Aspire parameters (`AddParameter(secret: true)`). `WithEnvironment("API_KEY", value)` literals are written as-is | Token and resolved parameter values in memory only |
| Output | `railway-plan.json`, `.env.example` | Created or adopted Railway ids |

The plan is not unconditionally secret-safe. `WithEnvironment("API_KEY", value)` literals land in `railway-plan.json`. Use `AddParameter(secret: true)` for secrets (stored as names). Railway `${{service.VAR}}` expressions, the deploy token, and bucket credentials stay out. An empty optional captured parameter is omitted; a missing required parameter still fails.

`WithReference` on official Railway databases emits expressions such as `${{postgres.DATABASE_URL}}` (private) onto services that actually referenced the database — never the local Docker connection string. Non-Railway connection strings are captured as secret parameter **names** in the plan and resolved on deploy.

Host addresses are host-only: `{service}.railway.internal` (lowercase). Endpoints and secrets are never concatenated into strings before Aspire resolves them.

Official DBs are created via `template(code: "postgres"|"redis")` then `templateDeployV2` with the fetched `templateId` and `serializedConfig` (never empty, never invented template UUIDs). Apply polls `workflowStatus` and fails if `workflowId` is missing.

## Adopt existing

```csharp
builder.AddRailwayEnvironment("railway").AsExisting();
// or pass parameters bound from RAILWAY_PROJECT_ID / RAILWAY_ENVIRONMENT_ID
```

`AsExisting()` binds `railway-project-id` / `railway-environment-id` from `RAILWAY_PROJECT_ID` / `RAILWAY_ENVIRONMENT_ID`. Both ids are required when adopt is set.

On adopt, and on later applies against an existing project id, apply lists project services and buckets by name (case-insensitive: `Postgres` / `postgres`, `api`). Matching services skip template deploy and service create; apply continues with instance update, variable upsert, and deploy.

Planned buckets match `project.buckets` by display name. A match records the bucket id and skips create. A same-name service is not a bucket. After a real create, apply retries credentials until a `BucketInstance` exists. Local state stores bucket **ids** (not S3 secrets); CI without that file adopts by name.

Re-deploy does not create a second project.

## Staging

If a `staging` environment does not exist on deploy, the default is to duplicate production when production exists. Empty create is opt-in:

```csharp
builder.AddRailwayEnvironment("railway")
    .WithProperties(env => env.CreateEmptyEnvironment = true);
```

`DuplicateProductionWhenCreatingStaging` defaults to `true` on `RailwayEnvironmentResource`. Creating staging without a known production environment id fails unless you deploy production first or opt into `CreateEmptyEnvironment`.

`railway-environment-id` is the **target** environment, not a production source for duplication. Passing the production id on a staging deploy applies that deploy onto production. Adopt with `railway-environment-id` only when that environment already exists.

PR / ephemeral Railway environment APIs are not part of this release.

## Destroy

`aspire destroy` (and `aspire destroy --environment Staging`) runs `destroy-{name}`. Aspire prompts first; `--yes` / `--non-interactive --yes` skip the prompt. Before GraphQL, destroy prints the project, environment, service names, bucket names, and custom-domain hostnames from deployment state plus a live `project(id)` read.

What is deleted (confirmed mutations only, in this order):

1. Railway-provided and custom domains **this integration created**
2. App services we created (`serviceDelete`, always with `environmentId` when known)
3. Official Postgres / Redis template services we created
4. The Railway environment **only if we created it** (`environmentCreate` — typically `staging`)

What is skipped, with a printed reason:

- **Adopted** project / environment / service / domain / bucket (`AsExisting()`, `railway-project-id` / `railway-environment-id`, or a live name match on a project we did not create). Adopted is someone else's production.
- **`serviceDelete` when another Railway environment remains.** The live schema deletes a non-fork service in every non-fork environment. Staging-only destroy therefore does not call `serviceDelete` (that would wipe production). It deletes the staging environment when we created it.
- **Buckets.** Public GraphQL has no `bucketDelete` (only `bucketCreate` / `bucketUpdate` / `bucketCredentialsReset`). Destroy does not call `bucketCredentialsReset` as a fake delete and does not treat the bucket as gone.
- **The Railway project.** v1 never calls `projectDelete`. Blast radius is the mapped environment, not Azure-style "delete the resource group."
- **Volumes / backups.** This slice does not call `volumeDelete` or `volumeInstanceBackupDelete`. Cascade from `serviceDelete` is not proven.

Empty deployment state with no `railway-project-id` fails closed. After a successful destroy, flatten-safe ids for that Railway environment are cleared. `ProjectId` stays so a later deploy adopts the leftover project instead of creating a second one.

`OverlapSeconds` / `DrainingSeconds` stay in-deploy cutover. They are not `aspire destroy`.

## Flatten-safe deployment state

Project, environment, service, bucket, custom-domain, volume-instance, volume-backup-schedule, and template ids are persisted in `IDeploymentStateManager` under `Railway:{computeEnvironmentName}`. Aspire's file state manager flattens with colon keys and does **not** round-trip JSON arrays. This integration therefore stores maps as JSON objects (for example template codes as `{ "postgres": "postgres" }`, custom domains as `{ "api.example.com": "cdom_placeholder" }`, volume instances as `{ "postgres": "volinst_placeholder" }`), not arrays.

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

## Compute settings

Replica count is Aspire-core `WithReplicas` on a project. Implicit compute on `AddRailwayEnvironment` is enough; you do not need `PublishAsRailwayService` just to set replicas.

Healthcheck path is Aspire-core `WithHttpHealthCheck("/health")`. Railway always probes until HTTP 200, so a non-200 Aspire `statusCode` is ignored. Custom `WithHealthCheck` keys that are not HTTP probes are not mapped.

Railway-specific settings use `PublishAsRailwayService`. Aspire.Hosting 13.5.1 has no `WithCpu` / `WithMemory` / healthcheck-timeout / restart-policy / start-command / overlap / drain / cron / custom-domain annotation. `WithArgs` is not mapped. Custom hostnames need `WithExternalHttpEndpoints()`.

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
        s.RestartPolicy = RailwayRestartPolicy.OnFailure; // optional; omit = Railway default On Failure
        s.RestartPolicyMaxRetries = 10; // optional; omit = Railway default 10
        s.StartCommand = "/bin/sh -c \"exec dotnet MyApp.dll --urls http://*:$PORT\""; // optional; omit = image ENTRYPOINT/CMD
        s.PreDeployCommand = "dotnet MyApp.dll --migrate"; // optional; omit = no pre-deploy step
        s.OverlapSeconds = 60; // optional; omit = do not send (in-deploy cutover, not aspire destroy)
        s.DrainingSeconds = 10; // optional; omit = do not send (0 = immediate kill)
        s.CustomDomains.Add("api.example.com"); // requires WithExternalHttpEndpoints(); hostnames only
    });

builder.AddProject<Projects.Worker>("nightly")
    .PublishAsRailwayService(s =>
    {
        s.CronSchedule = "0 3 * * *"; // 03:00 UTC; omit = always-on
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

`RailwayRegion` is the closed AppHost type ([Railway regions](https://docs.railway.com/deployments/regions)): `UsWest2` → `us-west2`, `UsEast4` → `us-east4-eqdc4a`, `EuropeWest4` → `europe-west4-drams3a`, `AsiaSoutheast1` → `asia-southeast1-eqsg3a`. Airport codes (`sjc`, `iad`, `ams`, `sin`) and older ids (`us-west1`, `us-east4`, `europe-west4`) are not members. Total replicas must be at least 1 and at most 50.

How apply maps plan fields onto GraphQL (`environmentId` is always passed):

| Plan fields | GraphQL input |
| --- | --- |
| `replicaRegions` set | `serviceInstanceUpdate.multiRegionConfig` (region id → `{ numReplicas }`). `numReplicas` is not sent. This map wins over `WithReplicas` + `region`. |
| `region` set (no map) | `serviceInstanceUpdate.multiRegionConfig` `{ [region]: { numReplicas: replicas ?? 1 } }` |
| `replicas` only (`WithReplicas`, no region / no map) | `serviceInstanceUpdate.numReplicas` — official single-region path; applies to the service's current Railway region |
| `serverless` set | `serviceInstanceUpdate.sleepApplication` (only when the user set it; there is no GraphQL field named `serverless`; applies to all replicas) |
| `cpu` and/or `memoryGb` set | `serviceInstanceLimitsUpdate` with `vCPUs` / `memoryGB` (floats). Always `serviceId` + `environmentId`. Unset fields are omitted. After `serviceInstanceUpdate`. |
| `healthcheckPath` set | `serviceInstanceUpdate.healthcheckPath` (String). From `WithHttpHealthCheck`. Omitted when unset. |
| `healthcheckTimeout` set | `serviceInstanceUpdate.healthcheckTimeout` (Int seconds). From `HealthcheckTimeoutSeconds`. Must be greater than 0. Omitted when unset (Railway default 300). |
| `restartPolicyType` set | `serviceInstanceUpdate.restartPolicyType` (`ON_FAILURE` / `ALWAYS` / `NEVER`). From `RailwayRestartPolicy`. Omitted when unset (Railway default On Failure). |
| `restartPolicyMaxRetries` set | `serviceInstanceUpdate.restartPolicyMaxRetries` (Int). From `RestartPolicyMaxRetries`. Must be greater than 0. Either field can be set alone. |
| `startCommand` set | `serviceInstanceUpdate.startCommand` (String). From `StartCommand`. Empty or whitespace fails. Omitted when unset so the image ENTRYPOINT/CMD applies. |
| `preDeployCommand` set | `serviceInstanceUpdate.preDeployCommand` (`[String!]`). From `PreDeployCommand` as a one-element array. Empty or whitespace fails. Empty array is omitted. |
| `overlapSeconds` set | `serviceInstanceUpdate.overlapSeconds` (Int). From `OverlapSeconds`. Must be ≥ 0 (0 is no wait). In-deploy cutover, not `aspire destroy`. |
| `drainingSeconds` set | `serviceInstanceUpdate.drainingSeconds` (Int). From `DrainingSeconds`. Must be ≥ 0 (0 is immediate kill). Either field can be set alone. |
| `cronSchedule` set | `serviceInstanceUpdate.cronSchedule` (String). From `CronSchedule`. Five-field crontab, UTC, minimum every 5 minutes. Omitted when unset (always-on). |
| `customDomains` set | After the service id exists and `serviceDomainCreate` (when `WithExternalHttpEndpoints()`): list, adopt existing hostnames (case-insensitive), else create. Optional `targetPort` from the Aspire HTTP endpoint. Hostnames only in the plan — no verification tokens. |
| `targetPort` set | Optional Int on `serviceDomainCreate` and `customDomainCreate`. From the Aspire HTTP endpoint. Omitted when unset. |
| none of the above | image-only `source.image` update |

Never send `numReplicas` and `multiRegionConfig` on the same update. `Cpu` / `MemoryGb` must be greater than 0 when set. Railway plan caps are not hardcoded; if Railway rejects an over-plan value, deploy surfaces the error.

Gotchas:

- Healthcheck is a cutover probe to HTTP 200, not monitoring. Allow `healthcheck.railway.app` if the app filters Host. Listen on `PORT`. Volume-backed services still have a cutover gap. See [healthchecks](https://docs.railway.com/deployments/healthchecks).
- Restart unset = Railway default On Failure / 10 retries. See [restart policy](https://docs.railway.com/deployments/restart-policy).
- Start is exec form; wrap `$PORT` as `/bin/sh -c "exec … $PORT"`. Pre-deploy is a separate container, no volume; a non-zero exit stops the deploy. `WithArgs` is not mapped. See [start command](https://docs.railway.com/guides/start-command) and [pre-deploy command](https://docs.railway.com/deployments/pre-deploy-command).
- Overlap/drain is in-deploy cutover, not `aspire destroy`. See [deployment teardown](https://docs.railway.com/guides/deployment-teardown).
- Cron: five-field UTC, 5-minute floor, service must exit. No replicas greater than 1 or `Serverless`. See [cron jobs](https://docs.railway.com/cron-jobs).
- Custom domains need `WithExternalHttpEndpoints()`. Deploy prints DNS + TXT. Missing TXT is 404 even if CNAME resolves. This integration does not talk to your DNS provider. See [working with domains](https://docs.railway.com/networking/domains/working-with-domains).
- Postgres / Redis / buckets do not get service knobs. Replicas cannot be used with [volumes](https://docs.railway.com/volumes/reference).

Confirmed operation names and omit/`null` rules live in [GraphQL](graphql.md).

## Official Postgres volume backup schedules

AppHosts request Railway volume backup schedules on official Postgres only:

```csharp
builder.AddPostgres("postgres")
    .PublishAsRailwayPostgres(s =>
    {
        s.VolumeBackupDaily = true;
        s.VolumeBackupWeekly = true;
        // s.VolumeBackupMonthly = true;
    });
```

Booleans match the Railway dashboard kinds. Unset / false omits that kind. All false / no callback omits the field so deploy leaves the dashboard as-is. At least one true kind is required to send the update. Empty or invalid deserialized kind strings fail honestly.

`PublishAsRailwayRedis` is unchanged and does not silently enable backups. Buckets and app services do not get this field.

Deploy finds the volume for the official Postgres service, lists existing schedules, and **unions** requested kinds with already-present kinds so a dashboard schedule this plan did not mention is not removed. If requested kinds are already a subset, the update is skipped. Deploy does not wait for a backup to complete.

Product retention is mapping only (do not hardcode as API): Daily (24h, keep 6 days), Weekly (7d, keep 1 month), Monthly (30d, keep 3 months). Multiple kinds are allowed. Wiping a volume deletes its backups. See [volume backups](https://docs.railway.com/volumes/backups).

PITR enable is HA-only and is not in this slice. Flatten-safe volume instance and schedule **ids** are persisted. Backup payloads stay out of plan and state.
