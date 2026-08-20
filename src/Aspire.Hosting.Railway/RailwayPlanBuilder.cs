using System.Text.Json;

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Railway;

/// <summary>
/// Builds a secret-safe <see cref="RailwayPlan"/> from the application model.
/// </summary>
public static class RailwayPlanBuilder
{
    private const string ConnectionStringPrefix = "ConnectionStrings__";
    private const string ReferenceRelationshipType = "Reference";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Creates a plan that contains expressions and parameter names only.
    /// </summary>
    /// <param name="model">The application model.</param>
    /// <param name="environment">The Railway compute environment.</param>
    /// <param name="aspireEnvironmentName">Aspire <c>--environment</c> name used to map the Railway environment.</param>
    /// <returns>The secret-safe plan.</returns>
    public static RailwayPlan Create(
        DistributedApplicationModel model,
        RailwayEnvironmentResource environment,
        string? aspireEnvironmentName)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(environment);

        var plan = new RailwayPlan
        {
            ComputeEnvironment = environment.Name,
            RailwayEnvironmentName = environment.GetRailwayEnvironmentName(aspireEnvironmentName),
            AdoptExisting = environment.IsExisting,
            DuplicateProductionWhenCreatingStaging = environment.DuplicateProductionWhenCreatingStaging,
            CreateEmptyEnvironment = environment.CreateEmptyEnvironment
        };

        AddParameterName(plan, RailwayConstants.TokenConfigurationKey);
        if (environment.ProjectIdParameter is not null)
        {
            AddParameterName(plan, RailwayConstants.ProjectIdConfigurationKey);
        }

        if (environment.EnvironmentIdParameter is not null)
        {
            AddParameterName(plan, RailwayConstants.EnvironmentIdConfigurationKey);
        }

        var registry = environment.ResolveContainerRegistry(model);
        if (registry is not null)
        {
            plan.ContainerRegistryEndpoint = registry.Endpoint.ValueExpression;
        }

        foreach (var resource in model.Resources)
        {
            foreach (var managed in resource.Annotations.OfType<IRailwayManagedServiceAnnotation>())
            {
                RejectScaleOnVolumeBackedResource(resource, managed);
                plan.ManagedServices.Add(new RailwayPlanManagedService
                {
                    Name = managed.ServiceName,
                    Kind = managed.Kind,
                    TemplateCode = managed.TemplateCode,
                    PrivateReferenceVariable = managed.PrivateReferenceVariable
                });
            }
        }

        foreach (var resource in model.GetComputeResources())
        {
            if (resource.Annotations.OfType<IRailwayManagedServiceAnnotation>().Any())
            {
                continue;
            }

            var assigned = resource.GetComputeEnvironment();
            if (assigned is not null && !ReferenceEquals(assigned, environment))
            {
                continue;
            }

            var serviceName = environment.GetRailwayServiceName(resource);
            var service = new RailwayPlanService
            {
                Name = serviceName
            };

            if (resource.TryGetContainerImageName(out var imageName))
            {
                service.Image = imageName;
            }
            else
            {
                service.Image = $"{{{resource.Name}.containerImage}}";
            }

            CopyComputeSettings(service, resource, environment);
            AddCapturedEnvironment(plan, service, resource, model);
            AddReferencedConnectionStrings(plan, service, resource);
            plan.Services.Add(service);
        }

