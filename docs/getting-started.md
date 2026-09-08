# Getting started

Packages live on [nuget.org](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway). Pack also publishes a GitHub Release and GitHub Packages. nuget.org uses Trusted Publishing (OIDC, no stored key). Current version is **13.5.3** (`Directory.Build.props`). Pinned Aspire.Hosting **13.5.3** / `net10.0`.

## Restore from nuget.org

No extra feed or PAT. Add the packages (or pin the version in `PackageReference` as below):

```bash
dotnet add package IntrepidDeveloper.Aspire.Hosting.Railway
dotnet add package IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL
dotnet add package IntrepidDeveloper.Aspire.Hosting.Railway.Redis
dotnet add package IntrepidDeveloper.Aspire.Hosting.Railway.Storage
dotnet add package IntrepidDeveloper.Aspire.Railway.Storage
```

This repo's playground sample references the projects directly and does not need a package restore of these IDs.

## Restore from GitHub Packages (optional)

GitHub Packages is still published. Use this feed only if you want it. Keep nuget.org for Aspire and other dependencies. See `NuGet.Config.example`.

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

## AppHost

Extension methods live in `Aspire.Hosting`, so AppHosts need no extra `using` for `AddRailwayEnvironment` / `PublishAsRailway*`. Resource types (`RailwayRegion`, `RailwayBucketRegion`, `RailwayRestartPolicy`, `RailwayServiceResource`, `RailwayPostgresSettings`, `RailwayRedisSettings`) live in `Aspire.Hosting.Railway` / `.PostgreSQL` / `.Redis` / `.Storage`.

