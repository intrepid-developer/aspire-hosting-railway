#pragma warning disable ASPIREPIPELINES001
#pragma warning disable ASPIREPIPELINES002

using Aspire.Hosting.Pipelines;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Applies a secret-safe <see cref="RailwayPlan"/> to Railway using confirmed GraphQL v2 operations.
/// The pipeline calls this from <c>deploy-{name}</c>; unit tests inject a fake <see cref="HttpMessageHandler"/>.
/// </summary>
public sealed class RailwayGraphQLApplyService
{
    private readonly RailwayGraphQLClient _client;
    private readonly RailwayApplyOptions _options;

    /// <summary>
    /// Initializes the apply service.
    /// </summary>
    /// <param name="client">Typed GraphQL client. Tests pass a client backed by a fake handler.</param>
    /// <param name="options">Optional poll timeouts. Tests should use a zero poll interval.</param>
    public RailwayGraphQLApplyService(RailwayGraphQLClient client, RailwayApplyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _options = options ?? new RailwayApplyOptions();
    }

    /// <summary>
    /// Provisions or adopts a Railway project and environment, then applies services, official
    /// templates, and buckets. Persists ids so a second deploy does not create a second project.
    /// </summary>
    /// <param name="plan">Publish-time plan (expressions and parameter names only).</param>
    /// <param name="request">Resolved token, adopt ids, and images. Never written to the plan file.</param>
    /// <param name="reportingStep">Pipeline reporter used for progress and honest failures.</param>
    /// <param name="stateManager">Optional deployment state used to adopt previous ids.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created or adopted Railway ids. Never includes tokens or bucket secrets.</returns>
    public async Task<RailwayApplyResult> ApplyAsync(
        RailwayPlan plan,
        RailwayApplyRequest request,
        IReportingStep reportingStep,
        IDeploymentStateManager? stateManager,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reportingStep);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Token);

        RailwayServiceComputeSettings.ValidatePlanServices(plan);
        RailwayVolumeBackupSchedule.ValidatePlan(plan);

        var snapshot = stateManager is not null
            ? await RailwayDeploymentStateStore.LoadAsync(
                stateManager,
                plan.ComputeEnvironment,
                plan.RailwayEnvironmentName,
                cancellationToken).ConfigureAwait(false)
            : new RailwayDeploymentSnapshot();

        var (projectId, createdProject, productionFromCreate) = await EnsureProjectAsync(
            plan,
            request,
            snapshot,
            reportingStep,
            cancellationToken).ConfigureAwait(false);

        var productionEnvironmentId = FirstNonEmpty(
            snapshot.ProductionEnvironmentId,
            productionFromCreate,
            string.Equals(plan.RailwayEnvironmentName, "production", StringComparison.OrdinalIgnoreCase)
                ? FirstNonEmpty(request.AdoptedEnvironmentId, snapshot.EnvironmentId)
                : null);

        var (environmentId, createdEnvironment, duplicatedProduction) = await EnsureEnvironmentAsync(
            plan,
            request,
            snapshot,
            projectId,
            productionEnvironmentId,
            reportingStep,
            cancellationToken).ConfigureAwait(false);

        if (string.Equals(plan.RailwayEnvironmentName, "production", StringComparison.OrdinalIgnoreCase))
        {
            productionEnvironmentId = environmentId;
        }

        var result = new RailwayApplyResult
        {
            ProjectId = projectId,
            EnvironmentId = environmentId,
            ProductionEnvironmentId = productionEnvironmentId,
            CreatedProject = createdProject,
            CreatedEnvironment = createdEnvironment
        };

        foreach (var pair in snapshot.ServiceIds)
        {
            result.ServiceIds[pair.Key] = pair.Value;
        }

        foreach (var pair in snapshot.BucketIds)
        {
            result.BucketIds[pair.Key] = pair.Value;
        }

        foreach (var pair in snapshot.CustomDomainIds)
        {
            result.CustomDomainIds[pair.Key] = pair.Value;
        }

        foreach (var pair in snapshot.VolumeInstanceIds)
        {
            result.VolumeInstanceIds[pair.Key] = pair.Value;
        }

        foreach (var pair in snapshot.VolumeBackupScheduleIds)
        {
            result.VolumeBackupScheduleIds[pair.Key] = pair.Value;
        }

        result.AppliedTemplateCodes.AddRange(snapshot.TemplateCodes);

        if (duplicatedProduction)
        {
            SeedFromProduction(snapshot, result);
        }

        if (!createdProject)
        {
            await AdoptExistingProjectResourcesAsync(
                plan,
                request,
                result,
                reportingStep,
                cancellationToken).ConfigureAwait(false);
        }

        async Task PersistAsync()
        {
            if (stateManager is null)
            {
                return;
            }

            await RailwayDeploymentStateStore.SaveAsync(
                stateManager,
                plan.ComputeEnvironment,
                plan.RailwayEnvironmentName,
                result,
                cancellationToken).ConfigureAwait(false);
        }

        await PersistAsync().ConfigureAwait(false);

        await ApplyManagedTemplatesAsync(plan, request, result, reportingStep, PersistAsync, cancellationToken)
            .ConfigureAwait(false);
        await ApplyVolumeBackupSchedulesAsync(plan, request, result, reportingStep, PersistAsync, cancellationToken)
            .ConfigureAwait(false);
        await ApplyBucketsAsync(plan, request, result, reportingStep, PersistAsync, cancellationToken)
            .ConfigureAwait(false);
        await ApplyComputeServicesAsync(plan, request, result, reportingStep, PersistAsync, cancellationToken)
            .ConfigureAwait(false);
        await CommitStagedAsync(request, result, reportingStep, cancellationToken).ConfigureAwait(false);
        await PersistAsync().ConfigureAwait(false);

        return result;
    }

    private async Task<(string ProjectId, bool Created, string? ProductionEnvironmentId)> EnsureProjectAsync(
        RailwayPlan plan,
        RailwayApplyRequest request,
        RailwayDeploymentSnapshot snapshot,
        IReportingStep reportingStep,
        CancellationToken cancellationToken)
    {
        var existing = FirstNonEmpty(request.AdoptedProjectId, snapshot.ProjectId);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            var adoptTask = await reportingStep.CreateTaskAsync(
                new MarkdownString($"Adopt Railway project `{existing}`"),
                cancellationToken).ConfigureAwait(false);
            await using (adoptTask.ConfigureAwait(false))
            {
                await adoptTask.CompleteAsync(
                    "Using an existing Railway project id (adopt parameter or persisted deployment state).",
                    CompletionState.Completed,
                    cancellationToken).ConfigureAwait(false);
            }

            return (existing, false, null);
        }

        var createTask = await reportingStep.CreateTaskAsync(
            new MarkdownString($"Create Railway project **{plan.ComputeEnvironment}**"),
            cancellationToken).ConfigureAwait(false);
        await using (createTask.ConfigureAwait(false))
        {
            var response = await _client.ProjectCreateAsync(
                new ProjectCreateInput { Name = plan.ComputeEnvironment },
                request.Token,
                cancellationToken).ConfigureAwait(false);
            RailwayGraphQLClient.ThrowIfFailed(response, "projectCreate");

            var project = response.Data?.ProjectCreate;
            if (string.IsNullOrWhiteSpace(project?.Id))
            {
                throw new InvalidOperationException("projectCreate returned no project id.");
            }

            var productionId = FindEnvironmentId(project.Environments, "production");
            await createTask.CompleteAsync(
                new MarkdownString($"Created project `{project.Id}`."),
                CompletionState.Completed,
                cancellationToken).ConfigureAwait(false);
            return (project.Id, true, productionId);
        }
    }

    private async Task<(string EnvironmentId, bool Created, bool DuplicatedProduction)> EnsureEnvironmentAsync(
        RailwayPlan plan,
        RailwayApplyRequest request,
        RailwayDeploymentSnapshot snapshot,
        string projectId,
        string? productionEnvironmentId,
        IReportingStep reportingStep,
        CancellationToken cancellationToken)
    {
        var existing = FirstNonEmpty(request.AdoptedEnvironmentId, snapshot.EnvironmentId);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            var adoptTask = await reportingStep.CreateTaskAsync(
                new MarkdownString($"Adopt Railway environment `{existing}`"),
                cancellationToken).ConfigureAwait(false);
            await using (adoptTask.ConfigureAwait(false))
            {
                await adoptTask.CompleteAsync(
                    "Using an existing Railway environment id (adopt parameter or persisted deployment state).",
                    CompletionState.Completed,
                    cancellationToken).ConfigureAwait(false);
            }

            return (existing, false, false);
        }

        if (string.Equals(plan.RailwayEnvironmentName, "production", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(productionEnvironmentId))
        {
            var reuseTask = await reportingStep.CreateTaskAsync(
                new MarkdownString("Use production environment created with the project"),
                cancellationToken).ConfigureAwait(false);
            await using (reuseTask.ConfigureAwait(false))
            {
                await reuseTask.CompleteAsync(
                    new MarkdownString($"Using production environment `{productionEnvironmentId}`."),
                    CompletionState.Completed,
                    cancellationToken).ConfigureAwait(false);
            }

            return (productionEnvironmentId, false, false);
        }

        var shouldDuplicateStaging =
            string.Equals(plan.RailwayEnvironmentName, "staging", StringComparison.OrdinalIgnoreCase) &&
            plan.DuplicateProductionWhenCreatingStaging &&
            request.DuplicateProductionWhenCreatingStaging &&
            !plan.CreateEmptyEnvironment &&
            !request.CreateEmptyEnvironment;

        string? sourceEnvironmentId = null;
        if (shouldDuplicateStaging)
        {
            sourceEnvironmentId = productionEnvironmentId;
            if (string.IsNullOrWhiteSpace(sourceEnvironmentId))
            {
                throw new InvalidOperationException(
                    "Cannot create staging by duplicating production: the production environment id is unknown. " +
                    "Deploy production first, adopt with railway-environment-id, or opt into CreateEmptyEnvironment.");
            }
        }

        var createTask = await reportingStep.CreateTaskAsync(
            new MarkdownString($"Create Railway environment **{plan.RailwayEnvironmentName}**"),
            cancellationToken).ConfigureAwait(false);
        await using (createTask.ConfigureAwait(false))
        {
            var response = await _client.EnvironmentCreateAsync(
                new EnvironmentCreateInput
                {
                    ProjectId = projectId,
                    Name = plan.RailwayEnvironmentName,
                    SourceEnvironmentId = sourceEnvironmentId
                },
                request.Token,
                cancellationToken).ConfigureAwait(false);
            RailwayGraphQLClient.ThrowIfFailed(response, "environmentCreate");

            var environment = response.Data?.EnvironmentCreate;
            if (string.IsNullOrWhiteSpace(environment?.Id))
            {
                throw new InvalidOperationException("environmentCreate returned no environment id.");
            }

            var detail = sourceEnvironmentId is null
                ? "Created an empty environment."
                : $"Duplicated production `{sourceEnvironmentId}`.";
            await createTask.CompleteAsync(
                new MarkdownString($"{detail} Environment `{environment.Id}`."),
                CompletionState.Completed,
                cancellationToken).ConfigureAwait(false);
            return (environment.Id, true, sourceEnvironmentId is not null);
        }
    }

    private async Task ApplyManagedTemplatesAsync(
        RailwayPlan plan,
        RailwayApplyRequest request,
        RailwayApplyResult result,
        IReportingStep reportingStep,
        Func<Task> persistAsync,
        CancellationToken cancellationToken)
    {
        foreach (var managed in plan.ManagedServices)
        {
            if (string.IsNullOrWhiteSpace(managed.TemplateCode))
            {
                continue;
            }

            if (ShouldSkipTemplateDeploy(managed, result))
            {
                RecordAppliedTemplate(managed, result);
                var skipTask = await reportingStep.CreateTaskAsync(
                    new MarkdownString($"Template `{managed.TemplateCode}` already applied"),
                    cancellationToken).ConfigureAwait(false);
                await using (skipTask.ConfigureAwait(false))
                {
                    await skipTask.CompleteAsync(
                        "Skipping templateDeployV2 because a matching service already exists on the project or this template code is already in deployment state.",
                        CompletionState.Completed,
                        cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            var task = await reportingStep.CreateTaskAsync(
                new MarkdownString($"Apply Railway template `{managed.TemplateCode}` for **{managed.Name}**"),
                cancellationToken).ConfigureAwait(false);
            await using (task.ConfigureAwait(false))
            {
                var deploy = await _client.ApplyTemplateAsync(
                    managed.TemplateCode,
                    result.ProjectId,
                    result.EnvironmentId,
                    request.Token,
                    cancellationToken).ConfigureAwait(false);

                var workflowId = deploy.Data?.TemplateDeployV2?.WorkflowId;
                if (string.IsNullOrWhiteSpace(workflowId))
                {
                    throw new InvalidOperationException(
                        $"templateDeployV2 for '{managed.TemplateCode}' returned no workflowId. " +
                        "The template is not recorded as applied.");
                }

                await WaitForWorkflowAsync(workflowId, request.Token, cancellationToken).ConfigureAwait(false);

                result.AppliedTemplateCodes.Add(managed.TemplateCode);
                await persistAsync().ConfigureAwait(false);
                await task.CompleteAsync(
                    new MarkdownString($"Deployed template `{managed.TemplateCode}` via templateDeployV2."),
                    CompletionState.Completed,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task WaitForWorkflowAsync(string workflowId, string token, CancellationToken cancellationToken)
    {
        var deadline = _options.TimeProvider.GetUtcNow() + _options.WorkflowTimeout;
        while (true)
        {
            var statusResponse = await _client.WorkflowStatusAsync(workflowId, token, cancellationToken)
                .ConfigureAwait(false);
            RailwayGraphQLClient.ThrowIfFailed(statusResponse, "workflowStatus");

            var status = statusResponse.Data?.WorkflowStatus?.Status;
            var error = statusResponse.Data?.WorkflowStatus?.Error;
            if (IsWorkflowSuccess(status))
            {
                return;
            }

            if (IsWorkflowFailure(status))
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(error)
                        ? $"templateDeployV2 workflow '{workflowId}' failed with status '{status}'."
                        : $"templateDeployV2 workflow '{workflowId}' failed: {error}");
            }

            if (_options.TimeProvider.GetUtcNow() >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out waiting for templateDeployV2 workflow '{workflowId}' (last status: '{status}').");
            }

            await Task.Delay(_options.WorkflowPollInterval, _options.TimeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<BucketS3Credentials> WaitForBucketS3CredentialsAsync(
        string bucketId,
        string bucketName,
        RailwayApplyRequest request,
        RailwayApplyResult result,
        bool retryWhileInstanceMissing,
        CancellationToken cancellationToken)
    {
        var deadline = _options.TimeProvider.GetUtcNow() + _options.BucketCredentialsTimeout;
        while (true)
        {
            var credentialsResponse = await _client.BucketS3CredentialsAsync(
                bucketId,
                result.EnvironmentId,
                result.ProjectId,
                request.Token,
                cancellationToken).ConfigureAwait(false);

            if (retryWhileInstanceMissing &&
                IsBucketInstanceNotFound(credentialsResponse) &&
                _options.TimeProvider.GetUtcNow() < deadline)
            {
                await Task.Delay(_options.BucketCredentialsPollInterval, _options.TimeProvider, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            RailwayGraphQLClient.ThrowIfFailed(credentialsResponse, "bucketS3Credentials");

            var credentials = credentialsResponse.Data?.BucketS3Credentials;
            if (credentials is null ||
                string.IsNullOrWhiteSpace(credentials.AccessKeyId) ||
                string.IsNullOrWhiteSpace(credentials.SecretAccessKey))
            {
                throw new InvalidOperationException(
                    $"bucketS3Credentials did not return access keys for bucket '{bucketName}'. " +
                    "Credentials are not persisted; apply cannot invent them.");
            }

            return credentials;
        }
    }

    private static bool IsBucketInstanceNotFound<T>(RailwayGraphQLResponse<T> response) =>
        response.Errors is { Count: > 0 } &&
        response.Errors.All(static error =>
            !string.IsNullOrWhiteSpace(error.Message) &&
            error.Message.Contains("BucketInstance not found", StringComparison.OrdinalIgnoreCase));

    private async Task ApplyVolumeBackupSchedulesAsync(
        RailwayPlan plan,
        RailwayApplyRequest request,
        RailwayApplyResult result,
        IReportingStep reportingStep,
        Func<Task> persistAsync,
        CancellationToken cancellationToken)
    {
        foreach (var managed in plan.ManagedServices)
        {
            if (managed.VolumeBackupScheduleKinds is not { Count: > 0 } requestedRaw)
            {
                continue;
            }

            var requested = RailwayVolumeBackupSchedule.Normalize(requestedRaw, managed.Name);
            var task = await reportingStep.CreateTaskAsync(
                new MarkdownString($"Volume backup schedule for **{managed.Name}**"),
                cancellationToken).ConfigureAwait(false);
            await using (task.ConfigureAwait(false))
            {
                await EnsureManagedServiceIdAsync(plan, request, result, managed, cancellationToken)
                    .ConfigureAwait(false);
                if (!TryGetManagedServiceId(result, managed, out var serviceId))
                {
                    throw new InvalidOperationException(
                        $"Cannot apply volume backup schedules for '{managed.Name}': " +
                        "the official Postgres service id is unknown after template deploy. " +
                        "project(id) did not list a matching service.");
                }

                var volumeInstanceId = await WaitForVolumeInstanceIdAsync(
                    serviceId,
                    managed.Name,
                    request,
                    result,
                    cancellationToken).ConfigureAwait(false);
                result.VolumeInstanceIds[managed.Name] = volumeInstanceId;
                await persistAsync().ConfigureAwait(false);

                var listResponse = await _client.VolumeInstanceBackupScheduleListAsync(
                    volumeInstanceId,
                    request.Token,
                    cancellationToken).ConfigureAwait(false);
                RailwayGraphQLClient.ThrowIfFailed(listResponse, "volumeInstanceBackupScheduleList");

                var existingSchedules = listResponse.Data?.VolumeInstanceBackupScheduleList ?? [];
                var existingKinds = existingSchedules
                    .Select(static schedule => schedule.Kind)
                    .Where(static kind => !string.IsNullOrWhiteSpace(kind))
                    .Cast<string>()
                    .ToList();
                var existingOfficial = existingKinds.Count == 0
                    ? []
                    : RailwayVolumeBackupSchedule.Normalize(existingKinds, managed.Name);
                var union = RailwayVolumeBackupSchedule.Union(requested, existingOfficial, managed.Name);

                if (RailwayVolumeBackupSchedule.IsSubset(requested, existingOfficial))
                {
                    PersistScheduleIds(result, managed.Name, existingSchedules);
                    await persistAsync().ConfigureAwait(false);
                    await task.CompleteAsync(
                        new MarkdownString(
                            $"Volume backup schedule kinds already present for `{managed.Name}`: " +
                            $"`{string.Join("`, `", union)}`. Mutation skipped."),
                        CompletionState.Completed,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var update = await _client.VolumeInstanceBackupScheduleUpdateAsync(
                    union,
                    volumeInstanceId,
                    request.Token,
                    cancellationToken).ConfigureAwait(false);
                RailwayGraphQLClient.ThrowIfFailed(update, "volumeInstanceBackupScheduleUpdate");

                var refreshed = await _client.VolumeInstanceBackupScheduleListAsync(
                    volumeInstanceId,
                    request.Token,
                    cancellationToken).ConfigureAwait(false);
                RailwayGraphQLClient.ThrowIfFailed(refreshed, "volumeInstanceBackupScheduleList");
                PersistScheduleIds(
                    result,
                    managed.Name,
                    refreshed.Data?.VolumeInstanceBackupScheduleList ?? existingSchedules);
                await persistAsync().ConfigureAwait(false);

                await task.CompleteAsync(
                    new MarkdownString(
                        $"Applied volume backup schedule kinds for `{managed.Name}`: " +
                        $"`{string.Join("`, `", union)}`. Deploy does not wait for a backup to complete."),
                    CompletionState.Completed,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task EnsureManagedServiceIdAsync(
        RailwayPlan plan,
        RailwayApplyRequest request,
        RailwayApplyResult result,
        RailwayPlanManagedService managed,
        CancellationToken cancellationToken)
    {
        if (TryGetManagedServiceId(result, managed, out _))
        {
            return;
        }

        var response = await _client.ProjectAsync(result.ProjectId, request.Token, cancellationToken)
            .ConfigureAwait(false);
        RailwayGraphQLClient.ThrowIfFailed(response, "project");
        AdoptServicesFromProject(plan, result, response.Data?.Project);
    }

    private static bool TryGetManagedServiceId(
        RailwayApplyResult result,
        RailwayPlanManagedService managed,
        out string serviceId)
    {
        if (HasServiceId(result, managed.Name))
        {
            serviceId = result.ServiceIds[managed.Name];
            return true;
        }

        if (HasServiceId(result, managed.TemplateCode))
        {
            serviceId = result.ServiceIds[managed.TemplateCode!];
            result.ServiceIds[managed.Name] = serviceId;
            return true;
        }

        serviceId = "";
        return false;
    }

    private async Task<string> WaitForVolumeInstanceIdAsync(
        string serviceId,
        string serviceName,
        RailwayApplyRequest request,
        RailwayApplyResult result,
        CancellationToken cancellationToken)
    {
        var deadline = _options.TimeProvider.GetUtcNow() + _options.VolumeInstanceTimeout;
        while (true)
        {
            var volumeInstanceId = await TryFindVolumeInstanceIdAsync(
                serviceId,
                request,
                result,
                cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(volumeInstanceId))
            {
                return volumeInstanceId;
            }

            if (_options.TimeProvider.GetUtcNow() >= deadline)
            {
                throw new InvalidOperationException(
                    $"No Railway volume instance matched Postgres service '{serviceName}' (`{serviceId}`) " +
                    "in environment.volumeInstances. The official template may still be provisioning, " +
                    "or the service has no volume. This integration does not invent a volume id.");
            }

            await Task.Delay(_options.VolumeInstancePollInterval, _options.TimeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<string?> TryFindVolumeInstanceIdAsync(
        string serviceId,
        RailwayApplyRequest request,
        RailwayApplyResult result,
        CancellationToken cancellationToken)
    {
        string? after = null;
        while (true)
        {
            var response = await _client.EnvironmentAsync(
                result.EnvironmentId,
                result.ProjectId,
                request.Token,
                after,
                first: 50,
                cancellationToken).ConfigureAwait(false);
            RailwayGraphQLClient.ThrowIfFailed(response, "environment");

            var connection = response.Data?.Environment?.VolumeInstances;
            if (connection?.Edges is { } edges)
            {
                foreach (var edge in edges)
                {
                    if (edge.Node is { } node &&
                        !string.IsNullOrWhiteSpace(node.Id) &&
                        string.Equals(node.ServiceId, serviceId, StringComparison.Ordinal))
                    {
                        return node.Id;
                    }
                }
            }

            if (connection?.PageInfo is not { HasNextPage: true } pageInfo ||
                string.IsNullOrWhiteSpace(pageInfo.EndCursor))
            {
                return null;
            }

            after = pageInfo.EndCursor;
        }
    }

    private static void PersistScheduleIds(
        RailwayApplyResult result,
        string serviceName,
        IEnumerable<RailwayVolumeInstanceBackupSchedule> schedules)
    {
        foreach (var schedule in schedules)
        {
            if (string.IsNullOrWhiteSpace(schedule.Id) || string.IsNullOrWhiteSpace(schedule.Kind))
            {
                continue;
            }

            result.VolumeBackupScheduleIds[$"{serviceName}-{schedule.Kind}"] = schedule.Id;
        }
    }

    private async Task ApplyBucketsAsync(
        RailwayPlan plan,
        RailwayApplyRequest request,
        RailwayApplyResult result,
        IReportingStep reportingStep,
        Func<Task> persistAsync,
        CancellationToken cancellationToken)
    {
        foreach (var managed in plan.ManagedServices)
        {
            if (!string.Equals(managed.Kind, "bucket", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var task = await reportingStep.CreateTaskAsync(
                new MarkdownString($"Apply Railway bucket **{managed.Name}**"),
                cancellationToken).ConfigureAwait(false);
            await using (task.ConfigureAwait(false))
            {
                var createdThisApply = false;
                if (!result.BucketIds.TryGetValue(managed.Name, out var bucketId) ||
                    string.IsNullOrWhiteSpace(bucketId))
                {
                    var created = await _client.BucketCreateAsync(
                        new BucketCreateInput
                        {
                            ProjectId = result.ProjectId,
                            EnvironmentId = result.EnvironmentId,
                            Name = managed.Name
                        },
                        request.Token,
                        cancellationToken).ConfigureAwait(false);
                    RailwayGraphQLClient.ThrowIfFailed(created, "bucketCreate");
                    bucketId = created.Data?.BucketCreate?.Id;
                    if (string.IsNullOrWhiteSpace(bucketId))
                    {
                        throw new InvalidOperationException($"bucketCreate returned no id for '{managed.Name}'.");
                    }

                    result.BucketIds[managed.Name] = bucketId;
                    createdThisApply = true;
                    await persistAsync().ConfigureAwait(false);
                }

                // After a real bucketCreate, Railway may not have a BucketInstance yet.
                // Retry credentials with backoff instead of querying immediately.
                // Adopted / persisted ids are queried once; they already have an instance.
                var credentials = await WaitForBucketS3CredentialsAsync(
                    bucketId,
                    managed.Name,
                    request,
                    result,
                    retryWhileInstanceMissing: createdThisApply,
                    cancellationToken).ConfigureAwait(false);

                // Image-less service that holds ${{uploads.ENDPOINT}} (and related) variables
                // so WithReference can resolve them. It is not a compute target and must not
                // be deployed with serviceInstanceDeployV2.
                if (!result.ServiceIds.TryGetValue(managed.Name, out var serviceId) ||
                    string.IsNullOrWhiteSpace(serviceId))
                {
                    var service = await _client.ServiceCreateAsync(
                        new ServiceCreateInput
                        {
                            ProjectId = result.ProjectId,
                            EnvironmentId = result.EnvironmentId,
                            Name = managed.Name
                        },
                        request.Token,
                        cancellationToken).ConfigureAwait(false);
                    RailwayGraphQLClient.ThrowIfFailed(service, "serviceCreate");
                    serviceId = service.Data?.ServiceCreate?.Id;
                    if (string.IsNullOrWhiteSpace(serviceId))
                    {
                        throw new InvalidOperationException(
                            $"serviceCreate returned no id for bucket variable service '{managed.Name}'.");
                    }

                    result.ServiceIds[managed.Name] = serviceId;
                    await persistAsync().ConfigureAwait(false);
                }

                var upsert = await _client.VariableCollectionUpsertAsync(
                    new VariableCollectionUpsertInput
                    {
                        ProjectId = result.ProjectId,
                        EnvironmentId = result.EnvironmentId,
                        ServiceId = serviceId,
                        Variables = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["ENDPOINT"] = string.IsNullOrWhiteSpace(credentials.Endpoint)
                                ? RailwayConstants.BucketS3Endpoint
                                : credentials.Endpoint,
                            ["ACCESS_KEY_ID"] = credentials.AccessKeyId!,
                            ["SECRET_ACCESS_KEY"] = credentials.SecretAccessKey!,
                            ["BUCKET"] = string.IsNullOrWhiteSpace(credentials.BucketName)
                                ? managed.Name
                                : credentials.BucketName,
                            ["REGION"] = string.IsNullOrWhiteSpace(credentials.Region) ? "auto" : credentials.Region
                        }
                    },
                    request.Token,
                    cancellationToken).ConfigureAwait(false);
                RailwayGraphQLClient.ThrowIfFailed(upsert, "variableCollectionUpsert");

                var endpoint = string.IsNullOrWhiteSpace(credentials.Endpoint)
                    ? RailwayConstants.BucketS3Endpoint
                    : credentials.Endpoint;
                var bucketName = string.IsNullOrWhiteSpace(credentials.BucketName)
                    ? managed.Name
                    : credentials.BucketName;
                var region = string.IsNullOrWhiteSpace(credentials.Region) ? "auto" : credentials.Region;
                result.BucketConnectionStrings[managed.Name] =
                    $"Endpoint={endpoint};AccessKeyId={credentials.AccessKeyId};SecretAccessKey={credentials.SecretAccessKey};Bucket={bucketName};Region={region};ForcePathStyle=false";

                await persistAsync().ConfigureAwait(false);

                await task.CompleteAsync(
                    new MarkdownString($"Bucket `{managed.Name}` is available at `{RailwayConstants.BucketS3Endpoint}`."),
                    CompletionState.Completed,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ApplyComputeServicesAsync(
        RailwayPlan plan,
        RailwayApplyRequest request,
        RailwayApplyResult result,
        IReportingStep reportingStep,
        Func<Task> persistAsync,
        CancellationToken cancellationToken)
    {
        foreach (var service in plan.Services)
        {
            var task = await reportingStep.CreateTaskAsync(
                new MarkdownString($"Apply Railway service **{service.Name}**"),
                cancellationToken).ConfigureAwait(false);
            await using (task.ConfigureAwait(false))
            {
                if (!request.ServiceImages.TryGetValue(service.Name, out var image) ||
                    string.IsNullOrWhiteSpace(image) ||
                    image.StartsWith('{'))
                {
                    throw new InvalidOperationException(
                        $"Cannot deploy image-based service '{service.Name}' because no container image was resolved. " +
                        "Railway has no image registry. Push to GHCR or Docker Hub (IContainerRegistry) first. " +
                        "Do not use `railway up`.");
                }

                if (!result.ServiceIds.TryGetValue(service.Name, out var serviceId) ||
                    string.IsNullOrWhiteSpace(serviceId))
                {
                    var created = await _client.ServiceCreateAsync(
                        new ServiceCreateInput
                        {
                            ProjectId = result.ProjectId,
                            EnvironmentId = result.EnvironmentId,
                            Name = service.Name
                        },
                        request.Token,
                        cancellationToken).ConfigureAwait(false);
                    RailwayGraphQLClient.ThrowIfFailed(created, "serviceCreate");
                    serviceId = created.Data?.ServiceCreate?.Id;
                    if (string.IsNullOrWhiteSpace(serviceId))
                    {
                        throw new InvalidOperationException($"serviceCreate returned no id for '{service.Name}'.");
                    }

                    result.ServiceIds[service.Name] = serviceId;
                    result.AdoptedRailwayServiceNames.Add(service.Name);
                    await persistAsync().ConfigureAwait(false);
                }

                var update = await _client.ServiceInstanceUpdateAsync(
                    serviceId,
                    result.EnvironmentId,
                    RailwayServiceComputeSettings.CreateUpdateInput(service, image),
                    request.Token,
                    cancellationToken).ConfigureAwait(false);
                RailwayGraphQLClient.ThrowIfFailed(update, "serviceInstanceUpdate");

                var limitsInput = RailwayServiceComputeSettings.CreateLimitsUpdateInput(
                    service,
                    serviceId,
                    result.EnvironmentId);
                if (limitsInput is not null)
                {
                    var limits = await _client.ServiceInstanceLimitsUpdateAsync(
                        limitsInput,
                        request.Token,
                        cancellationToken).ConfigureAwait(false);
                    RailwayGraphQLClient.ThrowIfFailed(limits, "serviceInstanceLimitsUpdate");
                }

                var variables = ResolveServiceEnvironment(service, request, result);
                if (variables.Count > 0)
                {
                    var upsert = await _client.VariableCollectionUpsertAsync(
                        new VariableCollectionUpsertInput
                        {
                            ProjectId = result.ProjectId,
                            EnvironmentId = result.EnvironmentId,
                            ServiceId = serviceId,
                            Variables = variables
                        },
                        request.Token,
                        cancellationToken).ConfigureAwait(false);
                    RailwayGraphQLClient.ThrowIfFailed(upsert, "variableCollectionUpsert");
                }

                if (request.ExternalHttpServices.Contains(service.Name))
                {
                    try
                    {
                        var domain = await _client.ServiceDomainCreateAsync(
                            new ServiceDomainCreateInput
                            {
                                ServiceId = serviceId,
                                EnvironmentId = result.EnvironmentId,
                                TargetPort = service.TargetPort
                            },
                            request.Token,
                            cancellationToken).ConfigureAwait(false);
                        RailwayGraphQLClient.ThrowIfFailed(domain, "serviceDomainCreate");
                    }
                    catch (InvalidOperationException exception)
                    {
                        result.Warnings.Add(exception.Message);
                        reportingStep.Log(Microsoft.Extensions.Logging.LogLevel.Warning, exception.Message);
                    }

                    await ApplyCustomDomainsAsync(
                        service,
                        request,
                        result,
                        serviceId,
                        reportingStep,
                        persistAsync,
                        cancellationToken).ConfigureAwait(false);
                }

                var deploy = await _client.ServiceInstanceDeployV2Async(
                    serviceId,
                    result.EnvironmentId,
                    request.Token,
                    cancellationToken).ConfigureAwait(false);
                RailwayGraphQLClient.ThrowIfFailed(deploy, "serviceInstanceDeployV2");
                await persistAsync().ConfigureAwait(false);

                await task.CompleteAsync(
                    new MarkdownString($"Service `{service.Name}` source.image is `{image}`."),
                    CompletionState.Completed,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ApplyCustomDomainsAsync(
        RailwayPlanService service,
        RailwayApplyRequest request,
        RailwayApplyResult result,
        string serviceId,
        IReportingStep reportingStep,
        Func<Task> persistAsync,
        CancellationToken cancellationToken)
    {
        if (service.CustomDomains is not { Count: > 0 } hostnames)
        {
            return;
        }

        var listResponse = await _client.DomainsAsync(
            result.EnvironmentId,
            result.ProjectId,
            serviceId,
            request.Token,
            cancellationToken).ConfigureAwait(false);
        RailwayGraphQLClient.ThrowIfFailed(listResponse, "domains");

        var existing = listResponse.Data?.Domains?.CustomDomains ?? [];

        foreach (var hostname in hostnames)
        {
            var task = await reportingStep.CreateTaskAsync(
                new MarkdownString($"Custom domain `{hostname}` for **{service.Name}**"),
                cancellationToken).ConfigureAwait(false);
            await using (task.ConfigureAwait(false))
            {
                var domain = await EnsureCustomDomainAsync(
                    hostname,
                    service.TargetPort,
                    existing,
                    request,
                    result,
                    serviceId,
                    cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(domain.Id))
                {
                    throw new InvalidOperationException(
                        $"Railway did not return a custom domain id for '{hostname}'.");
                }

                result.CustomDomainIds[hostname] = domain.Id;
                await persistAsync().ConfigureAwait(false);
                await task.CompleteAsync(
                    new MarkdownString(FormatCustomDomainReport(hostname, domain)),
                    CompletionState.Completed,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<RailwayCustomDomain> EnsureCustomDomainAsync(
        string hostname,
        int? targetPort,
        IReadOnlyList<RailwayCustomDomain> existing,
        RailwayApplyRequest request,
        RailwayApplyResult result,
        string serviceId,
        CancellationToken cancellationToken)
    {
        var match = existing.FirstOrDefault(candidate =>
            string.Equals(candidate.Domain, hostname, StringComparison.OrdinalIgnoreCase));
        if (match is not null && !string.IsNullOrWhiteSpace(match.Id))
        {
            if (targetPort is { } port && match.TargetPort != port)
            {
                var updated = await _client.CustomDomainUpdateAsync(
                    result.EnvironmentId,
                    match.Id,
                    port,
                    request.Token,
                    cancellationToken).ConfigureAwait(false);
                RailwayGraphQLClient.ThrowIfFailed(updated, "customDomainUpdate");
                return updated.Data?.CustomDomainUpdate
                    ?? throw new InvalidOperationException(
                        $"customDomainUpdate returned no custom domain for '{hostname}'.");
            }

            var queried = await _client.CustomDomainAsync(
                match.Id,
                result.ProjectId,
                request.Token,
                cancellationToken).ConfigureAwait(false);
            RailwayGraphQLClient.ThrowIfFailed(queried, "customDomain");
            return queried.Data?.CustomDomain
                ?? throw new InvalidOperationException(
                    $"customDomain returned no custom domain for '{hostname}'.");
        }

        var available = await _client.CustomDomainAvailableAsync(
            hostname,
            request.Token,
            cancellationToken).ConfigureAwait(false);
        RailwayGraphQLClient.ThrowIfFailed(available, "customDomainAvailable");

        if (available.Data?.CustomDomainAvailable is { Available: false } unavailable)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(unavailable.Message)
                    ? $"Custom domain '{hostname}' is not available."
                    : $"Custom domain '{hostname}' is not available: {unavailable.Message}");
        }

        var created = await _client.CustomDomainCreateAsync(
            new CustomDomainCreateInput
            {
                Domain = hostname,
                EnvironmentId = result.EnvironmentId,
                ProjectId = result.ProjectId,
                ServiceId = serviceId,
                TargetPort = targetPort
            },
            request.Token,
            cancellationToken).ConfigureAwait(false);
        RailwayGraphQLClient.ThrowIfFailed(created, "customDomainCreate");
        return created.Data?.CustomDomainCreate
            ?? throw new InvalidOperationException(
                $"customDomainCreate returned no custom domain for '{hostname}'.");
    }

    private static string FormatCustomDomainReport(string hostname, RailwayCustomDomain domain)
    {
        var lines = new List<string>
        {
            $"Custom domain `{hostname}`."
        };

        if (domain.Status?.DnsRecords is { Count: > 0 } records)
        {
            lines.Add("DNS records (as Railway returned them; this integration does not rewrite record types):");
            foreach (var record in records)
            {
                lines.Add(
                    $"- `{record.RecordType}` `{record.Fqdn}` → `{record.RequiredValue}`");
            }
        }

        if (domain.Status is { } status)
        {
            if (!string.IsNullOrWhiteSpace(status.VerificationDnsHost) ||
                !string.IsNullOrWhiteSpace(status.VerificationToken))
            {
                lines.Add($"Verification TXT host: `{status.VerificationDnsHost}`");
                lines.Add($"Verification token: `{status.VerificationToken}`");
            }

            lines.Add($"Verified: {status.Verified.ToString().ToLowerInvariant()}");
            if (!string.IsNullOrWhiteSpace(status.CertificateStatus))
            {
                lines.Add($"Certificate: `{status.CertificateStatus}`");
            }
        }

        lines.Add(
            "Add both the routing record and the verification TXT. Missing TXT returns 404 even if the routing record resolves. " +
            "Railway issues Let's Encrypt after verify. This integration does not talk to your DNS provider. " +
            "Pending DNS or certificate does not fail the deploy.");
        return string.Join("\n", lines);
    }

    private async Task CommitStagedAsync(
        RailwayApplyRequest request,
        RailwayApplyResult result,
        IReportingStep reportingStep,
        CancellationToken cancellationToken)
    {
        var task = await reportingStep.CreateTaskAsync(
            "Commit staged Railway environment patches",
            cancellationToken).ConfigureAwait(false);
        await using (task.ConfigureAwait(false))
        {
            try
            {
                var commit = await _client.EnvironmentPatchCommitStagedAsync(
                    result.EnvironmentId,
                    request.Token,
                    cancellationToken).ConfigureAwait(false);
                RailwayGraphQLClient.ThrowIfFailed(commit, "environmentPatchCommitStaged");
                await task.CompleteAsync(
                    "Committed staged environment patches.",
                    CompletionState.Completed,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                result.Warnings.Add(exception.Message);
                await task.CompleteAsync(
                    exception.Message,
                    CompletionState.CompletedWithWarning,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Dictionary<string, string> ResolveServiceEnvironment(
        RailwayPlanService service,
        RailwayApplyRequest request,
        RailwayApplyResult result)
    {
        var variables = new Dictionary<string, string>(service.Environment, StringComparer.Ordinal);
        if (request.ResolvedServiceEnvironment.TryGetValue(service.Name, out var resolved) &&
            resolved is not null)
        {
            foreach (var pair in resolved)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                {
                    variables[pair.Key] = pair.Value;
                }
            }
        }

        foreach (var pair in result.BucketConnectionStrings)
        {
            variables[$"ConnectionStrings__{pair.Key}"] = pair.Value;
        }

        foreach (var pair in variables.ToArray())
        {
            variables[pair.Key] = RailwayReferenceExpressions.RewriteServiceName(
                pair.Value,
                result.AdoptedRailwayServiceNames);
        }

        foreach (var pair in variables.ToArray())
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                variables.Remove(pair.Key);
            }
        }

        return variables;
    }

    private async Task AdoptExistingProjectResourcesAsync(
        RailwayPlan plan,
        RailwayApplyRequest request,
        RailwayApplyResult result,
        IReportingStep reportingStep,
        CancellationToken cancellationToken)
    {
        var task = await reportingStep.CreateTaskAsync(
            new MarkdownString($"List existing Railway project `{result.ProjectId}` services and buckets"),
            cancellationToken).ConfigureAwait(false);
        await using (task.ConfigureAwait(false))
        {
            var response = await _client.ProjectAsync(result.ProjectId, request.Token, cancellationToken)
                .ConfigureAwait(false);
            RailwayGraphQLClient.ThrowIfFailed(response, "project");

            var adoptedServices = AdoptServicesFromProject(plan, result, response.Data?.Project);
            var adoptedBuckets = AdoptBucketsFromProject(plan, result, response.Data?.Project);

            await task.CompleteAsync(
                FormatAdoptCompletion(adoptedServices, adoptedBuckets),
                CompletionState.Completed,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static int AdoptServicesFromProject(
        RailwayPlan plan,
        RailwayApplyResult result,
        RailwayProject? project)
    {
        var adopted = 0;
        if (project?.Services?.Edges is not { } edges)
        {
            return adopted;
        }

        foreach (var edge in edges)
        {
            if (edge.Node is not { } node ||
                string.IsNullOrWhiteSpace(node.Id) ||
                string.IsNullOrWhiteSpace(node.Name))
            {
                continue;
            }

            result.ServiceIds[node.Name] = node.Id;
            result.AdoptedRailwayServiceNames.Add(node.Name);
            adopted++;

            foreach (var managed in plan.ManagedServices)
            {
                if (!ServiceNameMatches(managed, node.Name))
                {
                    continue;
                }

                result.ServiceIds[managed.Name] = node.Id;
                if (!string.IsNullOrWhiteSpace(managed.TemplateCode))
                {
                    RecordAppliedTemplate(managed, result);
                }
            }
        }

        return adopted;
    }

    private static int AdoptBucketsFromProject(
        RailwayPlan plan,
        RailwayApplyResult result,
        RailwayProject? project)
    {
        var adopted = 0;
        if (project?.Buckets?.Edges is not { } edges)
        {
            return adopted;
        }

        foreach (var edge in edges)
        {
            if (edge.Node is not { } node ||
                string.IsNullOrWhiteSpace(node.Id) ||
                string.IsNullOrWhiteSpace(node.Name))
            {
                continue;
            }

            foreach (var managed in plan.ManagedServices)
            {
                if (!string.Equals(managed.Kind, "bucket", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(managed.Name, node.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // A same-name service is unrelated. Never copy ServiceIds into BucketIds.
                result.BucketIds[managed.Name] = node.Id;
                adopted++;
            }
        }

        return adopted;
    }

    private static string FormatAdoptCompletion(int adoptedServices, int adoptedBuckets)
    {
        if (adoptedServices == 0 && adoptedBuckets == 0)
        {
            return "No existing Railway services or buckets matched plan names.";
        }

        return $"Adopted {adoptedServices} existing Railway service id(s) and {adoptedBuckets} bucket id(s) by name.";
    }

    private static bool ShouldSkipTemplateDeploy(RailwayPlanManagedService managed, RailwayApplyResult result)
    {
        if (result.AppliedTemplateCodes.Contains(managed.TemplateCode, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return HasServiceId(result, managed.Name) ||
               HasServiceId(result, managed.TemplateCode);
    }

    private static void RecordAppliedTemplate(RailwayPlanManagedService managed, RailwayApplyResult result)
    {
        if (!string.IsNullOrWhiteSpace(managed.TemplateCode) &&
            !result.AppliedTemplateCodes.Contains(managed.TemplateCode, StringComparer.OrdinalIgnoreCase))
        {
            result.AppliedTemplateCodes.Add(managed.TemplateCode);
        }
    }

    private static bool ServiceNameMatches(RailwayPlanManagedService managed, string railwayServiceName) =>
        string.Equals(managed.Name, railwayServiceName, StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(managed.TemplateCode) &&
         string.Equals(managed.TemplateCode, railwayServiceName, StringComparison.OrdinalIgnoreCase));

    private static bool HasServiceId(RailwayApplyResult result, string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        result.ServiceIds.TryGetValue(name, out var serviceId) &&
        !string.IsNullOrWhiteSpace(serviceId);

    private static void SeedFromProduction(RailwayDeploymentSnapshot snapshot, RailwayApplyResult result)
    {
        foreach (var pair in snapshot.ProductionServiceIds)
        {
            result.ServiceIds.TryAdd(pair.Key, pair.Value);
        }

        foreach (var pair in snapshot.ProductionBucketIds)
        {
            result.BucketIds.TryAdd(pair.Key, pair.Value);
        }

        foreach (var code in snapshot.ProductionTemplateCodes)
        {
            if (!result.AppliedTemplateCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                result.AppliedTemplateCodes.Add(code);
            }
        }
    }

    private static string? FindEnvironmentId(RailwayNamedResourceConnection? connection, string name)
    {
        if (connection?.Edges is null)
        {
            return null;
        }

        foreach (var edge in connection.Edges)
        {
            if (edge.Node is { } node &&
                string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(node.Id))
            {
                return node.Id;
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsWorkflowSuccess(string? status) =>
        status is not null &&
        (status.Equals("Complete", StringComparison.OrdinalIgnoreCase) ||
         status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
         status.Equals("Success", StringComparison.OrdinalIgnoreCase) ||
         status.Equals("Succeeded", StringComparison.OrdinalIgnoreCase));

    private static bool IsWorkflowFailure(string? status) =>
        status is not null &&
        (status.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
         status.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
         status.Equals("Failure", StringComparison.OrdinalIgnoreCase));
}
