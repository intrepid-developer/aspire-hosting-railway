namespace Aspire.Hosting.Railway;

/// <summary>
/// Maps <see cref="RailwayRegion"/> to official GraphQL <c>Region.region</c>
/// deploy keys and validates leftover string ids (deserialized plans).
/// </summary>
internal static class RailwayRegionMapper
{
    internal const string UsWest2 = "us-west2";
    internal const string UsEast4 = "us-east4-eqdc4a";
    internal const string EuropeWest4 = "europe-west4-drams3a";
    internal const string AsiaSoutheast1 = "asia-southeast1-eqsg3a";

    internal static readonly IReadOnlyList<string> OfficialRegionIds =
    [
        UsWest2,
        UsEast4,
        EuropeWest4,
        AsiaSoutheast1
    ];

    /// <summary>
    /// Returns the official <c>Region.region</c> deploy key for
    /// <paramref name="region"/>. Undefined enum values fail honestly.
    /// </summary>
    internal static string ToRegionId(RailwayRegion region) => region switch
    {
        RailwayRegion.UsWest2 => UsWest2,
        RailwayRegion.UsEast4 => UsEast4,
        RailwayRegion.EuropeWest4 => EuropeWest4,
        RailwayRegion.AsiaSoutheast1 => AsiaSoutheast1,
        _ => throw new InvalidOperationException(
            $"Unknown Railway region '{region}' ({(int)region}). " +
            "Use RailwayRegion (UsWest2, UsEast4, EuropeWest4, AsiaSoutheast1). " +
            $"Official deploy region ids (Region.region): {string.Join(", ", OfficialRegionIds)}. " +
            "Airport codes (sjc, iad, ams, sin) and older ids (us-west1, us-east4, europe-west4) are not deploy keys. " +
            "See https://docs.railway.com/deployments/regions.")
    };

    internal static bool IsOfficialRegionId(string? regionId) =>
        !string.IsNullOrWhiteSpace(regionId) &&
        OfficialRegionIds.Contains(regionId, StringComparer.Ordinal);

    /// <summary>
    /// Defensive check for leftover string paths (deserialized
    /// <c>railway-plan.json</c>). Official ids pass through; airport codes,
    /// older ids, and other strings fail honestly.
    /// </summary>
    internal static string RequireOfficialRegionId(string serviceName, string regionId)
    {
        if (IsOfficialRegionId(regionId))
        {
            return regionId;
        }

        throw new InvalidOperationException(
            $"Unknown Railway region '{regionId}' for service '{serviceName}'. " +
            $"Use official deploy region ids (Region.region): {string.Join(", ", OfficialRegionIds)}. " +
            "Airport codes (sjc, iad, ams, sin) and older ids (us-west1, us-east4, europe-west4) are not deploy keys. " +
            "See https://docs.railway.com/deployments/regions.");
    }

    internal static Dictionary<string, int> ToOfficialReplicaRegions(
        IReadOnlyDictionary<RailwayRegion, int> replicaRegions)
    {
        var mapped = new Dictionary<string, int>(replicaRegions.Count, StringComparer.Ordinal);
        foreach (var pair in replicaRegions)
        {
            mapped[ToRegionId(pair.Key)] = pair.Value;
        }

        return mapped;
    }
}
