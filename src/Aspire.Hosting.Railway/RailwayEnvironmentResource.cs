#pragma warning disable ASPIREPIPELINES001
#pragma warning disable ASPIREPIPELINES002
#pragma warning disable ASPIREPIPELINES004
#pragma warning disable ASPIRECOMPUTE002

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Railway project compute environment used by <c>aspire publish</c> and <c>aspire deploy</c>.
/// </summary>
public sealed class RailwayEnvironmentResource : Resource, IComputeEnvironmentResource
{
    /// <summary>
    /// Initializes a new Railway compute environment and registers pipeline steps.
    /// </summary>
    /// <param name="name">Aspire resource name for this environment.</param>
    public RailwayEnvironmentResource(string name)
        : base(name)
    {
        Annotations.Add(new PipelineStepAnnotation(CreatePipelineSteps));
        Annotations.Add(new PipelineConfigurationAnnotation(ConfigurePipeline));
    }

    /// <summary>
    /// Gets or sets an explicit Railway environment name. When unset, Aspire
    /// <c>--environment</c> is mapped with <see cref="RailwayEnvironmentNameMapper"/>.
    /// </summary>
    public string? RailwayEnvironmentName { get; set; }

    /// <summary>
    /// Gets or sets whether creating a <c>staging</c> environment should duplicate production
    /// via <c>environmentCreate(sourceEnvironmentId)</c> when production exists. Default is <see langword="true"/>.
    /// Empty create is opt-in through <see cref="CreateEmptyEnvironment"/>.
    /// </summary>
    public bool DuplicateProductionWhenCreatingStaging { get; set; } = true;

    /// <summary>
    /// Gets or sets whether a missing Railway environment should be created empty instead of
    /// duplicating production. Default is <see langword="false"/>.
    /// </summary>
    public bool CreateEmptyEnvironment { get; set; }

    /// <summary>
    /// Gets or sets the account/workspace token parameter. Project tokens cannot call <c>projectCreate</c>.
    /// </summary>
    public ParameterResource? TokenParameter { get; set; }

    /// <summary>
    /// Gets or sets the optional parameter used to adopt an existing Railway project.
    /// </summary>
    public ParameterResource? ProjectIdParameter { get; set; }

    /// <summary>
    /// Gets or sets the optional parameter used to adopt an existing Railway environment.
    /// </summary>
    public ParameterResource? EnvironmentIdParameter { get; set; }

    /// <summary>
    /// Gets a value indicating whether this environment adopts an existing Railway canvas.
    /// </summary>
    public bool IsExisting =>
        Annotations.OfType<RailwayExistingAnnotation>().Any() ||
        ProjectIdParameter is not null ||
        EnvironmentIdParameter is not null;

    /// <inheritdoc />
    [Experimental("ASPIRECOMPUTE002", UrlFormat = "https://aka.ms/aspire/diagnostics/{0}")]
    public ReferenceExpression GetHostAddressExpression(EndpointReference endpointReference)
    {
        ArgumentNullException.ThrowIfNull(endpointReference);

        var host = $"{GetRailwayServiceName(endpointReference.Resource)}.{RailwayConstants.PrivateDnsSuffix}";
        return ReferenceExpression.Create($"{host}");
    }

    /// <summary>
    /// Resolves the Railway service name (lowercase) for private DNS and reference variables.
    /// </summary>
    /// <param name="resource">The Aspire resource.</param>
    /// <returns>The Railway service name.</returns>
    public string GetRailwayServiceName(IResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var customization = resource.Annotations.OfType<RailwayServiceCustomizationAnnotation>().LastOrDefault();
        if (!string.IsNullOrWhiteSpace(customization?.ServiceName))
        {
            return customization.ServiceName.ToLowerInvariant();
        }

        return resource.Name.ToLowerInvariant();
    }

    /// <summary>
    /// Resolves the Railway environment name from an override or the Aspire environment name.
    /// </summary>
    /// <param name="aspireEnvironmentName">Aspire host environment name.</param>
    /// <returns>The Railway environment name.</returns>
    public string GetRailwayEnvironmentName(string? aspireEnvironmentName) =>
        !string.IsNullOrWhiteSpace(RailwayEnvironmentName)
            ? RailwayEnvironmentName
            : RailwayEnvironmentNameMapper.Map(aspireEnvironmentName);

