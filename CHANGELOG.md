# Changelog

Versions match `Directory.Build.props`. Preview packages are on nuget.org (GitHub Packages is still published). This file starts at **0.1.0-preview.11**. Earlier previews are not listed here.

## 13.5.0-preview.2

- `aspire publish` writes replica, region, and serverless settings into `railway-plan.json`. `aspire deploy` applies them on the existing `serviceInstanceUpdate` call together with `source.image`.
- Replica count comes from Aspire `WithReplicas` / `ReplicaAnnotation` (`GetReplicaCount`). Implicit compute on `AddRailwayEnvironment` picks this up; `PublishAsRailwayService` is not required just to set replicas.
- Region, serverless (`sleepApplication`), and multi-region `replicaRegions` are Railway-specific and use `PublishAsRailwayService`.
- Prefer `multiRegionConfig` when a region or multi-region map is set. `numReplicas` is the documented single-region fallback when only `WithReplicas` is set (it applies to the service's current Railway region). A multi-region map wins over `WithReplicas` + `Region`.
- Unknown region ids fail before GraphQL. Replica counts must be at least 1 and at most 50 total.
- Managed Postgres, Redis, and buckets are unchanged.

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
