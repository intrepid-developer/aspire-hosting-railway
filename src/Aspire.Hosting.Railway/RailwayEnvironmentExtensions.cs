#pragma warning disable ASPIREPIPELINES001

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Railway;

using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting;

/// <summary>
/// AppHost extensions for the Railway compute environment.
/// </summary>
public static class RailwayEnvironmentExtensions
{
    /// <summary>
    /// Registers Railway pipeline validation once. Safe to call from satellite packages.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <returns>The same builder.</returns>
    public static IDistributedApplicationBuilder AddRailwayInfrastructureCore(this IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(RailwayPipelineStepMarker)))
        {
            return builder;
        }

        builder.Services.AddSingleton<RailwayPipelineStepMarker>();
        builder.Services.AddHttpClient(RailwayGraphQLClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(RailwayConstants.GraphQLEndpoint);
        });

        builder.Pipeline.AddStep(
            name: RailwayPipelineStepMarker.StepName,
            action: context =>
            {
                if (!context.ExecutionContext.IsPublishMode)
                {
                    return Task.CompletedTask;
                }

                if (context.Model.Resources.OfType<RailwayEnvironmentResource>().Any())
                {
                    return Task.CompletedTask;
                }

                foreach (var resource in context.Model.Resources)
                {
                    if (resource.Annotations.OfType<IRailwayManagedServiceAnnotation>().Any() ||
                        resource.Annotations.OfType<RailwayServiceCustomizationAnnotation>().Any())
                    {
                        throw new InvalidOperationException(
                            $"Resource '{resource.Name}' is configured to publish as a Railway service, but there are no '{nameof(RailwayEnvironmentResource)}' resources. Ensure you have added one by calling '{nameof(AddRailwayEnvironment)}'.");
                    }
                }

                return Task.CompletedTask;
            },
            requiredBy: WellKnownPipelineSteps.BeforeStart);

        return builder;
    }

    /// <summary>
    /// Adds a Railway project as the Aspire compute environment.
    /// In run mode the environment is not added to the model (it stays off the dashboard and never talks to Railway).
    /// In publish mode it is added so pipeline steps can run.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Aspire resource name for the environment.</param>
    /// <returns>A builder for the Railway environment.</returns>
    public static IResourceBuilder<RailwayEnvironmentResource> AddRailwayEnvironment(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        builder.AddRailwayInfrastructureCore();

        var resource = new RailwayEnvironmentResource(name);
        var resourceBuilder = builder.ExecutionContext.IsRunMode
            ? builder.CreateResourceBuilder(resource)
            : builder.AddResource(resource);

        var token = builder.AddParameter(RailwayConstants.TokenParameterName, secret: true);
        resource.TokenParameter = token.Resource;

        return resourceBuilder;
    }

    /// <summary>
    /// Allows setting properties on the Railway environment resource.
    /// </summary>
    /// <param name="builder">The environment builder.</param>
    /// <param name="configure">Callback that receives the environment resource.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<RailwayEnvironmentResource> WithProperties(
        this IResourceBuilder<RailwayEnvironmentResource> builder,
        Action<RailwayEnvironmentResource> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        configure(builder.Resource);
        return builder;
    }

    /// <summary>
    /// Overrides the Railway environment name that would otherwise be derived from Aspire <c>--environment</c>.
    /// </summary>
    /// <param name="builder">The environment builder.</param>
    /// <param name="railwayEnvironmentName">Railway environment name, for example <c>production</c>.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<RailwayEnvironmentResource> WithRailwayEnvironmentName(
        this IResourceBuilder<RailwayEnvironmentResource> builder,
        string railwayEnvironmentName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(railwayEnvironmentName);

        builder.Resource.RailwayEnvironmentName = railwayEnvironmentName;
        return builder;
    }

    /// <summary>
    /// Adopts an existing Railway project and environment. Re-deploy must not create a second project;
    /// IDs are also persisted in <see cref="IDeploymentStateManager"/> once GraphQL apply is implemented.
    /// </summary>
    /// <param name="builder">The environment builder.</param>
    /// <param name="projectId">Parameter named <c>RAILWAY_PROJECT_ID</c> (or equivalent).</param>
    /// <param name="environmentId">Parameter named <c>RAILWAY_ENVIRONMENT_ID</c> (or equivalent).</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<RailwayEnvironmentResource> AsExisting(
        this IResourceBuilder<RailwayEnvironmentResource> builder,
        IResourceBuilder<ParameterResource> projectId,
        IResourceBuilder<ParameterResource> environmentId)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentNullException.ThrowIfNull(environmentId);

        builder.Resource.ProjectIdParameter = projectId.Resource;
        builder.Resource.EnvironmentIdParameter = environmentId.Resource;
        return builder.WithAnnotation(new RailwayExistingAnnotation(projectId.Resource, environmentId.Resource));
    }

    /// <summary>
    /// Adopts an existing Railway canvas using the conventional
    /// <c>RAILWAY_PROJECT_ID</c> and <c>RAILWAY_ENVIRONMENT_ID</c> parameters.
    /// </summary>
    /// <param name="builder">The environment builder.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<RailwayEnvironmentResource> AsExisting(
        this IResourceBuilder<RailwayEnvironmentResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var projectId = builder.ApplicationBuilder.AddParameter(RailwayConstants.ProjectIdParameterName);
        var environmentId = builder.ApplicationBuilder.AddParameter(RailwayConstants.EnvironmentIdParameterName);
        return builder.AsExisting(projectId, environmentId);
    }

    /// <summary>
    /// Stores a Railway service customization. The environment materializes a
    /// <see cref="RailwayServiceResource"/> during prepare-deployment-targets.
    /// </summary>
    /// <typeparam name="T">The compute resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="configure">Optional customization applied to the materialized service.</param>
    /// <returns>The same resource builder.</returns>
    public static IResourceBuilder<T> PublishAsRailwayService<T>(
        this IResourceBuilder<T> builder,
        Action<RailwayServiceResource>? configure = null)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ApplicationBuilder.AddRailwayInfrastructureCore();

        if (!builder.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return builder;
        }

        return builder.WithAnnotation(new RailwayServiceCustomizationAnnotation(configure));
    }

    private sealed class RailwayPipelineStepMarker
    {
        public const string StepName = "validate-railway";
    }
}