    /// <summary>
    /// Resolves <see cref="IContainerRegistry"/> from this environment or the model.
    /// Railway has no image registry of its own.
    /// </summary>
    /// <param name="model">The application model.</param>
    /// <returns>The registry, or <see langword="null"/> if none is configured.</returns>
    public IContainerRegistry? ResolveContainerRegistry(DistributedApplicationModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var reference = Annotations.OfType<ContainerRegistryReferenceAnnotation>().LastOrDefault();
        if (reference is not null)
        {
            return reference.Registry;
        }

        var registries = model.Resources.OfType<IContainerRegistry>().ToArray();
        return registries.Length == 1 ? registries[0] : null;
    }

    internal IReadOnlyList<PipelineStep> CreatePipelineSteps(PipelineStepFactoryContext factoryContext)
    {
        var steps = new List<PipelineStep>();

        var prepare = new PipelineStep
        {
            Name = $"prepare-deployment-targets-{Name}",
            Description = $"Prepares Railway deployment targets for {Name}.",
            Action = PrepareDeploymentTargetsAsync,
            DependsOnSteps = [WellKnownPipelineSteps.ValidateComputeEnvironments],
            RequiredBySteps = [WellKnownPipelineSteps.BeforeStart],
            Resource = this
        };
        steps.Add(prepare);

        var publish = new PipelineStep
        {
            Name = $"publish-{Name}",
            Description = $"Publishes the Railway plan for {Name}.",
            Action = PublishAsync,
            Resource = this
        };
        publish.DependsOn(WellKnownPipelineSteps.PublishPrereq);
        publish.RequiredBy(WellKnownPipelineSteps.Publish);
        steps.Add(publish);

        var deploy = new PipelineStep
        {
            Name = $"deploy-{Name}",
            Description = $"Deploys resources to Railway for {Name}.",
            Action = DeployAsync,
            Tags = [WellKnownPipelineTags.DeployCompute],
            DependsOnSteps = [WellKnownPipelineSteps.DeployPrereq, $"publish-{Name}"],
            Resource = this
        };
        deploy.RequiredBy(WellKnownPipelineSteps.Deploy);
        steps.Add(deploy);

        var destroy = new PipelineStep
        {
            Name = $"destroy-{Name}",
            Description = $"Destroys the Railway environment {Name}.",
            Action = DestroyAsync,
            DependsOnSteps = [WellKnownPipelineSteps.DestroyPrereq],
            Resource = this
        };
        destroy.RequiredBy(WellKnownPipelineSteps.Destroy);
        steps.Add(destroy);

        return steps;
    }

    internal void ConfigurePipeline(PipelineConfigurationContext context)
    {
        foreach (var computeResource in context.Model.GetBuildResources())
        {
            var buildSteps = context.GetSteps(computeResource, WellKnownPipelineTags.BuildCompute);
            buildSteps.RequiredBy(WellKnownPipelineSteps.Deploy)
                .RequiredBy($"deploy-{Name}")
                .DependsOn(WellKnownPipelineSteps.DeployPrereq);
        }

        foreach (var pushResource in context.Model.GetBuildAndPushResources())
        {
            var pushSteps = context.GetSteps(pushResource, WellKnownPipelineTags.PushContainerImage);
            var deploySteps = context.GetSteps(this, WellKnownPipelineTags.DeployCompute);
            deploySteps.DependsOn(pushSteps);
        }
    }

    internal Task PrepareDeploymentTargetsAsync(PipelineStepContext context)
    {
        if (context.ExecutionContext.IsRunMode)
        {
            return Task.CompletedTask;
        }

        foreach (var resource in context.Model.GetComputeResources())
        {
            if (resource.Annotations.OfType<IRailwayManagedServiceAnnotation>().Any())
            {
                continue;
            }

            var assigned = resource.GetComputeEnvironment();
            if (assigned is not null && !ReferenceEquals(assigned, this))
            {
                continue;
            }

            var service = new RailwayServiceResource(resource.Name, resource, this)
            {
                RailwayServiceName = GetRailwayServiceName(resource)
            };

            var customization = resource.Annotations.OfType<RailwayServiceCustomizationAnnotation>().LastOrDefault();
            customization?.Configure?.Invoke(service);

            resource.Annotations.Add(new DeploymentTargetAnnotation(service)
            {
                ComputeEnvironment = this,
                ContainerRegistry = ResolveContainerRegistry(context.Model)
            });
        }

        return Task.CompletedTask;
    }

