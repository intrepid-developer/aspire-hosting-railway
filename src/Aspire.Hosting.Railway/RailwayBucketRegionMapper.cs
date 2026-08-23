namespace Aspire.Hosting.Railway;

/// <summary>
/// Maps <see cref="RailwayBucketRegion"/> to official Tigris airport codes
/// and validates leftover string ids (deserialized plans).
/// </summary>
internal static class RailwayBucketRegionMapper
{
    internal const string Iad = "iad";
    internal const string Sjc = "sjc";
    internal const string Ams = "ams";
    internal const string Sin = "sin";

    internal static readonly IReadOnlyList<string> OfficialIds = [Iad, Sjc, Ams, Sin];

    /// <summary>
    /// Returns the official Tigris airport code for <paramref name="region"/>.
    /// Undefined enum values fail honestly.
    /// </summary>
    internal static string ToRegionId(RailwayBucketRegion region) => region switch
    {
        RailwayBucketRegion.Iad => Iad,
        RailwayBucketRegion.Sjc => Sjc,
        RailwayBucketRegion.Ams => Ams,
        RailwayBucketRegion.Sin => Sin,
        _ => throw new InvalidOperationException(
            $"Unknown Railway bucket region '{region}' ({(int)region}). " +
            "Use RailwayBucketRegion (Iad, Sjc, Ams, Sin). " +
            $"Official Tigris codes: {string.Join(", ", OfficialIds)}. " +
            "Do not send compute Region.region ids such as us-east4-eqdc4a. " +
            "See https://docs.railway.com/cli/bucket.")
    };

    internal static bool IsOfficialBucketRegionId(string? regionId) =>
        !string.IsNullOrWhiteSpace(regionId) &&
        OfficialIds.Contains(regionId, StringComparer.Ordinal);

    /// <summary>
    /// Returns a confirmed Tigris region. Defaults to <see cref="Iad"/>
    /// when <paramref name="region"/> is unset. Rejects compute-region
    /// strings and unknown codes.
    /// </summary>
    internal static string Resolve(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return Iad;
        }

        if (IsOfficialBucketRegionId(region))
        {
            return region;
        }

        throw new InvalidOperationException(
            $"Railway bucket region '{region}' is not a Tigris airport code. " +
            $"Use {string.Join(", ", OfficialIds)}. " +
            "Do not send compute Region.region ids such as us-east4-eqdc4a.");
    }

    /// <summary>
    /// Defensive check for leftover string paths (deserialized
    /// <c>railway-plan.json</c>). Official Tigris codes pass through;
    /// compute ids and other strings fail honestly.
    /// </summary>
    internal static string RequireOfficialBucketRegionId(string bucketName, string regionId)
    {
        if (IsOfficialBucketRegionId(regionId))
        {
            return regionId;
        }

        throw new InvalidOperationException(
            $"Unknown Railway bucket region '{regionId}' for '{bucketName}'. " +
            $"Use official Tigris airport codes: {string.Join(", ", OfficialIds)}. " +
            "Do not send compute Region.region ids such as us-west2 or europe-west4-drams3a. " +
            "See https://docs.railway.com/cli/bucket.");
    }

    /// <summary>
    /// Builds the confirmed <c>EnvironmentConfig.buckets</c> patch used to
    /// provision a bucket instance after <c>bucketCreate</c>.
    /// </summary>
    internal static EnvironmentConfigInput CreateInstancePatch(string bucketId, string? region = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketId);

        return new EnvironmentConfigInput
        {
            Buckets = new Dictionary<string, EnvironmentConfigBucket>(StringComparer.Ordinal)
            {
                [bucketId] = new EnvironmentConfigBucket
                {
                    Region = Resolve(region),
                    IsCreated = true
                }
            }
        };
    }
}
