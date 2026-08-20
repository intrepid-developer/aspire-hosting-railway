#pragma warning disable ASPIREPIPELINES001
#pragma warning disable ASPIREPIPELINES002

using Aspire.Hosting.Pipelines;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Tears down Railway resources this integration created, using confirmed
/// GraphQL v2 delete operations only. The pipeline calls this from
/// <c>destroy-{name}</c>. Do not overload <see cref="RailwayGraphQLApplyService"/>.
/// </summary>
public sealed class RailwayGraphQLDestroyService
{
    private readonly RailwayGraphQLClient _client;

    /// <summary>
    /// Initializes the destroy service.
    /// </summary>
    /// <param name="client">Typed GraphQL client. Tests pass a client backed by a fake handler.</param>
    public RailwayGraphQLDestroyService(RailwayGraphQLClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>
    /// Deletes Railway-provided and custom domains, then app services, then
    /// official Postgres/Redis template services this integration created.
    /// Skips adopted resources, buckets (no public <c>bucketDelete</c>), and
    /// the Railway project (v1 never calls <c>projectDelete</c>).
    /// </summary>
    public async Task<RailwayDestroyResult> DestroyAsync(
        RailwayPlan plan,
        RailwayDestroyRequest request,
        IReportingStep reportingStep,
        IDeploymentStateManager? stateManager,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reportingStep);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Token);

        var snapshot = stateManager is not null
            ? await RailwayDeploymentStateStore.LoadAsync(
                stateManager,
                plan.ComputeEnvironment,
                plan.RailwayEnvironmentName,
                cancellationToken).ConfigureAwait(false)
            : new RailwayDeploymentSnapshot();

