# Getting started

Preview packages live on GitHub Packages, not nuget.org. Current version is **0.1.0-preview.10** (`Directory.Build.props`). Pinned Aspire.Hosting **13.4.6** / `net10.0`.

## Restore from GitHub Packages

Keep nuget.org for Aspire and other dependencies. Add the Intrepid Developer GitHub Packages source. See `NuGet.Config.example`.

GitHub Packages NuGet requires authentication even though this repository is public. Do not commit PATs or `packageSourceCredentials`.

Locally, a personal access token with `read:packages`:

```bash
dotnet nuget add source https://nuget.pkg.github.com/intrepid-developer/index.json \
  --name github-intrepid-developer \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_PAT \
  --store-password-in-clear-text
```

Or put credentials in a user-level NuGet config, then `dotnet restore`.

In GitHub Actions (`permissions: packages: read`):

```bash
dotnet nuget add source https://nuget.pkg.github.com/intrepid-developer/index.json \
  --name github-intrepid-developer \
  --username ${{ github.actor }} \
  --password ${{ secrets.GITHUB_TOKEN }} \
  --store-password-in-clear-text
```

```xml
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway" Version="0.1.0-preview.10" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL" Version="0.1.0-preview.10" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.Redis" Version="0.1.0-preview.10" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.Storage" Version="0.1.0-preview.10" />
<PackageReference Include="IntrepidDeveloper.Aspire.Railway.Storage" Version="0.1.0-preview.10" />
```

This repo's playground sample references the projects directly and does not need GitHub Packages.

## AppHost

Extension methods live in `Aspire.Hosting`, so AppHosts need no extra `using`. Resource types live in `Aspire.Hosting.Railway` / `.PostgreSQL` / `.Redis` / `.Storage`.

Use official resource types where they exist. Postgres and Redis stay `AddPostgres` / `AddRedis`; `PublishAsRailway*` only changes deploy. Buckets are `AddRailwayBucket` in the AppHost and `AddRailwayBucketClient` (`IAmazonS3`) in the consuming project.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io");
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

`AddContainerRegistry` + `WithContainerRegistry` is required for image deploy. Railway has no image registry. Aspire currently marks those APIs experimental (`ASPIRECOMPUTE003`); the playground AppHost suppresses that diagnostic so the sample still compiles with warnings-as-errors. Local `aspire run` still works without talking to Railway.

In the API project:

```csharp
builder.AddNpgsqlDataSource("postgres");
builder.AddRedisClient("redis");
builder.AddRailwayBucketClient("uploads");
```

Existing Aspire client packages keep working. `AddRailwayEnvironment` is the Railway **project** (compute environment). The Railway environment name is mapped from Aspire `--environment`: Production → `production`, Staging → `staging` (lowercase). Override with `WithRailwayEnvironmentName`.

## Token setup

Use an **account or workspace** token. Project tokens cannot call `projectCreate`.

| Where | What |
| --- | --- |
| AppHost parameter | `railway-token` (Aspire resource names cannot contain underscores) |
| Local / config | `RAILWAY_TOKEN` |
| CI | `RAILWAY_API_TOKEN` or `RAILWAY_TOKEN` |
| Adopt existing | `railway-project-id` / `railway-environment-id`, bound from `RAILWAY_PROJECT_ID` / `RAILWAY_ENVIRONMENT_ID` |

Copy `.env.example` to a local `.env` (gitignored) or set environment variables on the machine that deploys. Never commit the values.

Local `aspire run` needs no token and never talks to Railway. The Railway environment resource is not added to the model in run mode.

## First publish / deploy

```bash
aspire publish
aspire deploy
```

Publish writes `railway-plan.json` (expressions and parameter **names** only) plus a `.env.example` of captured parameter names. It does not call Railway.

Deploy resolves the token, applies the plan over GraphQL, persists Railway ids, and reports real progress or failures. Image-based services need `IContainerRegistry` (GHCR or Docker Hub). Missing registry fails with a message to add one. This integration does not run `railway up`.

See [publish-and-deploy.md](publish-and-deploy.md) for pipeline steps, adopt, staging, and image resolution.
