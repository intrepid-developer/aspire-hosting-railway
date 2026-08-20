# Publish and deploy

This integration uses Aspire 13.5 compute-environment + pipeline hooks (`IComputeEnvironmentResource`, `PipelineStepAnnotation`, `WellKnownPipelineSteps`). It does **not** use the obsolete `IDistributedApplicationPublisher` / `DeployingCallbackAnnotation` model.

## Pipeline steps

Per Railway environment resource named `{name}`:

| Step | What it does |
| --- | --- |
| `prepare-deployment-targets-{name}` | Materializes `RailwayServiceResource` children and `DeploymentTargetAnnotation`. Depends on `ValidateComputeEnvironments`; required by `BeforeStart`. |
| `publish-{name}` | Writes `railway-plan.json` plus a `.env.example` of captured parameter names. Required by the well-known `Publish` step. Parameter names and Railway expressions stay secret-safe; `WithEnvironment` string literals are written as-is. |
| `deploy-{name}` | Resolves the account/workspace token, applies the plan over GraphQL, persists ids, reports real progress or failures. Depends on `DeployPrereq` and `publish-{name}`. Image push steps run before this when the model has build-and-push resources. |
| `destroy-{name}` | Stub. Warns that project/environment teardown is not implemented. Confirmed operations do not include project or environment delete. This is **not** deployment overlap/drain (`OverlapSeconds` / `DrainingSeconds` on `PublishAsRailwayService`). |

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

## Compute settings (replicas, region, sleepApplication, CPU/RAM, healthcheck, restart policy, start / pre-deploy, overlap / drain, cron, custom hostnames)

Replica count is Aspire-core. Call `WithReplicas` on a project (`ProjectResource`). Publish copies `resource.GetReplicaCount()` into `railway-plan.json` when a `ReplicaAnnotation` is present. Implicit compute on `AddRailwayEnvironment` is enough; you do not need `PublishAsRailwayService` just to set replicas.