        var projectId = FirstNonEmpty(request.AdoptedProjectId, snapshot.ProjectId);
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new InvalidOperationException(
                "Cannot destroy Railway resources: deployment state is empty and no " +
                "railway-project-id is available. Failing closed. Run aspire deploy first, " +
                "or adopt with railway-project-id / AsExisting().");
        }

        var projectResponse = await _client.ProjectAsync(projectId, request.Token, cancellationToken)
            .ConfigureAwait(false);
        RailwayGraphQLClient.ThrowIfFailed(projectResponse, "project");
        var project = projectResponse.Data?.Project
            ?? throw new InvalidOperationException("project(id) returned no project.");

        var liveEnvironments = NamedResources(project.Environments);
        var liveServices = NamedResources(project.Services);
        var liveBuckets = NamedResources(project.Buckets);

        var environmentId = FirstNonEmpty(request.AdoptedEnvironmentId, snapshot.EnvironmentId)
            ?? FindNamedId(liveEnvironments, plan.RailwayEnvironmentName);
        if (string.IsNullOrWhiteSpace(environmentId))
        {
            throw new InvalidOperationException(
                $"Cannot destroy Railway environment '{plan.RailwayEnvironmentName}': " +
                "deployment state has no environment id and project(id) has no matching environment.");
        }

        var inventory = BuildInventory(plan, snapshot, liveServices, liveBuckets, liveEnvironments, project);
        await ReportInventoryAsync(plan, projectId, environmentId, inventory, reportingStep, cancellationToken)
            .ConfigureAwait(false);

        var result = new RailwayDestroyResult();
        var adoptedProject = plan.AdoptExisting || !string.IsNullOrWhiteSpace(request.AdoptedProjectId);
        var adoptedEnvironment = plan.AdoptExisting || !string.IsNullOrWhiteSpace(request.AdoptedEnvironmentId);
        var otherEnvironments = liveEnvironments
            .Where(candidate =>
                !string.Equals(candidate.Id, environmentId, StringComparison.Ordinal) &&
                !string.Equals(candidate.Name, plan.RailwayEnvironmentName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        await DeleteCreatedDomainsAsync(
            plan,
            request,
            snapshot,
            inventory,
            projectId,
            environmentId,
            adoptedProject,
            result,
            reportingStep,
            cancellationToken).ConfigureAwait(false);

        await DeleteCreatedServicesAsync(
            plan,
            request,
            snapshot,
            inventory,
            environmentId,
            adoptedProject,
            otherEnvironments,
            appServicesOnly: true,
            result,
            reportingStep,
            cancellationToken).ConfigureAwait(false);

        await DeleteCreatedServicesAsync(
            plan,
            request,
            snapshot,
            inventory,
            environmentId,
            adoptedProject,
            otherEnvironments,
            appServicesOnly: false,
            result,
            reportingStep,
            cancellationToken).ConfigureAwait(false);

        SkipBuckets(inventory, result, reportingStep);
        SkipVolumes(snapshot, result, reportingStep);
        SkipProjectDelete(projectId, result, reportingStep);

        await DeleteCreatedEnvironmentAsync(
            plan,
            request,
            snapshot,
            environmentId,
            adoptedEnvironment,
            result,
            reportingStep,
            cancellationToken).ConfigureAwait(false);

        if (stateManager is not null)
        {
            await RailwayDeploymentStateStore.ClearDestroyedEnvironmentAsync(
                stateManager,
                plan.ComputeEnvironment,
                plan.RailwayEnvironmentName,
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task DeleteCreatedDomainsAsync(
        RailwayPlan plan,
        RailwayDestroyRequest request,
        RailwayDeploymentSnapshot snapshot,
        DestroyInventory inventory,
        string projectId,
        string environmentId,
        bool adoptedProject,
        RailwayDestroyResult result,
        IReportingStep reportingStep,
        CancellationToken cancellationToken)
    {
        foreach (var service in inventory.Services)
        {
            if (!IsCreatedService(plan, snapshot, service, adoptedProject))
            {
                if (!string.IsNullOrWhiteSpace(service.Id))
                {
                    Skip(
                        result,
                        reportingStep,
                        $"Domain(s) on service `{service.Name}`",
                        AdoptedReason(service.Name));
                }

                continue;
            }

            RailwayAllDomains? domains = null;
            if (!string.IsNullOrWhiteSpace(service.Id))
            {
                try
                {
                    var response = await _client.DomainsAsync(
                        environmentId,
                        projectId,
                        service.Id,
                        request.Token,
                        cancellationToken).ConfigureAwait(false);
                    RailwayGraphQLClient.ThrowIfFailed(response, "domains");
                    domains = response.Data?.Domains;
                }
                catch (InvalidOperationException exception)
                {
                    result.Warnings.Add(exception.Message);
                    reportingStep.Log(Microsoft.Extensions.Logging.LogLevel.Warning, exception.Message);
                }
            }

            var serviceDomains = (domains?.ServiceDomains ?? [])
                .Concat(snapshot.CreatedServiceDomainIds.TryGetValue(service.Name, out var persistedDomainId)
                    ? [new RailwayServiceDomain { Id = persistedDomainId, Domain = service.Name }]
                    : [])
                .GroupBy(domain => domain.Id, StringComparer.Ordinal)
                .Select(group => group.First());

            foreach (var serviceDomain in serviceDomains)
            {
                if (string.IsNullOrWhiteSpace(serviceDomain.Id))
                {
                    continue;
                }

                if (!ShouldDeleteServiceDomain(snapshot, service, serviceDomain, adoptedProject))
                {
                    Skip(
                        result,
                        reportingStep,
                        $"Railway domain `{serviceDomain.Domain}`",
                        AdoptedReason(serviceDomain.Domain ?? service.Name));
                    continue;
                }

                await DeleteAsync(
                    result,
                    reportingStep,
                    $"Railway domain `{serviceDomain.Domain}`",
                    () => _client.ServiceDomainDeleteAsync(serviceDomain.Id, request.Token, cancellationToken),
                    "serviceDomainDelete",
                    cancellationToken).ConfigureAwait(false);
            }

            var plannedHostnames = plan.Services
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, service.Name, StringComparison.OrdinalIgnoreCase))
                ?.CustomDomains
                ?? [];

            var customDomains = (domains?.CustomDomains ?? [])
                .Concat(snapshot.CustomDomainIds.Select(pair => new RailwayCustomDomain
                {
                    Id = pair.Value,
                    Domain = pair.Key
                }))
                .GroupBy(domain => domain.Domain, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());

            foreach (var customDomain in customDomains)
            {
                if (string.IsNullOrWhiteSpace(customDomain.Id) ||
                    string.IsNullOrWhiteSpace(customDomain.Domain))
                {
                    continue;
                }

                var planned = plannedHostnames.Any(hostname =>
                    string.Equals(hostname, customDomain.Domain, StringComparison.OrdinalIgnoreCase));
                if (!planned && !snapshot.CustomDomainIds.ContainsKey(customDomain.Domain))
                {
                    continue;
                }

                if (!ShouldDeleteCustomDomain(snapshot, customDomain.Domain, adoptedProject))
                {
                    Skip(
                        result,
                        reportingStep,
                        $"Custom domain `{customDomain.Domain}`",
                        AdoptedReason(customDomain.Domain));
                    continue;
                }

                await DeleteAsync(
                    result,
                    reportingStep,
                    $"Custom domain `{customDomain.Domain}`",
                    () => _client.CustomDomainDeleteAsync(customDomain.Id, request.Token, cancellationToken),
                    "customDomainDelete",
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DeleteCreatedServicesAsync(
        RailwayPlan plan,
        RailwayDestroyRequest request,
        RailwayDeploymentSnapshot snapshot,
        DestroyInventory inventory,
        string environmentId,
        bool adoptedProject,
        IReadOnlyList<RailwayNamedResource> otherEnvironments,
        bool appServicesOnly,
        RailwayDestroyResult result,
        IReportingStep reportingStep,
        CancellationToken cancellationToken)
    {
        var services = inventory.Services.Where(service =>
            appServicesOnly ? service.Kind == DestroyServiceKind.App : service.Kind == DestroyServiceKind.Managed);

        foreach (var service in services)
        {
            if (string.IsNullOrWhiteSpace(service.Id))
            {
                continue;
            }

            if (!IsCreatedService(plan, snapshot, service, adoptedProject))
            {
                Skip(result, reportingStep, $"Service `{service.Name}`", AdoptedReason(service.Name));
                continue;
            }

            if (otherEnvironments.Count > 0)
            {
                var others = string.Join(", ", otherEnvironments.Select(item => item.Name));
                Skip(
                    result,
                    reportingStep,
                    $"Service `{service.Name}`",
                    $"serviceDelete would remove this service from other Railway environments ({others}). " +
                    "The live schema deletes a non-fork service in every non-fork environment. " +
                    "Destroy the other environments first if this service should go.");
                continue;
            }

            await DeleteAsync(
                result,
                reportingStep,
                $"Service `{service.Name}`",
                () => _client.ServiceDeleteAsync(service.Id, environmentId, request.Token, cancellationToken),
                "serviceDelete",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DeleteCreatedEnvironmentAsync(
        RailwayPlan plan,
        RailwayDestroyRequest request,
        RailwayDeploymentSnapshot snapshot,
        string environmentId,
        bool adoptedEnvironment,
        RailwayDestroyResult result,
        IReportingStep reportingStep,
        CancellationToken cancellationToken)
    {
        if (adoptedEnvironment || snapshot.CreatedEnvironment != true)
        {
            Skip(
                result,
                reportingStep,
                $"Environment `{plan.RailwayEnvironmentName}`",
                adoptedEnvironment
                    ? "Adopted via AsExisting() / railway-environment-id; not deleted."
                    : "This integration did not create this environment (it was adopted or bundled with the project). Not deleted.");
            return;
        }

        await DeleteAsync(
            result,
            reportingStep,
            $"Environment `{plan.RailwayEnvironmentName}`",
            () => _client.EnvironmentDeleteAsync(environmentId, request.Token, cancellationToken),
            "environmentDelete",
            cancellationToken).ConfigureAwait(false);
    }

    private static void SkipBuckets(
        DestroyInventory inventory,
        RailwayDestroyResult result,
        IReportingStep reportingStep)
    {
        foreach (var bucket in inventory.Buckets)
        {
            Skip(
                result,
                reportingStep,
                $"Bucket `{bucket.Name}`",
                "Skipped: public GraphQL has no bucketDelete (only bucketCreate / bucketUpdate / bucketCredentialsReset). " +
                "The bucket is not treated as gone.");
        }
    }

    private static void SkipVolumes(
        RailwayDeploymentSnapshot snapshot,
        RailwayDestroyResult result,
        IReportingStep reportingStep)
    {
        if (snapshot.VolumeInstanceIds.Count == 0)
        {
            return;
        }

        Skip(
            result,
            reportingStep,
            "Volume instance(s)",
            "Skipped: this slice does not call volumeDelete or volumeInstanceBackupDelete. " +
            "serviceDelete cascade onto volumes is not proven; leftovers stay for a later slice.");
    }

    private static void SkipProjectDelete(
        string projectId,
        RailwayDestroyResult result,
        IReportingStep reportingStep) =>
        Skip(
            result,
            reportingStep,
            $"Project `{projectId}`",
            "Skipped: v1 does not call projectDelete. Blast radius is the mapped Railway environment, not the project.");

    private async Task DeleteAsync(
        RailwayDestroyResult result,
        IReportingStep reportingStep,
        string label,
        Func<Task<RailwayGraphQLResponse<System.Text.Json.JsonElement>>> send,
        string operationName,
        CancellationToken cancellationToken)
    {
        var task = await reportingStep.CreateTaskAsync(
            new MarkdownString($"Delete {label}"),
            cancellationToken).ConfigureAwait(false);
        await using (task.ConfigureAwait(false))
        {
            var response = await send().ConfigureAwait(false);
            RailwayGraphQLClient.ThrowIfFailed(response, operationName);
            result.Deleted.Add(label);
            await task.CompleteAsync(
                new MarkdownString($"Deleted {label} via `{operationName}`."),
                CompletionState.Completed,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static void Skip(
        RailwayDestroyResult result,
        IReportingStep reportingStep,
        string label,
        string reason)
    {
        var message = $"{label}: {reason}";
        result.Skipped.Add(message);
        reportingStep.Log(Microsoft.Extensions.Logging.LogLevel.Information, message);
    }

    private static async Task ReportInventoryAsync(
        RailwayPlan plan,
        string projectId,
        string environmentId,
        DestroyInventory inventory,
        IReportingStep reportingStep,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>
        {
            $"Destroy Railway environment **{plan.RailwayEnvironmentName}** (Aspire compute `{plan.ComputeEnvironment}`).",
            $"- Project: `{projectId}` ({inventory.ProjectName})",
            $"- Environment: `{environmentId}` (`{plan.RailwayEnvironmentName}`)",
            $"- Services: {FormatNames(inventory.Services.Select(item => item.Name))}",
            $"- Buckets: {FormatNames(inventory.Buckets.Select(item => item.Name))} (will be skipped; no public bucketDelete)",
            $"- Custom domains: {FormatNames(inventory.CustomDomainHostnames)}"
        };

        var task = await reportingStep.CreateTaskAsync(
            new MarkdownString("Railway destroy inventory"),
            cancellationToken).ConfigureAwait(false);
        await using (task.ConfigureAwait(false))
        {
            await task.CompleteAsync(
                new MarkdownString(string.Join("\n", lines)),
                CompletionState.Completed,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static DestroyInventory BuildInventory(
        RailwayPlan plan,
        RailwayDeploymentSnapshot snapshot,
        IReadOnlyList<RailwayNamedResource> liveServices,
        IReadOnlyList<RailwayNamedResource> liveBuckets,
        IReadOnlyList<RailwayNamedResource> liveEnvironments,
        RailwayProject project)
    {
        var inventory = new DestroyInventory
        {
            ProjectName = string.IsNullOrWhiteSpace(project.Name) ? plan.ComputeEnvironment : project.Name
        };

        foreach (var service in plan.Services)
        {
            inventory.Services.Add(new DestroyService(
                service.Name,
                ResolveServiceId(service.Name, snapshot.ServiceIds, liveServices),
                DestroyServiceKind.App));
            if (service.CustomDomains is { Count: > 0 })
            {
                inventory.CustomDomainHostnames.AddRange(service.CustomDomains);
            }
        }

        foreach (var managed in plan.ManagedServices)
        {
            if (string.Equals(managed.Kind, "bucket", StringComparison.OrdinalIgnoreCase))
            {
                var bucketId = FirstNonEmpty(
                    snapshot.BucketIds.GetValueOrDefault(managed.Name),
                    FindNamedId(liveBuckets, managed.Name));
                inventory.Buckets.Add(new DestroyNamed(managed.Name, bucketId));
                continue;
            }

            inventory.Services.Add(new DestroyService(
                managed.Name,
                ResolveServiceId(managed.Name, snapshot.ServiceIds, liveServices)
                    ?? ResolveServiceId(managed.TemplateCode, snapshot.ServiceIds, liveServices),
                DestroyServiceKind.Managed));
        }

        foreach (var pair in snapshot.ServiceIds)
        {
            if (inventory.Services.Any(item =>
                    string.Equals(item.Name, pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            inventory.Services.Add(new DestroyService(pair.Key, pair.Value, DestroyServiceKind.App));
        }

        foreach (var pair in snapshot.BucketIds)
        {
            if (inventory.Buckets.Any(item =>
                    string.Equals(item.Name, pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            inventory.Buckets.Add(new DestroyNamed(pair.Key, pair.Value));
        }

        foreach (var hostname in snapshot.CustomDomainIds.Keys)
        {
            if (!inventory.CustomDomainHostnames.Contains(hostname, StringComparer.OrdinalIgnoreCase))
            {
                inventory.CustomDomainHostnames.Add(hostname);
            }
        }

        _ = liveEnvironments;
        return inventory;
    }

    private static bool IsCreatedService(
        RailwayPlan plan,
        RailwayDeploymentSnapshot snapshot,
        DestroyService service,
        bool adoptedProject)
    {
        if (snapshot.CreatedServiceIds.ContainsKey(service.Name))
        {
            return true;
        }

        if (adoptedProject || plan.AdoptExisting)
        {
            return false;
        }

        if (snapshot.CreatedProject == true)
        {
            return true;
        }

        // Preview.11 state has ids but no created-vs-adopted flags. AppHost is
        // not AsExisting, so treat persisted service ids as created-by-us.
        return snapshot.CreatedProject is null &&
               snapshot.ServiceIds.ContainsKey(service.Name);
    }

    private static bool ShouldDeleteServiceDomain(
        RailwayDeploymentSnapshot snapshot,
        DestroyService service,
        RailwayServiceDomain domain,
        bool adoptedProject)
    {
        if (snapshot.CreatedServiceDomainIds.TryGetValue(service.Name, out var createdId) &&
            string.Equals(createdId, domain.Id, StringComparison.Ordinal))
        {
            return true;
        }

        if (adoptedProject)
        {
            return false;
        }

        return snapshot.CreatedServiceDomainIds.Count == 0 &&
               (snapshot.CreatedProject == true || snapshot.CreatedProject is null);
    }

    private static bool ShouldDeleteCustomDomain(
        RailwayDeploymentSnapshot snapshot,
        string hostname,
        bool adoptedProject)
    {
        if (snapshot.CreatedCustomDomainIds.ContainsKey(hostname))
        {
            return true;
        }

        if (adoptedProject)
        {
            return false;
        }

        return snapshot.CreatedCustomDomainIds.Count == 0 &&
               snapshot.CustomDomainIds.ContainsKey(hostname) &&
               (snapshot.CreatedProject == true || snapshot.CreatedProject is null);
    }

    private static string AdoptedReason(string name) =>
        $"`{name}` was adopted (AsExisting(), railway-project-id / railway-environment-id, or a live name match on a project this integration did not create). Not deleted.";

    private static string? ResolveServiceId(
        string? name,
        IReadOnlyDictionary<string, string> snapshotIds,
        IReadOnlyList<RailwayNamedResource> liveServices)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (snapshotIds.TryGetValue(name, out var id) && !string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        return FindNamedId(liveServices, name);
    }

    private static IReadOnlyList<RailwayNamedResource> NamedResources(RailwayNamedResourceConnection? connection)
    {
        if (connection?.Edges is null)
        {
            return [];
        }

        return connection.Edges
            .Select(edge => edge.Node)
            .Where(node => node is not null && !string.IsNullOrWhiteSpace(node.Id))
            .Cast<RailwayNamedResource>()
            .ToArray();
    }

    private static string? FindNamedId(IEnumerable<RailwayNamedResource> resources, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return resources.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    private static string FormatNames(IEnumerable<string?> names)
    {
        var values = names
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => $"`{name}`")
            .ToArray();
        return values.Length == 0 ? "(none)" : string.Join(", ", values);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private sealed class DestroyInventory
    {
        public string ProjectName { get; init; } = "";
        public List<DestroyService> Services { get; } = [];
        public List<DestroyNamed> Buckets { get; } = [];
        public List<string> CustomDomainHostnames { get; } = [];
    }

    private sealed record DestroyService(string Name, string? Id, DestroyServiceKind Kind);

    private sealed record DestroyNamed(string Name, string? Id);

    private enum DestroyServiceKind
    {
        App,
        Managed
    }
}
