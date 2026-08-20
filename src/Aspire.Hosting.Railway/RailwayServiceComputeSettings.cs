namespace Aspire.Hosting.Railway;

/// <summary>
/// Validates plan-time compute settings and maps them onto confirmed
/// <c>serviceInstanceUpdate</c> input fields.
/// </summary>
internal static class RailwayServiceComputeSettings
{
    /// <summary>
    /// Validates region ids and replica counts on every compute service in
    /// <paramref name="plan"/>. Managed templates and buckets are not in
    /// <see cref="RailwayPlan.Services"/> and are not validated here.
    /// </summary>
    public static void ValidatePlanServices(RailwayPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var service in plan.Services)
        {
            Validate(service);
        }
    }

    /// <summary>
    /// Validates official region ids and replica bounds on a compute service plan entry.
    /// </summary>
    public static void Validate(RailwayPlanService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        if (!string.IsNullOrWhiteSpace(service.Region))
        {
            EnsureOfficialRegion(service.Name, service.Region);
        }

        if (service.Replicas is { } replicas)
        {
            EnsureReplicaCount(service.Name, replicas, "replica count");
        }

        if (service.ReplicaRegions is not { Count: > 0 } replicaRegions)
        {
            return;
        }

        var total = 0;
        foreach (var pair in replicaRegions)
        {
            EnsureOfficialRegion(service.Name, pair.Key);
            EnsureReplicaCount(service.Name, pair.Value, $"replica count for region '{pair.Key}'");
            total = checked(total + pair.Value);
        }

        if (total > RailwayConstants.MaxReplicas)
        {
            throw new InvalidOperationException(
                $"Railway service '{service.Name}' total replicas must be at most {RailwayConstants.MaxReplicas} across all regions. See https://docs.railway.com/cli/scale.");
        }
    }

    /// <summary>
    /// Builds <c>serviceInstanceUpdate</c> input: always <c>source.image</c>, plus
    /// confirmed scale/serverless/region fields when the plan requested them.
    /// </summary>
    public static ServiceInstanceUpdateInput CreateUpdateInput(RailwayPlanService service, string image)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        Validate(service);

        var input = new ServiceInstanceUpdateInput
        {
            Source = new ServiceSourceInput { Image = image }
        };

        if (service.Serverless is { } serverless)
        {
            input.SleepApplication = serverless;
        }

        var multiRegionConfig = CreateMultiRegionConfig(service);
        if (multiRegionConfig is { Count: > 0 })
        {
            input.MultiRegionConfig = multiRegionConfig;
            return input;
        }

        if (service.Replicas is { } replicas)
        {
            input.NumReplicas = replicas;
        }

        return input;
    }

    /// <summary>
    /// Prefer <c>replicaRegions</c> over <c>region</c> + <c>WithReplicas</c>. An empty
    /// map is treated as unset so it is not sent.
    /// </summary>
    internal static Dictionary<string, ServiceInstanceRegionConfig>? CreateMultiRegionConfig(
        RailwayPlanService service)
    {
        if (service.ReplicaRegions is { Count: > 0 } replicaRegions)
        {
            return replicaRegions.ToDictionary(
                static pair => pair.Key,
                static pair => new ServiceInstanceRegionConfig { NumReplicas = pair.Value },
                StringComparer.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(service.Region))
        {
            return null;
        }

        return new Dictionary<string, ServiceInstanceRegionConfig>(StringComparer.Ordinal)
        {
            [service.Region] = new ServiceInstanceRegionConfig { NumReplicas = service.Replicas ?? 1 }
        };
    }

    private static void EnsureOfficialRegion(string serviceName, string regionId)
    {
        if (RailwayConstants.OfficialRegionIds.Contains(regionId, StringComparer.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Unknown Railway region '{regionId}' for service '{serviceName}'. " +
            $"Official region ids are: {string.Join(", ", RailwayConstants.OfficialRegionIds)}. " +
            "See https://docs.railway.com/deployments/regions.");
    }

    private static void EnsureReplicaCount(string serviceName, int count, string what)
    {
        if (count < 1)
        {
            throw new InvalidOperationException(
                $"Railway service '{serviceName}' {what} must be at least 1.");
        }

        if (count > RailwayConstants.MaxReplicas)
        {
            throw new InvalidOperationException(
                $"Railway service '{serviceName}' {what} must be at most {RailwayConstants.MaxReplicas}. " +
                "See https://docs.railway.com/cli/scale.");
        }
    }
}
