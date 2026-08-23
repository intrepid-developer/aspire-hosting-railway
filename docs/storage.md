# Storage

`AddRailwayBucket` is a real Aspire resource (`RailwayBucketResource`). Locally it starts a maintained S3-compatible container. On deploy it becomes a Railway bucket.

## Local vs deploy

| | Local `aspire run` | Deploy |
| --- | --- | --- |
| Backing | [Adobe S3Mock](https://github.com/adobe/S3Mock) (`adobe/s3mock:4.9.1`) | `bucketCreate` (record) + environment patch (instance) + `bucketS3Credentials` |
| Endpoint | The emulator HTTP endpoint | `https://storage.railway.app` |
| Addressing | Path-style (`ForcePathStyle=true`) | Virtual-hosted (`ForcePathStyle=false`) |
| Credentials | Placeholder `s3mock` / `s3mock` | Fresh S3 keys from `bucketS3Credentials` (in memory only) |

The hosting package is `IntrepidDeveloper.Aspire.Hosting.Railway.Storage`. It is not the deprecated CommunityToolkit MinIO package. Bucket region is a Tigris airport code (`iad` / `sjc` / `ams` / `sin`) and is immutable after the instance is provisioned. Unset keeps `iad` so existing AppHosts do not silently move. EU AppHosts must set `ams`. Changing region after create means drop + recreate the bucket. These codes are not compute `RailwayRegion` ids. Railway buckets are **not** on private DNS.

```csharp
builder.AddRailwayBucket("uploads", configure: b => b.Region = RailwayBucketRegion.Ams);
// or
builder.AddRailwayBucket("uploads").WithRegion(RailwayBucketRegion.Ams);
```

On deploy of an adopted project, apply lists `project.buckets` from the documented `project(id)` query (same operation that lists services). If a planned bucket matches a display name (case-insensitive), that id is recorded and `bucketCreate` is skipped. `bucketCreate` runs only when no matching bucket exists. A same-name **service** is unrelated and is never passed to `bucketS3Credentials`.

`bucketCreate` only creates the project record. Apply then stages and commits an `EnvironmentConfig` patch that attaches the bucket instance (`buckets.{id}.region` + `isCreated`). Unset AppHosts keep `iad`. A requested Tigris code is sent on that patch. After that provision, apply retries `bucketS3Credentials` with backoff until keys exist. Canvas-created buckets already have an instance — create on the canvas, then deploy, and we adopt by name. Adopted instances are not re-patched (region is immutable). Credentials are used in memory only.

Apply does **not** create an image-less Railway service to hold `${{uploads.ENDPOINT}}` variables. Earlier previews did, which left an Offline compute card next to each bucket. `AddRailwayBucketClient` reads `ConnectionStrings:{name}` only; apply stamps the resolved connection string onto compute services that `WithReference` the bucket. Existing Offline leftovers from those previews can be deleted in the Railway dashboard. v1 does not adopt them as compute or as the bucket, and does not `serviceDelete` them automatically (destroy still skips buckets; there is no `bucketDelete`).

Bucket **secrets** are never written to `railway-plan.json` or `IDeploymentStateManager`. The plan stores a non-secret placeholder (`railway-bucket://uploads`) on referencing services. Flatten-safe bucket **ids** are persisted as JSON objects (not arrays) so a local retry can skip create; CI / a new machine without that file adopts by name from `project.buckets`.

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

Publish (`RailwayPlanBuilder`) only writes `ConnectionStrings__{name}` onto compute services that actually `WithReference` the bucket. The plan stores a placeholder (`railway-bucket://uploads`), never `${{uploads.ENDPOINT}}` service-variable refs and never resolved keys.

Deploy apply replaces that placeholder with the in-memory connection string on those same services only (`RailwayGraphQLApplyService` in `ResolveServiceEnvironment`). Services that do not `WithReference` the bucket do not receive `ConnectionStrings__{name}`.
