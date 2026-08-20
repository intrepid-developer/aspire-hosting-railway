# Getting started

Preview packages live on [nuget.org](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway). Pack also publishes a GitHub Release and GitHub Packages. nuget.org uses Trusted Publishing (OIDC, no stored key). Current version is **13.5.0-preview.10** (`Directory.Build.props`). Pinned Aspire.Hosting **13.5.0** / `net10.0`.

## Restore from nuget.org

No extra feed or PAT. Add the packages with `--prerelease` (or pin the version in `PackageReference` as below):

```bash
dotnet add package IntrepidDeveloper.Aspire.Hosting.Railway --prerelease
dotnet add package IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL --prerelease
dotnet add package IntrepidDeveloper.Aspire.Hosting.Railway.Redis --prerelease
dotnet add package IntrepidDeveloper.Aspire.Hosting.Railway.Storage --prerelease
dotnet add package IntrepidDeveloper.Aspire.Railway.Storage --prerelease
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

Extension methods live in `Aspire.Hosting`, so AppHosts need no extra `using` for `AddRailwayEnvironment` / `PublishAsRailway*`. Resource types (`RailwayRegion`, `RailwayRestartPolicy`, `RailwayServiceResource`) live in `Aspire.Hosting.Railway` / `.PostgreSQL` / `.Redis` / `.Storage`.

Use official resource types where they exist. Postgres and Redis stay `AddPostgres` / `AddRedis`; `PublishAsRailway*` only changes deploy. Railway replicas cannot be used with [volumes](https://docs.railway.com/volumes/reference), so those templates are not scaled. Buckets are `AddRailwayBucket` in the AppHost and `AddRailwayBucketClient` (`IAmazonS3`) in the consuming project. The AppHost also needs the official `Aspire.Hosting.PostgreSQL` and `Aspire.Hosting.Redis` packages for `AddPostgres` / `AddRedis`.

```xml
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway" Version="13.5.0-preview.10" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL" Version="13.5.0-preview.10" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.Redis" Version="13.5.0-preview.10" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.Storage" Version="13.5.0-preview.10" />
```

```csharp
using Aspire.Hosting.Railway;

var builder = DistributedApplication.CreateBuilder(args);

var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io", "intrepid-developer/playground");
var railway = builder.AddRailwayEnvironment("railway")
    .WithContainerRegistry(ghcr);

var db = builder.AddPostgres("postgres").PublishAsRailwayPostgres();
var cache = builder.AddRedis("redis").PublishAsRailwayRedis();
var uploads = builder.AddRailwayBucket("uploads");

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
        s.Region = RailwayRegion.UsWest2;
        s.Cpu = 1;
        s.MemoryGb = 2;
        s.HealthcheckTimeoutSeconds = 120;
        s.RestartPolicy = RailwayRestartPolicy.OnFailure;
        s.RestartPolicyMaxRetries = 10;
        s.CustomDomains.Add("api.example.com");
    });

