# Storage

`AddRailwayBucket` is a real Aspire resource (`RailwayBucketResource`). Locally it starts a maintained S3-compatible container. On deploy it becomes a Railway bucket.

## Local vs deploy

| | Local `aspire run` | Deploy |
| --- | --- | --- |
| Backing | [Adobe S3Mock](https://github.com/adobe/S3Mock) (`adobe/s3mock:4.9.1`) | `bucketCreate` + `bucketS3Credentials` |
| Endpoint | The emulator HTTP endpoint | `https://storage.railway.app` |
| Addressing | Path-style (`ForcePathStyle=true`) | Virtual-hosted (`ForcePathStyle=false`) |
| Credentials | Placeholder `s3mock` / `s3mock` | Fresh S3 keys from `bucketS3Credentials` (in memory only) |

The hosting package is `IntrepidDeveloper.Aspire.Hosting.Railway.Storage`. It is not the deprecated CommunityToolkit MinIO package. Region is immutable after `bucketCreate`. Railway buckets are **not** on private DNS.

On deploy of an adopted project, apply lists `project.buckets` from the documented `project(id)` query (same confirmed operation that lists services — verified on Railway's GraphQL schema as a Relay connection of `Bucket { id name }`; this is not a new query name). If a planned `Kind = bucket` resource matches a bucket display name (case-insensitive), that id is recorded in `BucketIds` and `bucketCreate` is skipped. `bucketCreate` runs only when no matching bucket exists. A same-name **service** is unrelated and is never passed to `bucketS3Credentials`.

After a real `bucketCreate`, apply retries `bucketS3Credentials` with backoff until a `BucketInstance` exists in the target environment (or the wait times out). Credentials are then used in memory only.

Apply also creates an image-less Railway service with the bucket resource name so `${{uploads.ENDPOINT}}` (and related) variables exist for `WithReference`. That service is not a compute target and is not deployed with `serviceInstanceDeployV2`.

Bucket **secrets** are never written to `railway-plan.json` or `IDeploymentStateManager`. Flatten-safe bucket **ids** are persisted as JSON objects (not arrays) so a local retry can skip create; CI / a new machine without that file adopts by name from `project.buckets`.

## Client

The consuming project uses `IntrepidDeveloper.Aspire.Railway.Storage`:

```csharp
builder.AddRailwayBucketClient("uploads"); // IAmazonS3
```

`AddRailwayBucketClient("uploads")` registers keyed and unkeyed `IAmazonS3` plus `RailwayBucketSettings` from `ConnectionStrings:uploads`. Local S3-compatible endpoints default to path-style; `storage.railway.app` uses virtual-hosted style.

## Connection string

```
Endpoint=https://storage.railway.app;AccessKeyId=...;SecretAccessKey=...;Bucket=uploads;Region=auto;ForcePathStyle=false
```

Semicolon-delimited keys: `Endpoint`, `AccessKeyId` (or `AccessKey`), `SecretAccessKey` (or `SecretKey`), `Bucket` (or `BucketName`), `Region`, `ForcePathStyle`.

On Railway, region is typically `auto`. Local emulator strings use `Region=us-east-1;ForcePathStyle=true`.

## Private by design

Railway buckets are private. There is no public HTTP object URL from this integration. Use the S3 API with the connection credentials, or mint presigned URLs in your own code.

## `WithReference` vs apply

Publish (`RailwayPlanBuilder`) only writes `ConnectionStrings__{name}` onto compute services that actually `WithReference` the bucket. The plan stores Railway expressions such as `${{uploads.ENDPOINT}}`, never resolved keys.

Deploy apply currently copies every resolved bucket connection string onto **every** compute service (`RailwayGraphQLApplyService` merges `BucketConnectionStrings` in `ResolveServiceEnvironment`). That is broader than the plan. Do not treat the extra copies as the public contract; the intended surface is still `WithReference` on the services that need the bucket.
