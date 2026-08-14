#pragma warning disable ASPIREPIPELINES001
#pragma warning disable ASPIREPIPELINES002
#pragma warning disable ASPIREPIPELINES004
#pragma warning disable ASPIRECOMPUTE002

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;

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
        var reporter = context.Services.GetService<IPipelineActivityReporter>();
        var extraStep = reporter is not null
            ? await reporter.CreateStepAsync("Railway GraphQL apply", context.CancellationToken).ConfigureAwait(false)
            : null;

        try
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

            var message =
                "Railway GraphQL apply is not implemented yet. Publish wrote railway-plan.json; " +
                "a later PR will call projectCreate / environmentCreate / serviceCreate / " +
                "templateDeployV2 / bucketCreate. This step does not contact Railway and does not report a successful apply.";

            var task = await context.ReportingStep.CreateTaskAsync(
                new MarkdownString("Railway GraphQL apply"),
                context.CancellationToken).ConfigureAwait(false);
            await using (task.ConfigureAwait(false))
            {
                await task.CompleteAsync(
                    new MarkdownString(message),
                    CompletionState.CompletedWithWarning,
                    context.CancellationToken).ConfigureAwait(false);
            }

            if (extraStep is not null)
            {
                await extraStep.CompleteAsync(message, CompletionState.CompletedWithWarning, context.CancellationToken)
                    .ConfigureAwait(false);
            }

            context.Summary.Add("🚂 Railway deploy", "GraphQL apply not implemented");
        }
        finally
        {
            if (extraStep is not null)
            {
                await extraStep.DisposeAsync().ConfigureAwait(false);
            }
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
                        "Nothing to destroy. GraphQL teardown is not implemented yet.",
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
                "Railway GraphQL destroy is not implemented yet.",
                CompletionState.CompletedWithWarning,
                context.CancellationToken).ConfigureAwait(false);
        }
    }

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
