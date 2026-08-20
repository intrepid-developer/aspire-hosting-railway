# Changelog

Versions match `Directory.Build.props`. Preview packages are on nuget.org (GitHub Packages is still published). This file starts at **0.1.0-preview.11**. Earlier previews are not listed here.

## 13.5.0-preview.2

- `aspire publish` writes replica, region, and `sleepApplication` settings into `railway-plan.json`. `aspire deploy` applies them on the existing `serviceInstanceUpdate` call together with `source.image`. `environmentId` is still sent on that mutation.
- Replica count comes from Aspire `WithReplicas` / `ReplicaAnnotation` (`GetReplicaCount`). Implicit compute on `AddRailwayEnvironment` picks this up; `PublishAsRailwayService` is not required just to set replicas.
- `WithReplicas` (no region / no map) sends `numReplicas`. A region or multi-region map sends `multiRegionConfig`. Never both. Live schema does not mark `numReplicas` deprecated.
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
