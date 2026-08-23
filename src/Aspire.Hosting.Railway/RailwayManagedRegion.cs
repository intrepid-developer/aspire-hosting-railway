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
    /// unset so apply does not invent a region.
    /// </summary>
    internal static ServiceInstanceUpdateInput? CreateTemplateRegionUpdate(RailwayPlanManagedService managed)
    {
        var region = TryOfficialTemplateRegion(managed);
        if (region is null)
        {
            return null;
        }

        return new ServiceInstanceUpdateInput
        {
            Region = region,
            NumReplicas = 1
        };
    }

    /// <summary>
    /// Copies the official template region onto an existing
    /// <c>serviceInstanceUpdate</c> input so a later update cannot omit
    /// <c>region</c> and reset the service to US West. No-op when unset.
    /// Still does not send <c>multiRegionConfig</c>.
    /// </summary>
    internal static void IncludeOnUpdate(ServiceInstanceUpdateInput input, RailwayPlanManagedService managed)
    {
        ArgumentNullException.ThrowIfNull(input);

        var region = TryOfficialTemplateRegion(managed);
        if (region is null)
        {
            return;
        }

        input.Region = region;
        input.NumReplicas = 1;
    }

    /// <summary>
    /// Standalone region <c>serviceInstanceUpdate</c> is for first-time
    /// template create (or the first apply that has not yet recorded the
    /// requested region). A safety re-send is skipped when the same
    /// official region was already applied and no other
    /// <c>serviceInstanceUpdate</c> is happening. Never twice in one apply.
    /// </summary>
    internal static bool ShouldSendStandaloneRegion(
        RailwayPlanManagedService managed,
        IReadOnlyDictionary<string, string> appliedRegions,
        ISet<string> regionUpdatesThisApply)
    {
        ArgumentNullException.ThrowIfNull(managed);
        ArgumentNullException.ThrowIfNull(appliedRegions);
        ArgumentNullException.ThrowIfNull(regionUpdatesThisApply);

        var region = TryOfficialTemplateRegion(managed);
        if (region is null)
        {
            return false;
        }

        if (regionUpdatesThisApply.Contains(managed.Name))
        {
            return false;
        }

        return !appliedRegions.TryGetValue(managed.Name, out var applied) ||
               !string.Equals(applied, region, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryOfficialTemplateRegion(RailwayPlanManagedService managed)
    {
        ArgumentNullException.ThrowIfNull(managed);

        if (string.IsNullOrWhiteSpace(managed.TemplateCode) ||
            string.IsNullOrWhiteSpace(managed.Region))
        {
            return null;
        }

        return RailwayRegionMapper.RequireOfficialRegionId(managed.Name, managed.Region);
    }
}
