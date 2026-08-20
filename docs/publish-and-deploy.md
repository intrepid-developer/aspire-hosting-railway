# Publish and deploy

This integration uses Aspire 13.5 compute-environment + pipeline hooks (`IComputeEnvironmentResource`, `PipelineStepAnnotation`, `WellKnownPipelineSteps`). It does **not** use the obsolete `IDistributedApplicationPublisher` / `DeployingCallbackAnnotation` model.

## Pipeline steps

Per Railway environment resource named `{name}`:

| Step | What it does |
| --- | --- |
| `prepare-deployment-targets-{name}` | Materializes `RailwayServiceResource` children and `DeploymentTargetAnnotation`. Depends on `ValidateComputeEnvironments`; required by `BeforeStart`. |
| `publish-{name}` | Writes `railway-plan.json` plus a `.env.example` of captured parameter names. Required by the well-known `Publish` step. Parameter names and Railway expressions stay secret-safe; `WithEnvironment` string literals are written as-is. |
| `deploy-{name}` | Resolves the account/workspace token, applies the plan over GraphQL, persists ids, reports real progress or failures. Depends on `DeployPrereq` and `publish-{name}`. Image push steps run before this when the model has build-and-push resources. |
| `destroy-{name}` | Stub. Warns that teardown is not implemented. Confirmed operations do not include project or environment delete. |

A `validate-railway` step (registered once) fails publish-mode apps that call `PublishAsRailway*` / `AddRailwayBucket` without `AddRailwayEnvironment`.

## Plan vs apply

| | Publish (`RailwayPlanBuilder`) | Deploy (`RailwayGraphQLApplyService`) |
| --- | --- | --- |
| Network | None | Railway GraphQL v2 |
| Secrets | Stay out of the plan only when modeled as Aspire parameters (`AddParameter(secret: true)`). `WithEnvironment("API_KEY", value)` literals are written as-is | Token and resolved parameter values in memory only |
| Output | `railway-plan.json`, `.env.example` | Created or adopted Railway ids |

The plan is not unconditionally secret-safe. `CaptureEnvironmentValue` writes string literals from `WithEnvironment("API_KEY", value)` into `railway-plan.json` as-is. Secrets stay out of the plan only when they are Aspire parameters (`AddParameter(secret: true)`), which are stored as parameter **names**. Railway `${{service.VAR}}` expressions, the deploy token, and bucket credentials from `bucketS3Credentials` are not written to the plan. Deploy fills `RailwayApplyRequest.ResolvedServiceEnvironment` from Aspire parameters and connection strings, then upserts variables. An empty optional captured parameter is omitted instead of aborting deploy. A missing required parameter still fails.

`WithReference` on official Railway databases emits expressions such as `${{postgres.DATABASE_URL}}` (private) onto services that actually referenced the database — never the local Docker connection string. Non-Railway connection strings (for example another Aspire connection-string resource) are captured as secret parameter **names** in the plan and resolved on deploy.

Host addresses are host-only: `{service}.railway.internal` (lowercase). Endpoints and secrets are never concatenated into strings before Aspire resolves them.

Official DBs are created via `template(code: "postgres"|"redis")` then `templateDeployV2` with the fetched `templateId` and `serializedConfig` (never empty, never invented template UUIDs). Apply polls `workflowStatus` and fails if `workflowId` is missing.

## Adopt existing

```csharp
builder.AddRailwayEnvironment("railway").AsExisting();
// or pass parameters bound from RAILWAY_PROJECT_ID / RAILWAY_ENVIRONMENT_ID
```

`AsExisting()` binds `railway-project-id` / `railway-environment-id` from `RAILWAY_PROJECT_ID` / `RAILWAY_ENVIRONMENT_ID`. Both ids are required when adopt is set.

On adopt, and on later applies against an existing project id, apply lists `project.services` and `project.buckets` (the documented `project(id)` query — `buckets` is a field on that same confirmed operation, not a new query). Service names match case-insensitively (`Postgres` / `postgres`, `api`). Matching services skip `templateDeployV2` and `serviceCreate`; apply continues with `serviceInstanceUpdate`, variable upsert, and deploy.

Planned `Kind = bucket` resources match `project.buckets` by display name (case-insensitive). A match records the bucket id in `BucketIds` and skips `bucketCreate`. `bucketCreate` runs only when no matching bucket exists. A same-name service is not a bucket id and is never passed to `bucketS3Credentials`. After a real `bucketCreate`, apply retries `bucketS3Credentials` until a `BucketInstance` exists. Flatten-safe local state still stores bucket **ids** (not S3 secrets) so a local retry can skip create; CI / a new machine without that file relies on name-based adopt.

Re-deploy does not create a second project.

## Staging

If a `staging` environment does not exist on deploy, the default is to duplicate production (`environmentCreate` with `sourceEnvironmentId`) when production exists. Empty create is opt-in:

```csharp
builder.AddRailwayEnvironment("railway")
    .WithProperties(env => env.CreateEmptyEnvironment = true);
```

`DuplicateProductionWhenCreatingStaging` defaults to `true` on `RailwayEnvironmentResource`. Creating staging without a known production environment id fails unless you deploy production first or opt into `CreateEmptyEnvironment`.

`EnsureEnvironmentAsync` treats `AdoptedEnvironmentId` (`railway-environment-id`) as the **target** environment, not a production source for duplication. Passing the production id on a staging deploy applies that deploy onto production. Adopt with `railway-environment-id` only when that environment already exists.

PR / ephemeral Railway environment APIs are not part of this release.

## Flatten-safe deployment state

Project, environment, service, bucket, and template ids are persisted in `IDeploymentStateManager` under `Railway:{computeEnvironmentName}`. Aspire's file state manager flattens with colon keys and does **not** round-trip JSON arrays. This integration therefore stores maps as JSON objects (for example template codes as `{ "postgres": "postgres" }`), not arrays.

A legacy `AppliedTemplateCodes` key that stored a JSON array string such as `["postgres"]` is still read and migrated on load. Preview.4 never read that key. Tokens and bucket secrets are never written to state.

## Image resolution

Railway has **no image registry**. Deploy of image-based services requires `IContainerRegistry` on the model:

```csharp
var ghcr = builder.AddContainerRegistry("ghcr", "ghcr.io", "intrepid-developer/playground");
var railway = builder.AddRailwayEnvironment("railway")
    .WithContainerRegistry(ghcr);
```

Pass the GHCR namespace as the third argument (`<owner>/<repository>`). The two-argument form has no owner/repo, so Aspire would push `ghcr.io/api` and GHCR rejects it.

If the registry is missing, deploy throws and tells you to add GHCR or Docker Hub. This integration does not shell out to `railway up`. Railpack has no .NET support; use an image or a Dockerfile.

Resolution order (`RailwayEnvironmentResource.ResolveDeployImageAsync`):

1. `ContainerImagePushOptions` + `IContainerRegistry` — `GetFullRemoteImageNameAsync` after push-option callbacks. This is what Aspire project resources need: they have no `ContainerImageAnnotation`, so the plan keeps a `{name.containerImage}` placeholder.
2. `TryGetContainerImageName` when that already looks like a real image (not a `{…}` placeholder).
3. The plan image, if it is already resolved.

`resolveContainerRegistry` uses `WithContainerRegistry` when present, otherwise the single `IContainerRegistry` in the model.
