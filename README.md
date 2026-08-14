# Aspire Hosting for Railway

Open-source Aspire hosting integrations that let you `aspire publish` and `aspire deploy` a distributed app to [Railway](https://railway.com).

This is the `IntrepidDeveloper.Aspire.Hosting.Railway` package family. Locally you keep the normal Aspire resource model. On publish/deploy, the Railway compute environment provisions (or adopts) a Railway project and environment, deploys your services, and wires Railway-backed databases.

## Packages (first release)

| Package | Role |
| --- | --- |
| `IntrepidDeveloper.Aspire.Hosting.Railway` | Compute environment: project, environment, services, variables, domains, volumes |
| `IntrepidDeveloper.Aspire.Hosting.Railway.PostgreSQL` | Local Postgres container; Railway Postgres template on deploy |
| `IntrepidDeveloper.Aspire.Hosting.Railway.Redis` | Local Redis container; Railway Redis template on deploy |

Later: MySQL, MongoDB, HA / PgBouncer, storage buckets, cron, PR environment clones.

## Intended AppHost usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddRailwayEnvironment("railway");

var db = builder.AddPostgres("postgres")
    .PublishAsRailwayPostgres();

var cache = builder.AddRedis("redis")
    .PublishAsRailwayRedis();

builder.AddProject<Projects.Api>("api")
    .WithReference(db)
    .WithReference(cache)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

Auth is an Aspire parameter (`RAILWAY_TOKEN` / account or workspace token). The integration talks to Railway's GraphQL API at `https://backboard.railway.com/graphql/v2`.

## Status

Early scaffolding. APIs and package layout may change before the first NuGet release.

## License

MIT