Deploy healthcheck path is also Aspire-core. Call `WithHttpHealthCheck("/health")`. Publish copies that HTTP path into `railway-plan.json`. Aspire stores the path in `HealthCheckAnnotation.Key` (`{resource}_{endpoint}_{path}_{statusCode}_check`); there is no separate path annotation in Aspire.Hosting 13.5.0. Railway always probes until HTTP 200 ([healthchecks](https://docs.railway.com/deployments/healthchecks)), so a non-200 Aspire `statusCode` is ignored. Custom `WithHealthCheck` keys that are not HTTP probes are not mapped. Implicit compute is enough; you do not need `PublishAsRailwayService` just to set the path.

Railway-specific settings use `PublishAsRailwayService`. Aspire.Hosting 13.5.0 has no `WithCpu` / `WithMemory` / healthcheck-timeout / restart-policy / start-command / overlap / drain / cron / custom-domain annotation. Aspire `WithArgs` is not mapped to Railway start. Custom hostnames require Aspire `WithExternalHttpEndpoints()` — the same public-HTTP signal as the Railway-provided `*.up.railway.app` service domain.

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
| `restartPolicyType` set | `serviceInstanceUpdate.restartPolicyType` (`RestartPolicyType`: `ON_FAILURE` / `ALWAYS` / `NEVER`). From `RailwayRestartPolicy`. Omitted when unset (Railway default On Failure). Do not send `null`. |
| `restartPolicyMaxRetries` set | `serviceInstanceUpdate.restartPolicyMaxRetries` (Int). From `RestartPolicyMaxRetries`. Must be greater than 0. Either field can be set alone. Omitted when unset (Railway default 10). Do not send `null`. |
| `startCommand` set | `serviceInstanceUpdate.startCommand` (String). From `StartCommand`. Empty or whitespace fails. Omitted when unset so the image ENTRYPOINT/CMD applies. Do not send `null`. |
| `preDeployCommand` set | `serviceInstanceUpdate.preDeployCommand` (`[String!]`). From `PreDeployCommand` as a one-element array. Empty or whitespace fails. Empty array is omitted (unset). Do not send `null`. |
| `overlapSeconds` set | `serviceInstanceUpdate.overlapSeconds` (Int). From `OverlapSeconds`. Must be greater than or equal to 0 (0 is no wait). Omitted when unset. Do not send `null`. In-deploy cutover, not `aspire destroy`. |
| `drainingSeconds` set | `serviceInstanceUpdate.drainingSeconds` (Int). From `DrainingSeconds`. Must be greater than or equal to 0 (0 is immediate kill). Either field can be set alone. Omitted when unset. Do not send `null`. |
| `cronSchedule` set | `serviceInstanceUpdate.cronSchedule` (String). From `CronSchedule`. Five-field crontab, UTC, minimum every 5 minutes. Empty or whitespace fails. Omitted when unset (always-on). Do not send `null`. |
| `customDomains` set | After the service id exists and today's `serviceDomainCreate` (when `WithExternalHttpEndpoints()`): `domains` to list, adopt existing hostnames (case-insensitive), else `customDomainAvailable` then `customDomainCreate`. Optional `targetPort` from the Aspire HTTP endpoint. Hostnames only in the plan — no verification tokens. |
| `targetPort` set | Optional Int on `serviceDomainCreate` and `customDomainCreate`. From the Aspire HTTP endpoint. Omitted when unset. Do not send `null`. |
| none of the above | today's image-only `source.image` update |

Config-as-code equivalent for CPU/RAM (mapping only, not the apply path): [`deploy.limitOverride.containers`](https://railway.com/railway.schema.json) with `cpu` and `memoryBytes`. GraphQL uses vCPU and GB floats, not bytes.

Never send `numReplicas` and `multiRegionConfig` on the same update. `serviceCreate` does not take these fields. Apply sends scale/region/sleep/healthcheck/restart-policy/start-command/pre-deploy/overlap/drain/cron on `serviceInstanceUpdate` after the service id exists (create and later updates), then `serviceInstanceLimitsUpdate` when CPU and/or RAM were requested. Do not add `vCPUs` / `memoryGB` onto `ServiceInstanceUpdateInput`. Do not add healthcheck, restart-policy, start-command, pre-deploy, teardown, or cron fields onto any other input. If the plan has region or a multi-region map, that update always includes `multiRegionConfig` so a later image-only update does not reset dashboard scale/region. `ServiceInstance` has no `multiRegionConfig` read field; apply does not invent a read-back query. Related read queries (`serviceInstanceLimits`, `serviceInstanceLimitOverride`) exist but are not used.

`Cpu` / `MemoryGb` must be greater than 0 when set. Dashboard plan caps (for example 24 vCPU) are plan-specific and are not hardcoded; if Railway rejects an over-plan value, deploy surfaces the GraphQL error.

Railway healthchecks are a deploy cutover probe, not continuous monitoring ([healthchecks](https://docs.railway.com/deployments/healthchecks)). The probe uses the service `PORT`. Origin host is `healthcheck.railway.app` — allow-list it if the app filters Host. Config-as-code mapping only: [`deploy.healthcheckPath` / `deploy.healthcheckTimeout`](https://docs.railway.com/reference/config-as-code) in [railway.schema.json](https://railway.com/railway.schema.json). GraphQL uses the same field names on `ServiceInstanceUpdateInput`. Do not use `environmentPatchCommit` / staged patches for this.

Restart policy is Railway-specific ([restart policy](https://docs.railway.com/deployments/restart-policy)). Unset omits `restartPolicyType` / `restartPolicyMaxRetries` so Railway's dashboard default (On Failure / 10 retries) applies. `RailwayRestartPolicy` members map to GraphQL `RestartPolicyType`: `OnFailure` → `ON_FAILURE`, `Always` → `ALWAYS`, `Never` → `NEVER`. Confirmed on the live schema 2026-08-20. Free/trial plan caps (Always unavailable, On Failure capped at 10) are not hardcoded. With multiple replicas, only the crashed replica restarts. Config-as-code mapping only: `deploy.restartPolicyType` / `deploy.restartPolicyMaxRetries` in [railway.schema.json](https://railway.com/railway.schema.json). GraphQL uses the same field names on `ServiceInstanceUpdateInput`. Do not use `environmentPatchCommit` / staged patches for this.

Start command and pre-deploy command are Railway-specific. Unset omits `startCommand` / `preDeployCommand` so the image ENTRYPOINT/CMD applies for start. On the image/Dockerfile v1 path, `startCommand` overrides ENTRYPOINT in **exec form**. There is no shell expansion unless the command is wrapped, for example `/bin/sh -c "exec … $PORT"`. See [start command](https://docs.railway.com/guides/start-command) and [deployments start command](https://docs.railway.com/deployments/start-command). Pre-deploy runs between build and deploy (migrations) on the private network with the app environment. A non-zero exit is not retried and the deploy stops. It runs in a separate container with **no volume**, so the filesystem does not persist. See [pre-deploy command](https://docs.railway.com/deployments/pre-deploy-command). AppHosts set a single `PreDeployCommand` string; publish writes GraphQL `preDeployCommand` as a one-element array. Empty or whitespace fails. An empty array is omitted. Aspire `WithArgs` is not mapped. These input fields were confirmed on the live schema 2026-08-20. Config-as-code mapping only: `deploy.startCommand` / `deploy.preDeployCommand` in [railway.schema.json](https://railway.com/railway.schema.json). GraphQL uses the same field names on `ServiceInstanceUpdateInput`. Do not use `environmentPatchCommit` / staged patches for this.

Deployment teardown (`OverlapSeconds` / `DrainingSeconds`) is Railway-specific **in-deploy lifecycle**, not `aspire destroy` / `destroy-{name}` (that stub is project/environment teardown and is a separate issue). After the new deploy is active, the previous replica stays up for `overlapSeconds`. Then Railway sends SIGTERM and waits `drainingSeconds` before SIGKILL. See [deployment teardown](https://docs.railway.com/guides/deployment-teardown) and [deployments teardown](https://docs.railway.com/deployments/deployment-teardown). Unset omits the fields. Either field can be set alone. Values must be greater than or equal to 0 when set (0 is no wait / immediate kill). These input fields were confirmed on the live schema 2026-08-20 as Int. Config-as-code mapping only: `deploy.overlapSeconds` / `deploy.drainingSeconds` in [railway.schema.json](https://railway.com/railway.schema.json) (examples type them as strings). GraphQL wants Int on `ServiceInstanceUpdateInput`. The documented variables `RAILWAY_DEPLOYMENT_OVERLAP_SECONDS` / `RAILWAY_DEPLOYMENT_DRAINING_SECONDS` are not the apply path. Volume-backed services cannot do zero-downtime; overlap does not invent a second volume mount.

Cron schedule (`CronSchedule`) is Railway-specific. Unset omits `cronSchedule` so the service stays always-on. Five-field crontab only (minute hour day month weekday), UTC. Railway's minimum frequency is every 5 minutes; `* * * * *` and minute-field `*/1` through `*/4` fail. Timezone names such as `Europe/London` are not converted to UTC. The service starts, runs the start command, and **must exit**. If it is still running at the next tick, Railway skips the new run and does not kill the previous one. There is no GraphQL for skip-if-still-running. Wrong fit for always-on HTTP APIs and bots; right fit for short tasks so you are not paying 24/7. HTTP healthchecks are a poor fit but are not auto-blocked. Combining cron with replicas greater than 1 or `Serverless = true` fails honestly. See [cron jobs](https://docs.railway.com/cron-jobs) and [cron workers and queues](https://docs.railway.com/guides/cron-workers-queues). This input field was confirmed on the live schema 2026-08-20. Config-as-code mapping only: `deploy.cronSchedule` in [railway.schema.json](https://railway.com/railway.schema.json). GraphQL uses the same field name on `ServiceInstanceUpdateInput`. Do not invent `cronCreate` / `scheduleCreate`. Do not use `environmentPatchCommit` / staged patches for this.

Custom hostnames (`CustomDomains`) are Railway-specific. v1 is a list of hostname strings on `PublishAsRailwayService`. Empty or whitespace fails. Duplicates fail. Hostnames are not secretly lowercased; adopt matches existing Railway domains case-insensitively. Requires `WithExternalHttpEndpoints()` — private services get neither a Railway service domain nor a custom hostname. Apex, subdomain, and wildcard all go through confirmed `customDomainCreate` (live schema 2026-08-20). Optional `targetPort` is sent when the Aspire HTTP endpoint has one (same for today's `serviceDomainCreate`). This integration does not talk to the user's DNS provider and does not special-case Cloudflare. Railway plan caps are not hardcoded.

Publish writes `customDomains` (hostnames only) into `railway-plan.json`. Verification tokens never land in the plan. Deploy, after the service id exists and after today's `serviceDomainCreate`:

1. `domains(environmentId, projectId, serviceId)` lists existing custom domains.
2. A same-hostname domain on that service/environment is adopted (no second create). Status is re-queried with `customDomain(id, projectId)`. `customDomainUpdate` runs only when the known target port differs.
3. Otherwise `customDomainAvailable` then `customDomainCreate` (`domain`, `environmentId`, `projectId`, `serviceId`, optional `targetPort`). Unavailable fails honestly with Railway's message.
4. The step report prints DNS records as Railway returned them (`recordType` / `fqdn` / `requiredValue`) plus `verificationDnsHost`, `verificationToken`, `verified`, and `certificateStatus`. Routing is CNAME / ALIAS-style; Railway has no static IP, so this integration does not rewrite records to A. Missing TXT returns 404 even if CNAME resolves. Railway issues Let's Encrypt after verify. Pending DNS or certificate does not fail the deploy.

See [working with domains](https://docs.railway.com/networking/domains/working-with-domains) and [manage domains](https://docs.railway.com/integrations/api/manage-domains). `customDomainDelete` and `customDomainIssueCertificate` are not called. Destroy of domains belongs to [#22](https://github.com/intrepid-developer/aspire-hosting-railway/issues/22). TCP proxies are out of scope. Flatten-safe custom-domain **ids** are persisted; tokens stay out of state.

Replicas and CPU/RAM limits cannot be used with [volumes](https://docs.railway.com/volumes/reference). `PublishAsRailwayPostgres` / `PublishAsRailwayRedis` are volume-backed templates: publish fails honestly if `WithReplicas` or `PublishAsRailwayService` scale/region/cpu/memory is set on them. Apply never sends `numReplicas` / `multiRegionConfig` / `serviceInstanceLimitsUpdate` / `healthcheckPath` / `healthcheckTimeout` / `restartPolicyType` / `restartPolicyMaxRetries` / `startCommand` / `preDeployCommand` / `overlapSeconds` / `drainingSeconds` / `cronSchedule` / custom domains for those services. Buckets stay on the existing create/credentials path and do not get CPU/RAM, healthcheck, restart-policy, start-command, pre-deploy, teardown, cron, or custom-domain fields. Volume-backed services still have a cutover gap even when a healthcheck is configured.

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

Booleans match the Railway dashboard kinds. Unset / false omits that kind. All false / no callback omits `volumeBackupScheduleKinds` from `railway-plan.json` so deploy leaves the dashboard as-is. At least one true kind is required to send the mutation. Empty or invalid deserialized kind strings fail honestly. Only `DAILY` / `WEEKLY` / `MONTHLY`.

`PublishAsRailwayRedis` is unchanged and does not silently enable backups. Buckets and app services do not get this field.

Product retention is mapping only (do not hardcode as API): Daily (24h, keep 6 days), Weekly (7d, keep 1 month), Monthly (30d, keep 3 months). Multiple kinds are allowed. Wiping a volume deletes its backups. See [volume backups](https://docs.railway.com/volumes/backups), [Postgres backups](https://docs.railway.com/guides/postgres-backups-restores), and [manage volumes](https://docs.railway.com/integrations/api/manage-volumes).

Deploy, after the official Postgres template service id exists (from `templateDeployV2` adopt or `project(id)`):

1. Query confirmed `environment(id)` (optional `projectId`) and select `volumeInstances { edges { node { id serviceId } } pageInfo }`. Live schema 2026-08-20: connection type `EnvironmentVolumeInstancesConnection`, edge type `EnvironmentVolumeInstancesConnectionEdge` (`cursor`, `node`). Match `node.serviceId` to the persisted Postgres service id. Retry like bucket credentials if the volume instance is not visible yet (template just finished). Fail honestly if none matches. Do not use `adminVolumeInstancesForVolume` or `volumeInstance(id)` unless the id is already known. Service has no `volumes` field; ServiceInstance has no `volume` field.
2. `volumeInstanceBackupScheduleList(volumeInstanceId)` — a list of `VolumeInstanceBackupSchedule`, not a connection.
3. Confirmed `volumeInstanceBackupScheduleUpdate(kinds: [VolumeInstanceBackupScheduleKind!]!, volumeInstanceId: String!)` returns `Boolean!` and **replaces** the kinds set. Apply unions requested kinds with already-present kinds so a dashboard schedule this plan did not mention is not removed. If requested kinds are already a subset of existing, skip the mutation.
4. Report the kinds applied. Deploy does not wait for a backup to complete.

Do not call `volumeInstanceBackupCreate` / `Delete` / `Lock` / `Restore`, `volumeInstancePITRRestore`, `enablePitrForHaCluster`, `pluginCreate`, or `environmentPatchCommitStaged` for backups. PITR enable on the live schema is `enablePitrForHaCluster` / `disablePitrForHaCluster` only; non-HA PITR enable is not a confirmed public mutation. This slice does not invent `WAL_ARCHIVE_*` or `bucketCreate` of `Postgres-PITR`. Restore is later. [#22](https://github.com/intrepid-developer/aspire-hosting-railway/issues/22) destroy must not invent backup or volume deletes. [#30](https://github.com/intrepid-developer/aspire-hosting-railway/issues/30) stays open for PITR enable.

Flatten-safe volume instance and schedule **ids** are persisted. Backup payloads stay out of plan and state.

