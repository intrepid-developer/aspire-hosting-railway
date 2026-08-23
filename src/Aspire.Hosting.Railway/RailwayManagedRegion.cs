namespace Aspire.Hosting.Railway;

/// <summary>
/// Plan-time validation and apply mapping for managed-service regions.
/// Buckets use Tigris airport codes; official Postgres / Redis templates
/// use compute <c>Region.region</c> ids. The two sets are never mixed.
/// </summary>
internal static class RailwayManagedRegion
{
    /// <summary>
    /// Validates <c>region</c> on managed services. Unset is omitted
    /// (buckets default to <c>iad</c> at apply; templates follow the
    /// project default). Compute ids on buckets and Tigris codes on
    /// Postgres / Redis fail before GraphQL.
    /// </summary>
    internal static void ValidatePlan(RailwayPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var managed in plan.ManagedServices)
        {
            if (string.IsNullOrWhiteSpace(managed.Region))
            {
                continue;
            }

            if (string.Equals(managed.Kind, "bucket", StringComparison.OrdinalIgnoreCase))
            {
                RailwayBucketRegionMapper.RequireOfficialBucketRegionId(managed.Name, managed.Region);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(managed.TemplateCode))
            {
                RailwayRegionMapper.RequireOfficialRegionId(managed.Name, managed.Region);
                continue;
            }

            throw new InvalidOperationException(
                $"Railway managed service '{managed.Name}' ({managed.Kind}) cannot set region.");
        }
    }

    /// <summary>
    /// Builds <c>serviceInstanceUpdate</c> input for a volume-backed
    /// template when a compute region was requested. Sends the official
    /// <c>region</c> key and <c>numReplicas</c> 1. Does not send
    /// <c>multiRegionConfig</c>. Returns <see langword="null"/> when
    /// unset so apply does not invent a region. Apply sends this at most
    /// once per managed service per <c>ApplyAsync</c> (first-time template
    /// create). Subsequent applies do not re-send it as a standalone
    /// update. Re-include only when already sending a
    /// <c>serviceInstanceUpdate</c>.
    /// </summary>
    internal static ServiceInstanceUpdateInput? CreateTemplateRegionUpdate(RailwayPlanManagedService managed)
    {
        ArgumentNullException.ThrowIfNull(managed);

        if (string.IsNullOrWhiteSpace(managed.TemplateCode) ||
            string.IsNullOrWhiteSpace(managed.Region))
        {
            return null;
        }

        var region = RailwayRegionMapper.RequireOfficialRegionId(managed.Name, managed.Region);
        return new ServiceInstanceUpdateInput
        {
            Region = region,
            NumReplicas = 1
        };
    }
}
