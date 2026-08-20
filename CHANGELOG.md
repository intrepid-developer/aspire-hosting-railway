# Changelog

Versions match `Directory.Build.props`. Preview packages are on nuget.org (GitHub Packages is still published). This file starts at **0.1.0-preview.11**. Earlier previews are not listed here.

## 13.5.0-preview.7

- `PublishAsRailwayService` can set `StartCommand` and `PreDeployCommand`. There is no Aspire-core annotation, and Aspire `WithArgs` is not mapped. Unset omits the fields so the image ENTRYPOINT/CMD applies for start. Either field can be set alone. Empty or whitespace fails honestly.
- `aspire publish` writes `startCommand` (string) and `preDeployCommand` (array of strings) into `railway-plan.json`. A single `PreDeployCommand` becomes a one-element array. Unset fields and an empty array are omitted. Do not send `null`.
- `aspire deploy` applies them on the existing `serviceInstanceUpdate` call (`ServiceInstanceUpdateInput.startCommand` String, `preDeployCommand` `[String!]`). Always pass `environmentId`. These fields were confirmed on the live schema 2026-08-20. No new mutation. Limits stay on `serviceInstanceLimitsUpdate`.
- Image/Dockerfile v1 start command overrides ENTRYPOINT in exec form. There is no shell expansion unless wrapped, for example `/bin/sh -c "exec … $PORT"`. See [start command](https://docs.railway.com/guides/start-command) and [deployments start command](https://docs.railway.com/deployments/start-command).
- Pre-deploy runs between build and deploy (migrations) on the private network with the app environment. A non-zero exit is not retried and the deploy stops. It runs in a separate container with no volume, so the filesystem does not persist. See [pre-deploy command](https://docs.railway.com/deployments/pre-deploy-command).
- Config-as-code `deploy.startCommand` / `deploy.preDeployCommand` are mapping only. The apply path is `serviceInstanceUpdate`.
- `PublishAsRailwayPostgres` / `PublishAsRailwayRedis` / buckets do not get these fields.

## 13.5.0-preview.6

- `PublishAsRailwayService` can set `RestartPolicy` (`RailwayRestartPolicy`) and `RestartPolicyMaxRetries`. There is no Aspire-core restart-policy annotation. Unset omits the fields so Railway's dashboard default (On Failure / 10 retries) applies. Either field can be set alone. Retries must be greater than 0 when set.
- AppHost enum members map to GraphQL `RestartPolicyType`: `OnFailure` → `ON_FAILURE`, `Always` → `ALWAYS`, `Never` → `NEVER`. See [restart policy](https://docs.railway.com/deployments/restart-policy).
- `aspire publish` writes `restartPolicyType` / `restartPolicyMaxRetries` into `railway-plan.json`. Unset fields are omitted. Do not send `null`.
- `aspire deploy` applies them on the existing `serviceInstanceUpdate` call (`ServiceInstanceUpdateInput.restartPolicyType` enum, `restartPolicyMaxRetries` Int). Always pass `environmentId`. These fields were confirmed on the live schema 2026-08-20. No new mutation. Limits stay on `serviceInstanceLimitsUpdate`.
- Free/trial plan caps (Always unavailable, On Failure capped at 10) are plan-specific and are not hardcoded; Railway GraphQL errors are surfaced. With multiple replicas, only the crashed replica restarts.
- `PublishAsRailwayPostgres` / `PublishAsRailwayRedis` / buckets do not get these fields.

## 13.5.0-preview.5

- Deploy healthcheck path comes from Aspire `WithHttpHealthCheck` / `HealthCheckAnnotation` (the same idea as `WithReplicas` → replica count). Implicit compute on `AddRailwayEnvironment` picks it up; `PublishAsRailwayService` is not required just to set the path.
- Railway-specific timeout is `PublishAsRailwayService` `HealthcheckTimeoutSeconds`. There is no Aspire-core timeout annotation. Unset omits the field so Railway's default (300 seconds) applies. Values must be greater than 0 when set.
- `aspire publish` writes `healthcheckPath` / `healthcheckTimeout` into `railway-plan.json`. Unset fields are omitted. Do not send `null`.
- `aspire deploy` applies them on the existing `serviceInstanceUpdate` call (`ServiceInstanceUpdateInput.healthcheckPath` String, `healthcheckTimeout` Int). Always pass `environmentId`. These fields were confirmed on the live schema 2026-08-20. No new mutation. The documented variable `RAILWAY_HEALTHCHECK_TIMEOUT_SEC` is not the apply path.
- Railway probes until HTTP 200, then flips traffic. It is not continuous monitoring. The probe uses `PORT`. Origin host is `healthcheck.railway.app`. Volume-backed services still have cutover downtime.
- A non-200 Aspire `statusCode` on `WithHttpHealthCheck` is ignored; Railway always wants 200. Custom `WithHealthCheck` keys that are not HTTP probes are not mapped.
- `PublishAsRailwayPostgres` / `PublishAsRailwayRedis` / buckets do not get these fields.

## 13.5.0-preview.4

- Breaking preview API: `PublishAsRailwayService` region is compile-time typed. `RailwayServiceResource.Region` is `RailwayRegion?` and `ReplicaRegions` is `Dictionary<RailwayRegion, int>?`. There is no string setter.
- `RailwayRegion` members are the four official Railway deploy keys (verified 2026-08-20): `UsWest2` → `us-west2`, `UsEast4` → `us-east4-eqdc4a`, `EuropeWest4` → `europe-west4-drams3a`, `AsiaSoutheast1` → `asia-southeast1-eqsg3a`.
- Airport codes (`sjc` / `iad` / `ams` / `sin`) and older ids (`us-west1`, `us-east4`, `europe-west4`) cannot be assigned on the AppHost surface. `railway-plan.json` still stores official `Region.region` strings; unknown deserialized ids fail honestly before GraphQL.
- GraphQL apply is unchanged: official region id strings only in `multiRegionConfig`. `numReplicas` remains the single-region `WithReplicas` path. Never both.

## 13.5.0-preview.3

- `PublishAsRailwayService` can set per-replica `Cpu` and `MemoryGb` on `RailwayServiceResource`. There is no Aspire-core `WithCpu` / `WithMemory` in Aspire.Hosting 13.5.0.
- `aspire publish` writes `cpu` / `memoryGb` into `railway-plan.json`. Unset fields are omitted.
- `aspire deploy` applies them with the confirmed `serviceInstanceLimitsUpdate` mutation after the service id exists (create and later updates), after today's `serviceInstanceUpdate` image/scale call. Always sends `serviceId` and `environmentId`. GraphQL fields are `vCPUs` and `memoryGB` (floats). Unset fields are omitted; an empty limits update is not sent.
- This is a different mutation from `serviceInstanceUpdate`. vCPU / memory are not added onto `ServiceInstanceUpdateInput`.
- Values must be greater than 0 when set. Dashboard plan caps (for example 24) are plan-specific and are not hardcoded; Railway GraphQL errors are surfaced.
- `PublishAsRailwayPostgres` / `PublishAsRailwayRedis` / buckets do not get these fields and do not call `serviceInstanceLimitsUpdate`.
- Config-as-code `deploy.limitOverride.containers` (`cpu` / `memoryBytes`) is documented as a mapping only. GraphQL uses GB and vCPU floats, not bytes.

## 13.5.0-preview.2

- `aspire publish` writes replica, region, and `sleepApplication` settings into `railway-plan.json`. `aspire deploy` applies them on the existing `serviceInstanceUpdate` call together with `source.image`. `environmentId` is still sent on that mutation.
- Replica count comes from Aspire `WithReplicas` / `ReplicaAnnotation` (`GetReplicaCount`). Implicit compute on `AddRailwayEnvironment` picks this up; `PublishAsRailwayService` is not required just to set replicas.
- `WithReplicas` (no region / no map) sends `numReplicas` (documented single-region path). A region or multi-region map sends `multiRegionConfig`. Never both.
- Region and `sleepApplication` are Railway-specific and use `PublishAsRailwayService`. There is no GraphQL field named `serverless`; `sleepApplication` applies to all replicas of the service.
- Official deploy region ids only (`Region.region`): `us-west2`, `us-east4-eqdc4a`, `europe-west4-drams3a`, `asia-southeast1-eqsg3a`. Airport codes (`sjc` / `iad` / `ams` / `sin`) and older ids (`us-west1`, `us-east4`, `europe-west4`) are rejected.
- Replica counts must be at least 1 and at most 50 total (CLI / product docs), not the 200 in `railway.schema.json`.
- Replicas cannot be used with volumes. `PublishAsRailwayPostgres` / `PublishAsRailwayRedis` fail honestly if scale is requested. `ServiceInstance` has no `multiRegionConfig` read field; apply does not invent a read-back query.

## 13.5.0-preview.1

- Retarget to Aspire.Hosting 13.5.0.
- Package version now tracks Aspire (`13.5.x-preview.n`) instead of the `0.1.0-preview.n` line. This release is `13.5.0-preview.1`.
- Align `Microsoft.Extensions.*` package versions with Aspire.Hosting 13.5.0 (`10.0.11`) so restore does not downgrade.

## 0.1.0-preview.12

- Adopt existing Railway buckets by name from `project(id).buckets` on deploy so CI and new machines do not `bucketCreate` a second undeployed bucket (`BucketInstance not found`).
- After a real `bucketCreate`, retry `bucketS3Credentials` until a BucketInstance exists instead of querying immediately.
- A same-name service is never used as a `bucketId`.
- Bucket secrets stay out of plan files and deployment state; only flatten-safe bucket ids are persisted.
- Successful Pack jobs also publish a GitHub Release for the packed version.
- Pack also publishes to nuget.org via Trusted Publishing (OIDC, no stored key).
- Docs now point at nuget.org as the default restore path.

## 0.1.0-preview.11

- Previous published preview on GitHub Packages.
- Empty optional captured parameters are omitted on `aspire deploy` instead of aborting. Missing required parameters still fail.
