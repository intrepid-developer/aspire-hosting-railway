# Aspire Hosting for Railway

[![NuGet](https://img.shields.io/nuget/vpre/IntrepidDeveloper.Aspire.Hosting.Railway.svg?label=nuget)](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Aspire 13.4 hosting so `aspire publish` and `aspire deploy` provision [Railway](https://railway.com).

Locally you keep the normal Aspire resource model: official Postgres and Redis, `WithReference`, `WaitFor`, health checks, and the dashboard. Publish writes `railway-plan.json`. Deploy talks to Railway over GraphQL. `aspire run` never needs a Railway token and never calls Railway.

## Status

Preview on [nuget.org](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway). Pack also publishes a GitHub Release and [GitHub Packages](https://nuget.pkg.github.com/intrepid-developer/index.json). nuget.org uses Trusted Publishing (OIDC, no stored key). Current version: **0.1.0-preview.12** (from `Directory.Build.props`). MIT. Pinned to Aspire.Hosting **13.4.6** / `net10.0`. See [CHANGELOG.md](CHANGELOG.md).

## Packages

| Package | Role |
| --- | --- |
| [`IntrepidDeveloper.Aspire.Hosting.Railway`](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway) | Compute environment, pipeline, GraphQL client, `PublishAsRailwayService` |
| [`IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL`](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL) | `AddPostgres` locally; `PublishAsRailwayPostgres` on deploy |
| [`IntrepidDeveloper.Aspire.Hosting.Railway.Redis`](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway.Redis) | `AddRedis` locally; `PublishAsRailwayRedis` on deploy |
| [`IntrepidDeveloper.Aspire.Hosting.Railway.Storage`](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway.Storage) | `AddRailwayBucket`: local S3-compatible container; Railway bucket on deploy |
| [`IntrepidDeveloper.Aspire.Railway.Storage`](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Railway.Storage) | Client: `AddRailwayBucketClient` registers `IAmazonS3` |

AppHost extensions live in `Aspire.Hosting`. Resource types live in `Aspire.Hosting.Railway` / `.PostgreSQL` / `.Redis` / `.Storage`.

## Quick start

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io", "intrepid-developer/playground");
var railway = builder.AddRailwayEnvironment("railway")
    .WithContainerRegistry(ghcr);

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

`AddContainerRegistry` is required for image deploy. Pass the GHCR namespace as the third argument (`<owner>/<repository>`). The two-argument form has no owner/repo, so Aspire would push `ghcr.io/api` and GHCR rejects it. Railway has no registry of its own; without `IContainerRegistry` (GHCR or Docker Hub), deploy of image-based services fails. Aspire currently marks the registry APIs experimental (`ASPIRECOMPUTE003`). The playground sample matches this snippet.

In the API project:

```csharp
builder.AddNpgsqlDataSource("postgres");
builder.AddRedisClient("redis");
builder.AddRailwayBucketClient("uploads"); // IAmazonS3
```

Restore from [nuget.org](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway). No extra feed or PAT. These are prerelease packages, so use `--prerelease` or pin the version in `PackageReference` as below.

```bash
dotnet add package IntrepidDeveloper.Aspire.Hosting.Railway --prerelease
dotnet add package IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL --prerelease
dotnet add package IntrepidDeveloper.Aspire.Hosting.Railway.Redis --prerelease
dotnet add package IntrepidDeveloper.Aspire.Hosting.Railway.Storage --prerelease
dotnet add package IntrepidDeveloper.Aspire.Railway.Storage --prerelease
```

GitHub Packages is still published if you want that feed; see [Getting started](docs/getting-started.md). Do not commit PATs or `packageSourceCredentials`.

AppHost (`IntrepidDeveloper.Aspire.Hosting.Railway*`):

```xml
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway" Version="0.1.0-preview.12" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL" Version="0.1.0-preview.12" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.Redis" Version="0.1.0-preview.12" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.Storage" Version="0.1.0-preview.12" />
```

API / consuming project (`AddRailwayBucketClient` plus the usual Aspire clients):

```xml
<PackageReference Include="IntrepidDeveloper.Aspire.Railway.Storage" Version="0.1.0-preview.12" />
<PackageReference Include="Aspire.Npgsql" Version="13.4.6" />
<PackageReference Include="Aspire.StackExchange.Redis" Version="13.4.6" />
```

## Auth

Use an **account or workspace** token. Project tokens cannot call `projectCreate`.

| Where | What |
| --- | --- |
| AppHost parameter | `railway-token` (Aspire resource names cannot contain underscores) |
| Local / config | `RAILWAY_TOKEN` |
| CI | `RAILWAY_API_TOKEN` or `RAILWAY_TOKEN` |
| Adopt existing | `railway-project-id` / `railway-environment-id`, bound from `RAILWAY_PROJECT_ID` / `RAILWAY_ENVIRONMENT_ID` |

Local `aspire run` needs no token.

## Publish vs deploy

| | Publish | Deploy |
| --- | --- | --- |
| Command | `aspire publish` | `aspire deploy` |
| Talks to Railway? | No | Yes (GraphQL) |
| Output | `railway-plan.json` plus a `.env.example` of captured parameter names | Created or adopted Railway project, environment, services, templates, buckets |
| Secrets | Parameter **names** and Railway expressions when you use `AddParameter`. `WithEnvironment` string literals are written as-is | Resolves the token and parameter values in memory; never writes those to the plan or deployment state |

`AddRailwayEnvironment` is the Railway **project** (compute environment). The Railway environment name is mapped from Aspire `--environment`: Production → `production`, Staging → `staging` (lowercase). Override with `WithRailwayEnvironmentName`.

## Limits (honest)

- Railway has **no image registry**. Push to GHCR or Docker Hub, then deploy sets `source.image`. Missing `IContainerRegistry` fails clearly.
- This integration does not shell out to `railway up`. Railpack has no .NET support; use an image or a Dockerfile.
- `destroy-{name}` is a stub. Confirmed GraphQL operations do not include project or environment delete.
- PR / ephemeral Railway environments are not in this release.
- MySQL, MongoDB, and HA / PgBouncer are later.
- Railway buckets are **private**. Use S3 credentials or presigned URLs. They are not on private DNS.

## Docs

- [Getting started](docs/getting-started.md) — restore, AppHost, first publish/deploy, token setup
- [Publish and deploy](docs/publish-and-deploy.md) — pipeline, plan vs apply, adopt, staging, state, images
- [Storage](docs/storage.md) — buckets, local S3Mock, `IAmazonS3`, connection strings
- [GraphQL](docs/graphql.md) — confirmed operations only
- [CHANGELOG.md](CHANGELOG.md) — preview.11 and later
- [AGENTS.md](AGENTS.md) — contract for coding agents working this repo
- [SECURITY.md](SECURITY.md) — never commit secrets; how to report issues

## License

MIT
