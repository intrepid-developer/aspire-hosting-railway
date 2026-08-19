# Publish and deploy

This integration uses Aspire 13.4 compute-environment + pipeline hooks (`IComputeEnvironmentResource`, `PipelineStepAnnotation`, `WellKnownPipelineSteps`). It does **not** use the obsolete `IDistributedApplicationPublisher` / `DeployingCallbackAnnotation` model.

## Pipeline steps

Per Railway environment resource named `{name}`:

| Step | What it does |
| --- | --- |
| `prepare-deployment-targets-{name}` | Materializes `RailwayServiceResource` children and `DeploymentTargetAnnotation`. Depends on `ValidateComputeEnvironments`; required by `BeforeStart`. |
| `publish-{name}` | Writes `railway-plan.json` (expressions and parameter **names** only) plus a `.env.example` of captured parameter names. Required by the well-known `Publish` step. |
| `deploy-{name}` | Resolves the account/workspace token, applies the plan over GraphQL, persists ids, reports real progress or failures. Depends on `DeployPrereq` and `publish-{name}`. Image push steps run before this when the model has build-and-push resources. |
| `destroy-{name}` | Stub. Warns that teardown is not implemented. Confirmed operations do not include project or environment delete. |

A `validate-railway` step (registered once) fails publish-mode apps that call `PublishAsRailway*` / `AddRailwayBucket` without `AddRailwayEnvironment`.

## Plan vs apply

| | Publish (`RailwayPlanBuilder`) | Deploy (`RailwayGraphQLApplyService`) |
| --- | --- | --- |
| Network | None | Railway GraphQL v2 |
| Secrets | Parameter names and `${{service.VAR}}` expressions | Token and resolved values in memory only |
| Output | `railway-plan.json`, `.env.example` | Created or adopted Railway ids |

The plan never contains resolved tokens, passwords, or bucket credentials. Deploy fills `RailwayApplyRequest.ResolvedServiceEnvironment` from Aspire parameters and connection strings, then upserts variables. An empty optional captured parameter is omitted instead of aborting deploy. A missing required parameter still fails.

`WithReference` on official Railway databases emits expressions such as `${{postgres.DATABASE_URL}}` (private) onto services that actually referenced the database — never the local Docker connection string. Non-Railway connection strings (for example another Aspire connection-string resource) are captured as secret parameter **names** in the plan and resolved on deploy.

Host addresses are host-only: `{service}.railway.internal` (lowercase). Endpoints and secrets are never concatenated into strings before Aspire resolves them.

Official DBs are created via `template(code: "postgres"|"redis")` then `templateDeployV2` with the fetched `templateId` and `serializedConfig` (never empty, never invented template UUIDs). Apply polls `workflowStatus` and fails if `workflowId` is missing.

## Adopt existing

```csharp
builder.AddRailwayEnvironment("railway").AsExisting();
// or pass parameters bound from RAILWAY_PROJECT_ID / RAILWAY_ENVIRONMENT_ID
```

`AsExisting()` binds `railway-project-id` / `railway-environment-id` from `RAILWAY_PROJECT_ID` / `RAILWAY_ENVIRONMENT_ID`. Both ids are required when adopt is set.

On adopt, and on later applies against an existing project id, apply lists `project.services` (the documented `project(id)` query) and matches names case-insensitively (`Postgres` / `postgres`, `api`, `uploads` when it appears as a service). Matching services skip `templateDeployV2` and `serviceCreate`; apply continues with `serviceInstanceUpdate`, variable upsert, and deploy. Bucket create is skipped when flatten-safe local state already has that bucket id.

Re-deploy does not create a second project.

## Staging

If a `staging` environment does not exist on deploy, the default is to duplicate production (`environmentCreate` with `sourceEnvironmentId`) when production exists. Empty create is opt-in:

```csharp
builder.AddRailwayEnvironment("railway")
    .WithProperties(env => env.CreateEmptyEnvironment = true);
```

`DuplicateProductionWhenCreatingStaging` defaults to `true` on `RailwayEnvironmentResource`. Creating staging without a known production environment id fails unless you adopt with `railway-environment-id`, deploy production first, or opt into `CreateEmptyEnvironment`.

PR / ephemeral Railway environment APIs are not part of this release.

## Flatten-safe deployment state

Project, environment, service, bucket, and template ids are persisted in `IDeploymentStateManager` under `Railway:{computeEnvironmentName}`. Aspire's file state manager flattens with colon keys and does **not** round-trip JSON arrays. This integration therefore stores maps as JSON objects (for example template codes as `{ "postgres": "postgres" }`), not arrays.

A legacy `AppliedTemplateCodes` key that stored a JSON array string such as `["postgres"]` is still read and migrated on load. Preview.4 never read that key. Tokens and bucket secrets are never written to state.

## Image resolution

Railway has **no image registry**. Deploy of image-based services requires `IContainerRegistry` on the model:

```csharp
var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io");
var railway = builder.AddRailwayEnvironment("railway")
    .WithContainerRegistry(ghcr);
```

If the registry is missing, deploy throws and tells you to add GHCR or Docker Hub. This integration does not shell out to `railway up`. Railpack has no .NET support; use an image or a Dockerfile.

Resolution order (`RailwayEnvironmentResource.ResolveDeployImageAsync`):

1. `ContainerImagePushOptions` + `IContainerRegistry` — `GetFullRemoteImageNameAsync` after push-option callbacks. This is what Aspire project resources need: they have no `ContainerImageAnnotation`, so the plan keeps a `{name.containerImage}` placeholder.
2. `TryGetContainerImageName` when that already looks like a real image (not a `{…}` placeholder).
3. The plan image, if it is already resolved.

`resolveContainerRegistry` uses `WithContainerRegistry` when present, otherwise the single `IContainerRegistry` in the model.