    internal async Task PublishAsync(PipelineStepContext context)
    {
        var outputPath = GetOutputDirectory(context);
        Directory.CreateDirectory(outputPath);

        var aspireEnvironment = context.Services.GetService<IHostEnvironment>()?.EnvironmentName;
        var plan = RailwayPlanBuilder.Create(context.Model, this, aspireEnvironment);
        var json = RailwayPlanBuilder.ToJson(plan);

        var planPath = Path.Combine(outputPath, "railway-plan.json");
        await File.WriteAllTextAsync(planPath, json, context.CancellationToken).ConfigureAwait(false);

        var envExamplePath = Path.Combine(outputPath, ".env.example");
        await File.WriteAllTextAsync(envExamplePath, RailwayPlanBuilder.ToEnvExample(plan), context.CancellationToken)
            .ConfigureAwait(false);

        var task = await context.ReportingStep.CreateTaskAsync(
            new MarkdownString($"Wrote Railway plan for **{Name}**"),
            context.CancellationToken).ConfigureAwait(false);
        await using (task.ConfigureAwait(false))
        {
            await task.CompleteAsync(
                new MarkdownString($"Published `{Path.GetFileName(planPath)}` (parameter names and expressions only)."),
                CompletionState.Completed,
                context.CancellationToken).ConfigureAwait(false);
        }

        context.Summary.Add("🚂 Railway plan", planPath);
    }

