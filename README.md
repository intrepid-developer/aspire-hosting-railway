# Aspire Hosting for Railway

[![NuGet](https://img.shields.io/nuget/vpre/IntrepidDeveloper.Aspire.Hosting.Railway.svg?label=nuget)](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Aspire 13.5 hosting so `aspire publish` and `aspire deploy` provision [Railway](https://railway.com).

Locally you keep the normal Aspire resource model: official Postgres and Redis, `WithReference`, `WaitFor`, health checks, and the dashboard. Publish writes `railway-plan.json`. Deploy talks to Railway over GraphQL. `aspire run` never needs a Railway token and never calls Railway.

## Status

Preview on [nuget.org](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway). Pack also publishes a GitHub Release and [GitHub Packages](https://nuget.pkg.github.com/intrepid-developer/index.json). nuget.org uses Trusted Publishing (OIDC, no stored key). Current version: **13.5.0-preview.11** (from `Directory.Build.props`). MIT. Pinned to Aspire.Hosting **13.5.0** / `net10.0`. See [CHANGELOG.md](CHANGELOG.md).

## Packages

| Package | Role |
| --- | --- |
| [`IntrepidDeveloper.Aspire.Hosting.Railway`](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway) | Compute environment, pipeline, GraphQL client, `PublishAsRailwayService` |
| [`IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL`](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL) | `AddPostgres` locally; `PublishAsRailwayPostgres` on deploy |
| [`IntrepidDeveloper.Aspire.Hosting.Railway.Redis`](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway.Redis) | `AddRedis` locally; `PublishAsRailwayRedis` on deploy |
| [`IntrepidDeveloper.Aspire.Hosting.Railway.Storage`](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Hosting.Railway.Storage) | `AddRailwayBucket`: local S3-compatible container; Railway bucket on deploy |
| [`IntrepidDeveloper.Aspire.Railway.Storage`](https://www.nuget.org/packages/IntrepidDeveloper.Aspire.Railway.Storage) | Client: `AddRailwayBucketClient` registers `IAmazonS3` |

AppHost extensions live in `Aspire.Hosting`. Resource types live in `Aspire.Hosting.Railway` / `.PostgreSQL` / `.Redis` / `.Storage`. Postgres and Redis templates are volume-backed; Railway [replicas cannot be used with volumes](https://docs.railway.com/volumes/reference).

## Quick start

```csharp
using Aspire.Hosting.Railway;

var builder = DistributedApplication.CreateBuilder(args);

var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io", "intrepid-developer/playground");
var railway = builder.AddRailwayEnvironment("railway")
    .WithContainerRegistry(ghcr);

var db = builder.AddPostgres("postgres").PublishAsRailwayPostgres(s =>
{
    s.VolumeBackupDaily = true;
    s.VolumeBackupWeekly = true;
});
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
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway" Version="13.5.0-preview.11" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL" Version="13.5.0-preview.11" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.Redis" Version="13.5.0-preview.11" />
<PackageReference Include="IntrepidDeveloper.Aspire.Hosting.Railway.Storage" Version="13.5.0-preview.11" />
```

API / consuming project (`AddRailwayBucketClient` plus the usual Aspire clients):

```xml
<PackageReference Include="IntrepidDeveloper.Aspire.Railway.Storage" Version="13.5.0-preview.11" />
<PackageReference Include="Aspire.Npgsql" Version="13.5.0" />
<PackageReference Include="Aspire.StackExchange.Redis" Version="13.5.0" />
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
- Scale uses Aspire [`WithReplicas`](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/annotations-overview) → Railway `numReplicas` (single-region) or `multiRegionConfig` (region / multi-region). Never both. Region is `RailwayRegion` (`UsWest2`, `UsEast4`, `EuropeWest4`, `AsiaSoutheast1`); GraphQL gets the official deploy keys (`us-west2`, `us-east4-eqdc4a`, `europe-west4-drams3a`, `asia-southeast1-eqsg3a`). Airport codes and older ids are not members. Max 50 replicas. Replicas cannot be used with volumes (Postgres / Redis templates). Sleep-when-idle is `sleepApplication` (no GraphQL field named `serverless`). Per-replica CPU and RAM are Railway-specific (`PublishAsRailwayService` `Cpu` / `MemoryGb`) and apply via `serviceInstanceLimitsUpdate` (`vCPUs` / `memoryGB`). Deploy healthcheck path is Aspire-core [`WithHttpHealthCheck`](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/health-checks) → Railway `healthcheckPath`. Timeout is Railway-specific (`PublishAsRailwayService` `HealthcheckTimeoutSeconds` → `healthcheckTimeout`). Railway probes until HTTP 200, then flips traffic ([healthchecks](https://docs.railway.com/deployments/healthchecks)). It is not continuous monitoring. Origin host is `healthcheck.railway.app`. Volume-backed services still have cutover downtime. Restart policy is Railway-specific (`PublishAsRailwayService` `RestartPolicy` / `RestartPolicyMaxRetries` → `restartPolicyType` / `restartPolicyMaxRetries`). Unset leaves Railway's default (On Failure / 10 retries). See [restart policy](https://docs.railway.com/deployments/restart-policy). Start and pre-deploy commands are Railway-specific (`PublishAsRailwayService` `StartCommand` / `PreDeployCommand` → `startCommand` / `preDeployCommand`). Unset leaves the image ENTRYPOINT/CMD. Aspire `WithArgs` is not mapped. Image/Dockerfile start is exec form — wrap `$PORT` as `/bin/sh -c "exec … $PORT"`. Pre-deploy runs between build and deploy, cannot touch volumes, and a non-zero exit stops the deploy. See [start command](https://docs.railway.com/guides/start-command) and [pre-deploy command](https://docs.railway.com/deployments/pre-deploy-command). Deployment teardown is Railway-specific (`PublishAsRailwayService` `OverlapSeconds` / `DrainingSeconds` → `overlapSeconds` / `drainingSeconds`). After the new deploy is active, the previous replica stays up for the overlap, then SIGTERM, then SIGKILL after the drain. This is in-deploy cutover, not `aspire destroy`. See [deployment teardown](https://docs.railway.com/guides/deployment-teardown). Cron is Railway-specific (`PublishAsRailwayService` `CronSchedule` → `cronSchedule`). Unset leaves an always-on service. Five-field crontab, UTC, minimum every 5 minutes. The service must exit; if it is still running at the next tick, Railway skips. Wrong fit for always-on HTTP APIs. See [cron jobs](https://docs.railway.com/cron-jobs). Custom hostnames are Railway-specific (`PublishAsRailwayService` `CustomDomains` → `customDomainCreate` / adopt via `domains`). Requires `WithExternalHttpEndpoints()`. Deploy reports DNS records plus the verification TXT; missing TXT returns 404 even if CNAME resolves. Railway issues Let's Encrypt after verify. See [working with domains](https://docs.railway.com/networking/domains/working-with-domains). Official Postgres volume backup schedules are Railway-specific (`PublishAsRailwayPostgres` `VolumeBackupDaily` / `VolumeBackupWeekly` / `VolumeBackupMonthly` → confirmed `volumeInstanceBackupScheduleUpdate`). Unset leaves the dashboard as-is. See [volume backups](https://docs.railway.com/volumes/backups). PITR enable is HA-only on the live schema (`enablePitrForHaCluster`) and is not in this slice.
- This integration does not shell out to `railway up`. Railpack has no .NET support; use an image or a Dockerfile.
- `destroy-{name}` is a stub for project/environment teardown. Confirmed GraphQL operations do not include project or environment delete. It is not the same as in-deploy overlap/drain.
- PR / ephemeral Railway environments are not in this release.
- MySQL, MongoDB, and HA / PgBouncer are later.
- Railway buckets are **private**. Use S3 credentials or presigned URLs. They are not on private DNS.

Replica count is Aspire-core `WithReplicas`. Deploy healthcheck path is Aspire-core `WithHttpHealthCheck`. Public HTTP is Aspire-core `WithExternalHttpEndpoints`. Railway region, `sleepApplication`, per-replica CPU/RAM, healthcheck timeout, restart policy, start command, pre-deploy command, deployment teardown, cron schedule, and custom hostnames are set on the materialized service. Official Postgres volume backup schedules are set on `PublishAsRailwayPostgres`. Aspire.Hosting 13.5.0 has no `WithCpu` / `WithMemory` / healthcheck-timeout / restart-policy / start-command / overlap / drain / cron / custom-domain annotation. `WithArgs` is not mapped to Railway start.

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

builder.AddProject<Projects.Api>("api")
    .WithComputeEnvironment(railway)
    .PublishAsRailwayService(s =>
    {
        s.ReplicaRegions = new()
        {
            [RailwayRegion.UsWest2] = 2,
            [RailwayRegion.EuropeWest4] = 1
        };
        s.Serverless = false;
    });
```

`RailwayRegion` (`Aspire.Hosting.Railway`) is the closed AppHost type: `UsWest2` → `us-west2`, `UsEast4` → `us-east4-eqdc4a`, `EuropeWest4` → `europe-west4-drams3a`, `AsiaSoutheast1` → `asia-southeast1-eqsg3a`. Airport codes (`sjc`, `iad`, `ams`, `sin`) and older ids (`us-west1`, `us-east4`, `europe-west4`) are not members. GraphQL and `railway-plan.json` still use those official `Region.region` strings. When `ReplicaRegions` is set, it wins over `WithReplicas` + `Region` and deploy sends `multiRegionConfig` only. `WithReplicas` alone sends `numReplicas` for the service's current Railway region. `Serverless` writes `sleepApplication` for every replica of that service. `Cpu` / `MemoryGb` write `vCPUs` / `memoryGB` on `serviceInstanceLimitsUpdate` (after `serviceInstanceUpdate`) and apply to each replica. Values must be greater than 0; Railway plan caps are not hardcoded. `WithHttpHealthCheck` writes `healthcheckPath` on the same `serviceInstanceUpdate` as image/scale. `HealthcheckTimeoutSeconds` writes `healthcheckTimeout` (Int seconds) on that mutation; unset omits the field (Railway default 300). `RestartPolicy` (`RailwayRestartPolicy`) writes `restartPolicyType` (`ON_FAILURE` / `ALWAYS` / `NEVER`) on that same mutation. `RestartPolicyMaxRetries` writes `restartPolicyMaxRetries` (Int); either field can be set alone, and unset omits both so Railway's default (On Failure / 10 retries) applies. Free/trial caps are not hardcoded. With multiple replicas, only the crashed replica restarts ([restart policy](https://docs.railway.com/deployments/restart-policy)). `StartCommand` writes `startCommand` (String) on that same mutation; unset omits it so the image ENTRYPOINT/CMD applies. On the image/Dockerfile path this is exec form — no shell expansion unless wrapped as `/bin/sh -c "exec … $PORT"` ([start command](https://docs.railway.com/guides/start-command)). `PreDeployCommand` writes `preDeployCommand` as a one-element `[String!]` array (migrations). It runs between build and deploy on the private network with the app environment; a non-zero exit is not retried and the deploy stops. The step is a separate container with no volume, so the filesystem does not persist ([pre-deploy command](https://docs.railway.com/deployments/pre-deploy-command)). Either start or pre-deploy can be set alone. Empty or whitespace fails. `OverlapSeconds` writes `overlapSeconds` (Int) on that same mutation; `DrainingSeconds` writes `drainingSeconds` (Int). After the new deploy is active, the previous replica stays up for the overlap, then SIGTERM, then SIGKILL after the drain ([deployment teardown](https://docs.railway.com/guides/deployment-teardown)). Either field can be set alone. Values must be greater than or equal to 0 (0 is no wait / immediate kill). This is in-deploy cutover, not `aspire destroy`. `CronSchedule` writes `cronSchedule` (String) on that same mutation; unset omits it so the service stays always-on. Five-field crontab only, UTC. Railway's minimum frequency is every 5 minutes (`* * * * *` and minute-field `*/1` through `*/4` fail). Timezone names are not converted. The service starts, runs the start command, and must exit. If it is still running at the next tick, Railway skips and does not kill the previous run ([cron jobs](https://docs.railway.com/cron-jobs), [cron workers and queues](https://docs.railway.com/guides/cron-workers-queues)). Wrong fit for always-on HTTP APIs and bots; HTTP healthchecks are a poor fit but are not auto-blocked. Combining cron with replicas greater than 1 or `Serverless = true` fails. `CustomDomains` is a list of hostname strings. Publish writes them as `customDomains` (no tokens). Deploy creates-missing / adopts-existing via confirmed `domains` / `customDomainAvailable` / `customDomainCreate` (live schema 2026-08-20). Requires `WithExternalHttpEndpoints()`. The deploy report prints DNS records plus the verification TXT; missing TXT returns 404 even if CNAME resolves. Railway issues Let's Encrypt after verify ([working with domains](https://docs.railway.com/networking/domains/working-with-domains)). This integration does not talk to your DNS provider. TCP proxies are out of scope. Destroy of domains is a later slice. These input fields were confirmed on the live schema 2026-08-20. Config-as-code `deploy.startCommand` / `deploy.preDeployCommand` / `deploy.overlapSeconds` / `deploy.drainingSeconds` / `deploy.cronSchedule` and the `RAILWAY_DEPLOYMENT_*` variables are mapping only. Managed Redis / buckets do not get these fields. Official Postgres volume backup schedules use `PublishAsRailwayPostgres` (`VolumeBackupDaily` / `VolumeBackupWeekly` / `VolumeBackupMonthly`); publish writes `volumeBackupScheduleKinds` and deploy applies confirmed `volumeInstanceBackupScheduleUpdate` (live schema 2026-08-20) after resolving `environment.volumeInstances` by Postgres service id. Unset omits the field. PITR enable is HA-only on the live schema and is not in this slice. Allow `healthcheck.railway.app` if the app filters Host. The app must listen on `PORT` (Railway's probe uses that). Volume-backed services still have a cutover gap even with a healthcheck.

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