builder.Build().Run();
```

`AddContainerRegistry` + `WithContainerRegistry` is required for image deploy. Pass the GHCR namespace as the third argument (`<owner>/<repository>`). The two-argument form has no owner/repo, so Aspire would push `ghcr.io/api` and GHCR rejects it. Railway has no image registry. Aspire currently marks those APIs experimental (`ASPIRECOMPUTE003`); the playground AppHost suppresses that diagnostic so the sample still compiles with warnings-as-errors. Local `aspire run` still works without talking to Railway.

In the API / consuming project, add the storage client plus the usual Aspire Npgsql and Redis clients:

```xml
<PackageReference Include="IntrepidDeveloper.Aspire.Railway.Storage" Version="13.5.0-preview.10" />
<PackageReference Include="Aspire.Npgsql" Version="13.5.0" />
<PackageReference Include="Aspire.StackExchange.Redis" Version="13.5.0" />
```

```csharp
builder.AddNpgsqlDataSource("postgres");
builder.AddRedisClient("redis");
builder.AddRailwayBucketClient("uploads");
```

Existing Aspire client packages keep working. `AddRailwayEnvironment` is the Railway **project** (compute environment). The Railway environment name is mapped from Aspire `--environment`: Production → `production`, Staging → `staging` (lowercase). Override with `WithRailwayEnvironmentName`.

Replica count is Aspire-core `WithReplicas` (project resources). Implicit compute on the Railway environment picks it up and deploy sends `numReplicas`. Deploy healthcheck path is Aspire-core `WithHttpHealthCheck` — publish copies that path into `healthcheckPath` and deploy sends it on the existing `serviceInstanceUpdate`. Railway probes until HTTP 200, then flips traffic ([healthchecks](https://docs.railway.com/deployments/healthchecks)); it is not continuous monitoring. Region, `sleepApplication`, per-replica CPU/RAM, healthcheck timeout, restart policy, start command, pre-deploy command, deployment teardown, cron schedule, and custom hostnames are Railway-specific and use `PublishAsRailwayService`. Aspire.Hosting 13.5.0 has no `WithCpu` / `WithMemory` / healthcheck-timeout / restart-policy / start-command / overlap / drain / cron / custom-domain annotation. Aspire `WithArgs` is not mapped to Railway start. There is no GraphQL field named `serverless`; sleep applies to all replicas. `Cpu` / `MemoryGb` map to GraphQL `vCPUs` / `memoryGB` (not config-as-code `memoryBytes`). `HealthcheckTimeoutSeconds` maps to GraphQL `healthcheckTimeout` (Int seconds); omit it to leave Railway's default (300). `RestartPolicy` (`RailwayRestartPolicy`) maps to GraphQL `restartPolicyType` (`ON_FAILURE` / `ALWAYS` / `NEVER`); `RestartPolicyMaxRetries` maps to `restartPolicyMaxRetries`. Either field can be set alone. Omit both to leave Railway's default (On Failure / 10 retries). See [restart policy](https://docs.railway.com/deployments/restart-policy). `StartCommand` maps to GraphQL `startCommand`; unset leaves the image ENTRYPOINT/CMD. Image/Dockerfile start is exec form — wrap `$PORT` as `/bin/sh -c "exec … $PORT"`. See [start command](https://docs.railway.com/guides/start-command). `PreDeployCommand` maps to GraphQL `preDeployCommand` as a one-element array. It runs between build and deploy (migrations) on the private network with the app environment; a non-zero exit is not retried and the deploy stops. The step is a separate container with no volume, so the filesystem does not persist. See [pre-deploy command](https://docs.railway.com/deployments/pre-deploy-command). Either start or pre-deploy can be set alone. Empty or whitespace fails. `OverlapSeconds` / `DrainingSeconds` map to GraphQL `overlapSeconds` / `drainingSeconds` (Int). After the new deploy is active, the previous replica stays up for the overlap, then SIGTERM, then SIGKILL after the drain. This is in-deploy cutover, not `aspire destroy`. Either field can be set alone. Values must be greater than or equal to 0 (0 is no wait / immediate kill). See [deployment teardown](https://docs.railway.com/guides/deployment-teardown). `CronSchedule` maps to GraphQL `cronSchedule`. Unset leaves an always-on service. Five-field crontab, UTC, minimum every 5 minutes. The service must exit; if it is still running at the next tick, Railway skips. Wrong fit for always-on HTTP APIs. Combining cron with replicas greater than 1 or `Serverless = true` fails. See [cron jobs](https://docs.railway.com/cron-jobs). `CustomDomains` is a list of hostname strings. Requires `WithExternalHttpEndpoints()`. Publish writes `customDomains` (no tokens). Deploy creates or adopts via confirmed `customDomainCreate` / `domains` (live schema 2026-08-20). The deploy report prints CNAME/ALIAS-style routing records plus the verification TXT; missing TXT returns 404 even if CNAME resolves. Railway issues Let's Encrypt after verify. See [working with domains](https://docs.railway.com/networking/domains/working-with-domains). This integration does not talk to your DNS provider. TCP proxies are out of scope. Destroy of domains is a later slice. These fields were confirmed on the live schema 2026-08-20. Config-as-code `deploy.startCommand` / `deploy.preDeployCommand` / `deploy.overlapSeconds` / `deploy.drainingSeconds` / `deploy.cronSchedule` and the `RAILWAY_DEPLOYMENT_*` variables are mapping only. Replicas, CPU/RAM limits, healthcheck, restart-policy, start-command, pre-deploy, teardown, cron, and custom-domain fields cannot be used with Railway volumes — do not set them on `PublishAsRailwayPostgres` / `PublishAsRailwayRedis`. Allow `healthcheck.railway.app` if the app filters Host. The app must listen on `PORT`. Volume-backed services still have cutover downtime even with a healthcheck.

```csharp
builder.AddProject<Projects.Api>("api")
    .WithReplicas(2)
    .WithHttpHealthCheck("/health")
    .WithComputeEnvironment(railway)
    .PublishAsRailwayService(s =>
    {
        s.Region = RailwayRegion.EuropeWest4;
        s.Cpu = 1;
        s.MemoryGb = 2;
        s.Serverless = true;
        s.HealthcheckTimeoutSeconds = 120;
        s.RestartPolicy = RailwayRestartPolicy.OnFailure;
        s.RestartPolicyMaxRetries = 10;
        s.StartCommand = "/bin/sh -c \"exec dotnet MyApp.dll --urls http://*:$PORT\"";
        s.PreDeployCommand = "dotnet MyApp.dll --migrate";
        s.OverlapSeconds = 60;
        s.DrainingSeconds = 10;
        s.CustomDomains.Add("api.example.com");
    });

builder.AddProject<Projects.Worker>("nightly")
    .PublishAsRailwayService(s =>
    {
        s.CronSchedule = "0 3 * * *"; // 03:00 UTC
    });
```

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

Publish writes `railway-plan.json` plus a `.env.example` of captured parameter names. It does not call Railway. Secrets stay out of the plan only when they are Aspire parameters (`AddParameter(secret: true)`). `WithEnvironment("API_KEY", value)` string literals are written as-is.

Deploy resolves the token, applies the plan over GraphQL, persists Railway ids, and reports real progress or failures. Image-based services need `IContainerRegistry` (GHCR or Docker Hub). Missing registry fails with a message to add one. This integration does not run `railway up`.

See [publish-and-deploy.md](publish-and-deploy.md) for pipeline steps, adopt, staging, and image resolution.
