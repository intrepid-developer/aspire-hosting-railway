# Aspire Hosting for Railway

Open-source Aspire 13.4 hosting integrations that let you `aspire publish` and `aspire deploy` a distributed app to [Railway](https://railway.com).

This is the `IntrepidDeveloper.Aspire.Hosting.Railway` package family. Locally you keep the normal Aspire resource model. On publish/deploy, the Railway compute environment provisions (or adopts) a Railway project and environment, deploys your services, and wires Railway-backed databases and buckets.

These are first-class Aspire integrations: official resource types where they exist, standard connection strings and properties, `WithReference`, `WaitFor`, health checks, and the dashboard. Existing Aspire client packages keep working. Local `aspire run` never needs a Railway token and never talks to Railway.

## Packages

| Package | Role |
| --- | --- |
| `IntrepidDeveloper.Aspire.Hosting.Railway` | Compute environment, pipeline, GraphQL client, `PublishAsRailwayService` |
| `IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL` | `AddPostgres` locally; `PublishAsRailwayPostgres` on deploy |
| `IntrepidDeveloper.Aspire.Hosting.Railway.Redis` | `AddRedis` locally; `PublishAsRailwayRedis` on deploy |
| `IntrepidDeveloper.Aspire.Hosting.Railway.Storage` | `AddRailwayBucket`: local S3-compatible container; Railway bucket on deploy |
| `IntrepidDeveloper.Aspire.Railway.Storage` | Client: `AddRailwayBucketClient` registers `IAmazonS3` |

Pinned on Aspire.Hosting **13.4.6** / `net10.0`. Later: MySQL, MongoDB, HA / PgBouncer, cron, PR environment clones.

## AppHost usage

Extension methods live in `Aspire.Hosting`, so AppHosts need no extra `using`. Resource types live in `Aspire.Hosting.Railway` / `.PostgreSQL` / `.Redis` / `.Storage`.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var railway = builder.AddRailwayEnvironment("railway");

var db = builder.AddPostgres("postgres").PublishAsRailwayPostgres();
var cache = builder.AddRedis("redis").PublishAsRailwayRedis();
var uploads = builder.AddRailwayBucket("uploads");

builder.AddProject<Projects.Api>("api")
    .WithReference(db)
    .WithReference(cache)
    .WithReference(uploads)
    .WaitFor(db)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

In the API project:

```csharp
builder.AddNpgsqlDataSource("postgres");
builder.AddRedisClient("redis");
builder.AddRailwayBucketClient("uploads"); // IAmazonS3
```

`AddRailwayEnvironment` is the Railway **project** (compute environment). The Railway environment name is mapped from Aspire `--environment`: Production → `production`, Staging → `staging` (lowercase). Override with `WithRailwayEnvironmentName`.

Adopt an existing canvas:

```csharp
builder.AddRailwayEnvironment("railway").AsExisting();
// or pass parameters bound from RAILWAY_PROJECT_ID / RAILWAY_ENVIRONMENT_ID
```

If a staging environment does not exist on deploy, the default is to duplicate production (`environmentCreate` with `sourceEnvironmentId`) when production exists. Empty create is opt-in (`CreateEmptyEnvironment`). Re-deploy must not create a second project; IDs will be persisted in `IDeploymentStateManager`.

PR / ephemeral environment APIs are not part of this release.

## Auth

Use an **account or workspace** token. Project tokens cannot call `projectCreate`.

- Aspire resource names cannot contain underscores, so the parameter resource is `railway-token`, bound from configuration key `RAILWAY_TOKEN`
- CI may set `RAILWAY_API_TOKEN` or `RAILWAY_TOKEN`
- Adopt-existing IDs use `railway-project-id` / `railway-environment-id`, bound from `RAILWAY_PROJECT_ID` / `RAILWAY_ENVIRONMENT_ID`
- Local `aspire run` does not need a token

## Publish and deploy

This integration uses Aspire 13.4 compute-environment + pipeline hooks (`PipelineStepAnnotation`, `WellKnownPipelineSteps`). It does **not** use the obsolete `IDistributedApplicationPublisher` / `DeployingCallbackAnnotation` model.

| Step | What it does today |
| --- | --- |
| `prepare-deployment-targets-{name}` | Materializes `RailwayServiceResource` children and `DeploymentTargetAnnotation` |
| `publish-{name}` | Writes `railway-plan.json` (expressions and parameter **names** only) plus a `.env.example` of captured parameter names |
| `deploy-{name}` | Reports that GraphQL apply is **not implemented** yet. Does not contact Railway and does not fake success |
| `destroy-{name}` | Reports that GraphQL teardown is not implemented yet |

Railway has **no image registry**. Resolve `IContainerRegistry` from the model (for example `builder.AddContainerRegistry("ghcr", "ghcr.io")`). If it is missing, deploy of image-based services fails with a message to add GHCR or Docker Hub. This integration does not shell out to `railway up`. Railpack has no .NET support; use images or a Dockerfile.

Host addresses are host-only: `{service}.railway.internal` (lowercase). Endpoints and secrets are never concatenated into strings before Aspire resolves them.

### Databases and buckets

Postgres and Redis stay official `AddPostgres` / `AddRedis`. `PublishAsRailway*` only changes deploy. `Aspire.Npgsql` and `Aspire.StackExchange.Redis` keep working. In publish mode, `WithReference` emits Railway references such as `${{postgres.DATABASE_URL}}` (private), never the local Docker connection string.

Official DBs will be created later via `template(code: "postgres"|"redis")` then `templateDeployV2` with the fetched `serializedConfig` (never empty, never invented template UUIDs). `ApplyTemplateAsync` is a stub in this release.

`AddRailwayBucket` is a real Aspire resource. Local run starts a maintained S3-compatible container ([Adobe S3Mock](https://github.com/adobe/S3Mock)); deploy will use `bucketCreate` + `bucketS3Credentials`. Railway buckets use `https://storage.railway.app`, virtual-hosted URLs, and an immutable region. They are not on private DNS.

### Storage client connection string

```
Endpoint=https://storage.railway.app;AccessKeyId=...;SecretAccessKey=...;Bucket=uploads;Region=auto;ForcePathStyle=false
```

`AddRailwayBucketClient("uploads")` registers `IAmazonS3` from `Endpoint`, `AccessKeyId`, `SecretAccessKey`, `Bucket`, and `Region`. Local S3-compatible endpoints default to path-style; `storage.railway.app` uses virtual-hosted style.

## GraphQL

The integration talks to `https://backboard.railway.com/graphql/v2`. Confirmed operations used by the typed client: `projectCreate`, `environmentCreate`, `serviceCreate` (always pass `environmentId`), `serviceInstanceUpdate`, `serviceInstanceDeployV2`, `variableCollectionUpsert`, `serviceDomainCreate`, `template` + `templateDeployV2`, `workflowStatus`, `bucketCreate`, `bucketS3Credentials`, `environmentPatchCommitStaged`, `regions`.

Deprecated and unused: `pluginCreate`, `templateDeploy` v1.

## Secrets

This repo is public. Do not commit tokens, `.env` files, user-secrets, NuGet API keys, or real Railway project/environment IDs.

- Copy `.env.example` to a local `.env` (gitignored)
- Pass `RAILWAY_TOKEN` as an Aspire parameter or environment variable on the machine that deploys
- `railway-plan.json` and samples must contain placeholders / parameter names only
- PRs run Gitleaks; GitHub secret scanning and push protection are on

See [SECURITY.md](SECURITY.md).

## Build and test

```bash
dotnet build IntrepidDeveloper.Aspire.Hosting.Railway.slnx
dotnet test IntrepidDeveloper.Aspire.Hosting.Railway.slnx
```

Unit tests do not need a Railway token and do not use the network.

## License

MIT