        RailwayServiceComputeSettings.ValidatePlanServices(plan);
        return plan;
    }

    /// <summary>
    /// Serializes <paramref name="plan"/> to JSON. The payload must not contain secret values.
    /// </summary>
    /// <param name="plan">The plan to serialize.</param>
    /// <returns>Indented JSON.</returns>
    public static string ToJson(RailwayPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(plan, s_jsonOptions);
    }

    /// <summary>
    /// Writes a <c>.env.example</c>-style file that lists captured parameter names with empty values.
    /// </summary>
    /// <param name="plan">The plan whose parameter names should be listed.</param>
    /// <returns>Example env file contents.</returns>
    public static string ToEnvExample(RailwayPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var lines = new List<string>
        {
            "# Generated by IntrepidDeveloper.Aspire.Hosting.Railway. Parameter names only — never commit values.",
            ""
        };

        foreach (var name in plan.Parameters.Distinct(StringComparer.Ordinal))
        {
            lines.Add($"{name}=");
        }

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>
    /// Returns whether <paramref name="value"/> is a Railway <c>${{service.VAR}}</c> expression
    /// (or a connection string composed of those expressions).
    /// </summary>
    public static bool IsRailwayReferenceExpression(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains("${{", StringComparison.Ordinal);

    /// <summary>
    /// Returns the referenced resource name for a <c>ConnectionStrings__{name}</c> variable, or
    /// <see langword="null"/> when the key is not a connection-string variable.
    /// </summary>
    public static string? TryGetConnectionStringResourceName(string environmentKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentKey);
        return environmentKey.StartsWith(ConnectionStringPrefix, StringComparison.Ordinal)
            ? environmentKey[ConnectionStringPrefix.Length..]
            : null;
    }

    private static void RejectScaleOnVolumeBackedResource(
        IResource resource,
        IRailwayManagedServiceAnnotation managed)
    {
        if (string.IsNullOrWhiteSpace(managed.TemplateCode))
        {
            return;
        }

        if (!resource.TryGetLastAnnotation<ReplicaAnnotation>(out _) &&
            !resource.Annotations.OfType<RailwayServiceCustomizationAnnotation>().Any())
        {
            return;
        }

        throw new InvalidOperationException(
            $"Railway {managed.Kind} '{managed.ServiceName}' is volume-backed and cannot be scaled. " +
            "Replicas cannot be used with volumes (https://docs.railway.com/volumes/reference). " +
            "Do not set WithReplicas or PublishAsRailwayService scale/region/cpu/memory on PublishAsRailwayPostgres / PublishAsRailwayRedis.");
    }

    private static void CopyComputeSettings(
        RailwayPlanService service,
        IResource resource,
        RailwayEnvironmentResource environment)
    {
        if (resource.TryGetLastAnnotation<ReplicaAnnotation>(out _))
        {
            service.Replicas = resource.GetReplicaCount();
        }

        var railwayService = GetConfiguredRailwayService(resource, environment);
        if (railwayService is null)
        {
            return;
        }

        if (railwayService.Region is { } region)
        {
            service.Region = RailwayRegionMapper.ToRegionId(region);
        }

        if (railwayService.Serverless is { } serverless)
        {
            service.Serverless = serverless;
        }

        if (railwayService.ReplicaRegions is { Count: > 0 } replicaRegions)
        {
            service.ReplicaRegions = RailwayRegionMapper.ToOfficialReplicaRegions(replicaRegions);
        }

        if (railwayService.Cpu is { } cpu)
        {
            service.Cpu = cpu;
        }

        if (railwayService.MemoryGb is { } memoryGb)
        {
            service.MemoryGb = memoryGb;
        }
    }

    private static RailwayServiceResource? GetConfiguredRailwayService(
        IResource resource,
        RailwayEnvironmentResource environment)
    {
        if (resource.GetDeploymentTargetAnnotation(environment)?.DeploymentTarget is RailwayServiceResource prepared)
        {
            return prepared;
        }

        var customization = resource.Annotations.OfType<RailwayServiceCustomizationAnnotation>().LastOrDefault();
        if (customization?.Configure is null)
        {
            return null;
        }

        var service = new RailwayServiceResource(resource.Name, resource, environment)
        {
            RailwayServiceName = environment.GetRailwayServiceName(resource)
        };
        customization.Configure(service);
        return service;
    }

    private static void AddCapturedEnvironment(
        RailwayPlan plan,
        RailwayPlanService service,
        IResource resource,
        DistributedApplicationModel model)
    {
        if (!resource.TryGetEnvironmentVariables(out var annotations) || annotations is null)
        {
            return;
        }

        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        var executionContext = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish);
        var callbackContext = new EnvironmentCallbackContext(
            executionContext,
            resource,
            values,
            CancellationToken.None);

        foreach (var annotation in annotations)
        {
            try
            {
                annotation.Callback(callbackContext).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
                // Publish-mode callbacks may not be able to resolve run-time endpoints.
            }
        }

        foreach (var pair in values)
        {
            CaptureEnvironmentValue(plan, service, model, pair.Key, pair.Value);
        }
    }

    private static void CaptureEnvironmentValue(
        RailwayPlan plan,
        RailwayPlanService service,
        DistributedApplicationModel model,
        string key,
        object? value)
    {
        switch (value)
        {
            case ParameterResource parameter:
                AddParameterName(plan, parameter.Name);
                service.Environment[key] = parameter.Name;
                break;

            case string text when !string.IsNullOrWhiteSpace(text):
                service.Environment[key] = text;
                break;

            case IManifestExpressionProvider expression:
                var expressionText = expression.ValueExpression;
                if (IsRailwayReferenceExpression(expressionText))
                {
                    service.Environment[key] = expressionText;
                    break;
                }

                if (TryGetParameterNameFromExpression(expressionText, out var parameterName)
                    && IsParameterResource(model, parameterName))
                {
                    AddParameterName(plan, parameterName);
                    service.Environment[key] = parameterName;
                }

                break;
        }
    }

    /// <summary>
    /// Returns a resolved environment value. Captured Aspire parameters are looked
    /// up; everything else (for example <c>OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY=in_memory</c>)
    /// is a literal.
    /// </summary>
    /// <summary>
    /// Resolves a captured environment value. Returns the parameter value when
    /// present, an empty string when the parameter exists but is blank (omit on
    /// deploy), <see langword="null"/> when a required captured parameter is
    /// missing, or the literal plan value otherwise.
    /// </summary>
    internal static string? CoalesceCapturedEnvironmentValue(
        string planValue,
        bool valueRead,
        string? resolvedParameterValue,
        IReadOnlyCollection<string> capturedParameterNames)
    {
        if (!string.IsNullOrWhiteSpace(resolvedParameterValue))
        {
            return resolvedParameterValue;
        }

        if (valueRead)
        {
            return "";
        }

        if (capturedParameterNames.Any(name =>
                string.Equals(name, planValue, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return planValue;
    }

    private static bool IsParameterResource(DistributedApplicationModel model, string name) =>
        model.Resources.OfType<ParameterResource>()
            .Any(parameter => string.Equals(parameter.Name, name, StringComparison.Ordinal));

    private static bool TryGetParameterNameFromExpression(string expression, out string name)
    {
        name = "";
        if (string.IsNullOrWhiteSpace(expression) ||
            expression[0] != '{' ||
            !expression.EndsWith(".value}", StringComparison.Ordinal))
        {
            return false;
        }

        name = expression[1..^".value}".Length];
        return !string.IsNullOrWhiteSpace(name);
    }

    private static void AddReferencedConnectionStrings(
        RailwayPlan plan,
        RailwayPlanService service,
        IResource resource)
    {
        foreach (var referenced in GetReferencedResources(resource))
        {
            var managed = FindManagedAnnotation(referenced);
            if (managed is not null && !string.IsNullOrWhiteSpace(managed.PrivateReferenceVariable))
            {
                service.Environment[$"{ConnectionStringPrefix}{referenced.Name}"] =
                    RailwayReferenceExpressions.PrivateServiceVariable(
                        managed.ServiceName,
                        managed.PrivateReferenceVariable);
                continue;
            }

            if (referenced is not IResourceWithConnectionString withConnectionString)
            {
                continue;
            }

            var expression = withConnectionString.ConnectionStringExpression.ValueExpression;
            if (IsRailwayReferenceExpression(expression))
            {
                service.Environment[$"{ConnectionStringPrefix}{referenced.Name}"] = expression;
                continue;
            }

            var parameters = CollectParameterResources(withConnectionString);
            foreach (var parameter in parameters)
            {
                AddParameterName(plan, parameter.Name);
            }

            var captureName = parameters.FirstOrDefault(static parameter => parameter.Secret)?.Name
                ?? parameters.FirstOrDefault()?.Name
                ?? referenced.Name;
            AddParameterName(plan, captureName);
            service.Environment[$"{ConnectionStringPrefix}{referenced.Name}"] = captureName;
        }
    }

    private static IEnumerable<IResource> GetReferencedResources(IResource resource)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relationship in resource.Annotations.OfType<ResourceRelationshipAnnotation>())
        {
            if (!string.Equals(relationship.Type, ReferenceRelationshipType, StringComparison.Ordinal))
            {
                continue;
            }

            if (relationship.Resource is null || !seen.Add(relationship.Resource.Name))
            {
                continue;
            }

            yield return relationship.Resource;
        }
    }

    private static IRailwayManagedServiceAnnotation? FindManagedAnnotation(IResource resource)
    {
        var managed = resource.Annotations.OfType<IRailwayManagedServiceAnnotation>().FirstOrDefault();
        if (managed is not null)
        {
            return managed;
        }

        return resource is IResourceWithParent { Parent: { } parent }
            ? FindManagedAnnotation(parent)
            : null;
    }

    private static List<ParameterResource> CollectParameterResources(IResourceWithConnectionString resource)
    {
        var parameters = new List<ParameterResource>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectParameters(resource, parameters, seen);
        return parameters;
    }

    private static void CollectParameters(object? value, List<ParameterResource> parameters, HashSet<object> seen)
    {
        if (value is null || !seen.Add(value))
        {
            return;
        }

        switch (value)
        {
            case ParameterResource parameter:
                if (!parameters.Exists(existing => string.Equals(existing.Name, parameter.Name, StringComparison.Ordinal)))
                {
                    parameters.Add(parameter);
                }

                break;

            case IResourceWithConnectionString connectionString:
                if (connectionString.TryGetLastAnnotation<ConnectionStringRedirectAnnotation>(out var redirect))
                {
                    CollectParameters(redirect.Resource, parameters, seen);
                }

                CollectParameters(connectionString.ConnectionStringExpression, parameters, seen);
                break;

            case ReferenceExpression expression:
                foreach (var provider in expression.ValueProviders)
                {
                    CollectParameters(provider, parameters, seen);
                }

                break;

            case IValueWithReferences withReferences:
                foreach (var reference in withReferences.References)
                {
                    CollectParameters(reference, parameters, seen);
                }

                break;
        }
    }

    private static void AddParameterName(RailwayPlan plan, string name)
    {
        if (!plan.Parameters.Contains(name, StringComparer.Ordinal))
        {
            plan.Parameters.Add(name);
        }
    }
}
