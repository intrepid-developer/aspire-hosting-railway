# Aspire Hosting for Railway

Open-source Aspire hosting integrations that let you `aspire publish` and `aspire deploy` a distributed app to [Railway](https://railway.com).

This is the `IntrepidDeveloper.Aspire.Hosting.Railway` package family. Locally you keep the normal Aspire resource model. On publish/deploy, the Railway compute environment provisions (or adopts) a Railway project and environment, deploys your services, and wires Railway-backed databases and buckets.

These are first-class Aspire integrations: official resource types where they exist, standard connection strings and properties, `WithReference`, `WaitFor`, health checks, and the dashboard. Existing Aspire client packages keep working. Local `aspire run` never needs a Railway token.

## Packages (first release)

| Package | Role |
| --- | --- |
| `IntrepidDeveloper.Aspire.Hosting.Railway` | Compute environment: project, environment, services, variables, domains, volumes |
| `IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL` | `AddPostgres` locally; Railway Postgres template on deploy |
| `IntrepidDeveloper.Aspire.Hosting.Railway.Redis` | `AddRedis` locally; Railway Redis template on deploy |
| `IntrepidDeveloper.Aspire.Hosting.Railway.Storage` | Local S3-compatible container; Railway bucket on deploy |
| `IntrepidDeveloper.Aspire.Railway.Storage` | Client: `AddRailwayBucketClient` registers `IAmazonS3` |

Later: MySQL, MongoDB, HA / PgBouncer, cron, PR environment clones.

## Intended AppHost usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddRailwayEnvironment("railway");

var db = builder.AddPostgres("postgres")
    .PublishAsRailwayPostgres();

var cache = builder.AddRedis("redis")
    .PublishAsRailwayRedis();

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

Auth is an Aspire parameter (`RAILWAY_TOKEN` / account or workspace token) used only for publish/deploy. The integration talks to Railway's GraphQL API at `https://backboard.railway.com/graphql/v2`.

## Secrets

This repo is public. Do not commit tokens, `.env` files, user-secrets, or NuGet API keys.

- Copy `.env.example` to a local `.env` (gitignored)
- Pass `RAILWAY_TOKEN` as an Aspire parameter or environment variable on the machine that deploys
- PRs run Gitleaks; GitHub secret scanning and push protection are on

## Status

Early scaffolding. APIs and package layout may change before the first NuGet release.

## License

MIT