    internal async Task DeployAsync(PipelineStepContext context)
    {
        var imageBasedServices = context.Model.GetComputeResources()
            .Where(resource => resource.GetDeploymentTargetAnnotation(this) is not null)
            .Where(resource => !resource.Annotations.OfType<IRailwayManagedServiceAnnotation>().Any())
            .ToArray();

        if (imageBasedServices.Length > 0 && ResolveContainerRegistry(context.Model) is null)
        {
            throw new InvalidOperationException(
                "Railway has no container image registry. Add GHCR or Docker Hub with " +
                "builder.AddContainerRegistry(\"ghcr\", \"ghcr.io\") and associate it with the Railway " +
                "environment (for example railway.WithContainerRegistry(ghcr)) before deploying " +
                "image-based services. Do not use `railway up`.");
        }

        var aspireEnvironment = context.Services.GetService<IHostEnvironment>()?.EnvironmentName;
        var plan = RailwayPlanBuilder.Create(context.Model, this, aspireEnvironment);
        var token = await ResolveTokenAsync(context).ConfigureAwait(false);
        var request = await CreateApplyRequestAsync(context, plan, token).ConfigureAwait(false);

        var client = CreateGraphQLClient(context.Services);
        var apply = new RailwayGraphQLApplyService(client);
        var stateManager = context.Services.GetService<IDeploymentStateManager>();

        try
        {
            var result = await apply.ApplyAsync(
                plan,
                request,
                context.ReportingStep,
                stateManager,
                context.CancellationToken).ConfigureAwait(false);

            var summary = result.CreatedProject
                ? $"Created Railway project `{result.ProjectId}`"
                : $"Adopted Railway project `{result.ProjectId}`";
            context.Summary.Add("🚂 Railway project", result.ProjectId);
            context.Summary.Add("🌿 Railway environment", result.EnvironmentId);
            if (result.Warnings.Count > 0)
            {
                await context.ReportingStep.CompleteAsync(
                    new MarkdownString($"{summary} with warnings."),
                    CompletionState.CompletedWithWarning,
                    context.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                await context.ReportingStep.CompleteAsync(
                    new MarkdownString($"{summary} / environment `{result.EnvironmentId}`."),
                    CompletionState.Completed,
                    context.CancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await context.ReportingStep.CompleteAsync(
                exception.Message,
                CompletionState.CompletedWithError,
                context.CancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    internal async Task DestroyAsync(PipelineStepContext context)
    {
        var stateManager = context.Services.GetService<IDeploymentStateManager>();
        if (stateManager is not null)
        {
            var section = await stateManager.AcquireSectionAsync($"Railway:{Name}", context.CancellationToken)
                .ConfigureAwait(false);
            if (section.Data.Count == 0)
            {
                var emptyTask = await context.ReportingStep.CreateTaskAsync(
                    new MarkdownString($"No Railway deployment state for **{Name}**"),
                    context.CancellationToken).ConfigureAwait(false);
                await using (emptyTask.ConfigureAwait(false))
                {
                    await emptyTask.CompleteAsync(
                        "Nothing to destroy.",
                        CompletionState.Completed,
                        context.CancellationToken).ConfigureAwait(false);
                }

                return;
            }
        }

        var task = await context.ReportingStep.CreateTaskAsync(
            new MarkdownString($"Destroy Railway environment **{Name}**"),
            context.CancellationToken).ConfigureAwait(false);
        await using (task.ConfigureAwait(false))
        {
            await task.CompleteAsync(
                "Railway GraphQL destroy is not implemented. Confirmed operations do not include project or environment delete; this step does not invent those mutations.",
                CompletionState.CompletedWithWarning,
                context.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes project and environment ids only. GraphQL apply persists the full id set through
    /// <c>RailwayDeploymentStateStore</c> after each successful item; <see cref="DeployAsync"/>
    /// does not call this again.
    /// </summary>
    internal static async Task PersistDeploymentIdsAsync(
        IDeploymentStateManager stateManager,
        string environmentName,
        string projectId,
        string railwayEnvironmentId,
        CancellationToken cancellationToken)
    {
        var section = await stateManager.AcquireSectionAsync($"Railway:{environmentName}", cancellationToken)
            .ConfigureAwait(false);
        section.Data["ProjectId"] = JsonValue.Create(projectId);
        section.Data["EnvironmentId"] = JsonValue.Create(railwayEnvironmentId);
        await stateManager.SaveSectionAsync(section, cancellationToken).ConfigureAwait(false);
    }

    internal static RailwayGraphQLClient CreateGraphQLClient(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.GetService<RailwayGraphQLClient>() is { } existing)
        {
            return existing;
        }

        var factory = services.GetService<IHttpClientFactory>();
        var httpClient = factory?.CreateClient(RailwayGraphQLClient.HttpClientName) ?? new HttpClient();
        return new RailwayGraphQLClient(httpClient);
    }

    private async Task<string> ResolveTokenAsync(PipelineStepContext context)
    {
        if (TokenParameter is not null)
        {
            var fromParameter = await TokenParameter.GetValueAsync(context.CancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(fromParameter))
            {
                return fromParameter;
            }
        }

        var configuration = context.Services.GetService<IConfiguration>();
        var fromConfiguration = configuration?[RailwayConstants.TokenConfigurationKey]
            ?? configuration?[RailwayConstants.ApiTokenEnvironmentVariableName];
        if (!string.IsNullOrWhiteSpace(fromConfiguration))
        {
            return fromConfiguration;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(RailwayConstants.TokenConfigurationKey)
            ?? Environment.GetEnvironmentVariable(RailwayConstants.ApiTokenEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        throw new InvalidOperationException(
            "An account or workspace Railway token is required for aspire deploy. " +
            "Set RAILWAY_TOKEN (or RAILWAY_API_TOKEN in CI). Project tokens cannot call projectCreate.");
    }

    private async Task<RailwayApplyRequest> CreateApplyRequestAsync(
        PipelineStepContext context,
        RailwayPlan plan,
        string token)
    {
        var adoptedProjectId = await TryGetParameterValueAsync(ProjectIdParameter, context.CancellationToken)
            .ConfigureAwait(false);
        var adoptedEnvironmentId = await TryGetParameterValueAsync(EnvironmentIdParameter, context.CancellationToken)
            .ConfigureAwait(false);

        if (IsExisting && (string.IsNullOrWhiteSpace(adoptedProjectId) || string.IsNullOrWhiteSpace(adoptedEnvironmentId)))
        {
            throw new InvalidOperationException(
                "AsExisting requires railway-project-id and railway-environment-id " +
                $"(configuration keys {RailwayConstants.ProjectIdConfigurationKey} / {RailwayConstants.EnvironmentIdConfigurationKey}).");
        }

        var request = new RailwayApplyRequest
        {
            Token = token,
            AdoptedProjectId = adoptedProjectId,
            AdoptedEnvironmentId = adoptedEnvironmentId,
            DuplicateProductionWhenCreatingStaging = DuplicateProductionWhenCreatingStaging,
            CreateEmptyEnvironment = CreateEmptyEnvironment
        };

        foreach (var service in plan.Services)
        {
            var resource = context.Model.Resources.FirstOrDefault(candidate =>
                string.Equals(GetRailwayServiceName(candidate), service.Name, StringComparison.OrdinalIgnoreCase));
            if (resource is not null &&
                resource.TryGetContainerImageName(out var imageName) &&
                !string.IsNullOrWhiteSpace(imageName))
            {
                request.ServiceImages[service.Name] = imageName;
            }
            else if (!string.IsNullOrWhiteSpace(service.Image) && !service.Image.StartsWith('{'))
            {
                request.ServiceImages[service.Name] = service.Image;
            }

            if (resource is not null && HasExternalHttpEndpoint(resource))
            {
                request.ExternalHttpServices.Add(service.Name);
            }

            await ResolveServiceEnvironmentAsync(context, service, request).ConfigureAwait(false);
        }

        return request;
    }

    private static async Task ResolveServiceEnvironmentAsync(
        PipelineStepContext context,
        RailwayPlanService service,
        RailwayApplyRequest request)
    {
        Dictionary<string, string>? resolved = null;
        foreach (var pair in service.Environment)
        {
            if (RailwayPlanBuilder.IsRailwayReferenceExpression(pair.Value))
            {
                continue;
            }

            var resolvedValue = await TryResolveEnvironmentValueAsync(
                    context,
                    service.Name,
                    pair.Key,
                    pair.Value)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(resolvedValue))
            {
                throw new InvalidOperationException(
                    $"Cannot resolve '{pair.Key}' for Railway service '{service.Name}'. " +
                    $"The plan captured parameter '{pair.Value}' but no connection string or parameter value was available.");
            }

            resolved ??= new Dictionary<string, string>(StringComparer.Ordinal);
            resolved[pair.Key] = resolvedValue;
        }

        if (resolved is not null)
        {
            request.ResolvedServiceEnvironment[service.Name] = resolved;
        }
    }

    private static async Task<string?> TryResolveEnvironmentValueAsync(
        PipelineStepContext context,
        string serviceName,
        string environmentKey,
        string planValue)
    {
        var resourceName = RailwayPlanBuilder.TryGetConnectionStringResourceName(environmentKey);
        if (!string.IsNullOrWhiteSpace(resourceName))
        {
            var connectionStringResource = context.Model.Resources
                .OfType<IResourceWithConnectionString>()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, resourceName, StringComparison.OrdinalIgnoreCase));
            if (connectionStringResource is not null)
            {
                try
                {
                    var connectionString = await connectionStringResource
                        .GetConnectionStringAsync(context.CancellationToken)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(connectionString))
                    {
                        return connectionString;
                    }
                }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidOperationException(
                        $"Cannot resolve connection string '{resourceName}' for Railway service '{serviceName}': {exception.Message}",
                        exception);
                }
            }
        }

        var parameter = context.Model.Resources.OfType<ParameterResource>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, planValue, StringComparison.OrdinalIgnoreCase));
        return await TryGetParameterValueAsync(parameter, context.CancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> TryGetParameterValueAsync(
        ParameterResource? parameter,
        CancellationToken cancellationToken)
    {
        if (parameter is null)
        {
            return null;
        }

        try
        {
            var value = await parameter.GetValueAsync(cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool HasExternalHttpEndpoint(IResource resource)
    {
        if (!resource.TryGetEndpoints(out var endpoints))
        {
            return false;
        }

        return endpoints.Any(endpoint =>
            endpoint.IsExternal &&
            (string.Equals(endpoint.UriScheme, "http", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(endpoint.UriScheme, "https", StringComparison.OrdinalIgnoreCase)));
    }

    private string GetOutputDirectory(PipelineStepContext context)
    {
        var output = context.Services.GetService<IPipelineOutputService>();
        if (output is not null)
        {
            return output.GetOutputDirectory(this);
        }

        var options = context.Services.GetService<IOptions<PipelineOptions>>()?.Value.OutputPath;
        if (!string.IsNullOrWhiteSpace(options))
        {
            return Path.Combine(options, Name);
        }

        return Path.Combine(Path.GetTempPath(), "aspire-railway", Name);
    }
}
