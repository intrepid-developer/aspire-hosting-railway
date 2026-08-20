# Changelog

Versions match `Directory.Build.props`. Preview packages are on nuget.org (GitHub Packages is still published). This file starts at **0.1.0-preview.11**. Earlier previews are not listed here.

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