Use official resource types where they exist. Postgres and Redis stay `AddPostgres` / `AddRedis`; `PublishAsRailway*` only changes deploy. Railway replicas cannot be used with [volumes](https://docs.railway.com/volumes/reference), so those templates are not scaled. Buckets are `AddRailwayBucket` in the AppHost and `AddRailwayBucketClient` (`IAmazonS3`) in the consuming project. The AppHost also needs the official `Aspire.Hosting.PostgreSQL` and `Aspire.Hosting.Redis` packages for `AddPostgres` / `AddRedis`.

```xml
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway" Version="13.5.3" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL" Version="13.5.3" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.Redis" Version="13.5.3" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.Storage" Version="13.5.3" />
```

```csharp
using Aspire.Hosting.Railway;

var builder = DistributedApplication.CreateBuilder(args);

var ghcrUsername = builder.AddParameter("ghcr-username");
var ghcrPassword = builder.AddParameter("ghcr-password", secret: true);
var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io", "intrepid-developer/playground")
    .WithUsername(ghcrUsername)
    .WithPassword(ghcrPassword);
var railway = builder.AddRailwayEnvironment("railway")
    .WithContainerRegistry(ghcr);

var db = builder.AddPostgres("postgres").PublishAsRailwayPostgres(s =>
{
    s.Region = RailwayRegion.EuropeWest4;
    s.VolumeBackupDaily = true;
    s.VolumeBackupWeekly = true;
});
var cache = builder.AddRedis("redis").PublishAsRailwayRedis(s =>
{
    s.Region = RailwayRegion.EuropeWest4;
});
var uploads = builder.AddRailwayBucket("uploads", configure: b => b.Region = RailwayBucketRegion.Ams);

builder.AddProject<Projects.Api>("api")
    .WithReplicas(2)
    .WithHttpHealthCheck("/health")
    .WithReference(db)
    .WithReference(cache)
    .WithReference(uploads)
    .WaitFor(db)
    .WithExternalHttpEndpoints()
    .PublishAsRailwayService(s =>
    {
        s.Region = RailwayRegion.EuropeWest4;
        s.Cpu = 1;
        s.MemoryGb = 2;
        s.HealthcheckTimeoutSeconds = 120;
        s.RestartPolicy = RailwayRestartPolicy.OnFailure;
        s.RestartPolicyMaxRetries = 10;
        s.CustomDomains.Add("api.example.com");
    });

builder.Build().Run();
```

`WithReference` on official Postgres / Redis writes `ConnectionStrings__{name}` onto the consumer, same as local `AddPostgres` / `AddRedis`. The **format** follows the consumer:

| Consumer | `ConnectionStrings__{name}` | When `DATABASE_URL` / `REDIS_URL` is also set |
| --- | --- | --- |
| `AddProject` / `IProjectMetadata` (.NET, Aspire.Npgsql, Aspire.StackExchange.Redis, EF `UseNpgsql`) | Keyword form: `Host=${{postgres.PGHOST}};Port=…;Username=…;Password=…;Database=…` and `host:port,password=` for Redis. Composed from official Railway [Postgres](https://docs.railway.com/databases/postgresql) `PG*` / [Redis](https://docs.railway.com/databases/redis) `REDIS*` variables. | Stays the `postgresql://` / `redis://` URI (`${{postgres.DATABASE_URL}}` / `${{redis.REDIS_URL}}`). Use this for libraries that read `DATABASE_URL`. |
| `AddContainer` / Node / other URI processes | `${{postgres.DATABASE_URL}}` / `${{redis.REDIS_URL}}` | Same URI. |

`WithReference` does not invent a `DATABASE_URL` variable. Set it with `WithEnvironment` when the process reads that name. The plan stores Railway expressions only — never resolved passwords, never the deploy token.

Bucket `RailwayBucketRegion` codes (`iad` / `sjc` / `ams` / `sin`) are not compute `RailwayRegion` ids. Unset buckets stay `iad`. EU AppHosts must set `ams`. Bucket region cannot be changed after create (drop + recreate). Official Postgres / Redis `Region` is applied after first-time template create via one `serviceInstanceUpdate` (`region` + `numReplicas` 1). Volume backups do not send a second region update. Apply does not create an image-less Railway service per bucket; credentials reach `WithReference` consumers as `ConnectionStrings__{name}` at deploy time. Offline leftovers from earlier previews can be deleted in the Railway dashboard — v1 does not adopt or destroy them automatically.

`AddContainerRegistry` is required for image deploy. Pass owner/repo as the third argument (`<owner>/<repository>`). The two-argument form has no owner/repo, so Aspire would push `ghcr.io/api` and GHCR rejects it. Railway has no image registry. Private GHCR (and similar) images also need `WithUsername` / `WithPassword` parameter references so Railway can pull them — that is a **Pro plan** feature. Bind the password parameter from `GITHUB_TOKEN` in CI; do not paste a PAT into chat or commit it. Username and password are resolved at `aspire deploy` only and never written to `railway-plan.json`. Missing credentials for a private image host fail the deploy. Aspire marks those APIs experimental (`ASPIRECOMPUTE003`); the playground AppHost suppresses that diagnostic so the sample still compiles with warnings-as-errors. Local `aspire run` still works without talking to Railway.

In the API / consuming project, add the storage client plus the usual Aspire Npgsql and Redis clients:

```xml
<PackageReference Include="IntrepidDeveloper.Aspire.Railway.Storage" Version="13.5.3" />
<PackageReference Include="Aspire.Npgsql" Version="13.5.3" />
<PackageReference Include="Aspire.StackExchange.Redis" Version="13.5.3" />
```

```csharp
builder.AddNpgsqlDataSource("postgres");
builder.AddRedisClient("redis");
builder.AddRailwayBucketClient("uploads");
```

Existing Aspire client packages keep working. `AddRailwayEnvironment` is the Railway **project** (compute environment). `--environment` Production/Staging maps to `production`/`staging`. Override with `WithRailwayEnvironmentName`. Kitchen-sink, cron, and multi-region samples are in the [README](../README.md). Full mapping is in [Publish and deploy](publish-and-deploy.md).

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

## First publish / deploy / destroy

```bash
aspire publish
aspire deploy
aspire destroy
```

Publish writes `railway-plan.json` plus a `.env.example` of captured parameter names. It does not call Railway. Secrets stay out of the plan only when they are Aspire parameters (`AddParameter(secret: true)`). `WithEnvironment("API_KEY", value)` string literals are written as-is.

Deploy resolves the token, applies the plan, persists Railway ids, and reports real progress or failures. Image-based services need `IContainerRegistry` (GHCR or Docker Hub). Missing registry fails with a message to add one. Private image hosts also need resolved `WithUsername` / `WithPassword` parameters. This integration does not run `railway up`.

Destroy tears down resources **this integration created** in the mapped Railway environment (`aspire destroy --environment Staging` → `staging`). Aspire already prompts; `--yes` / `--non-interactive --yes` skip that prompt. Adopted resources are skipped. Buckets stay (no public `bucketDelete`). Leftover Offline image-less services from earlier previews that sat next to a bucket are not adopted or `serviceDelete`d automatically; delete them in the Railway dashboard. The Railway project is not deleted. This is not in-deploy overlap/drain.

See [publish-and-deploy.md](publish-and-deploy.md) for pipeline steps, adopt, staging, destroy, and image resolution.
