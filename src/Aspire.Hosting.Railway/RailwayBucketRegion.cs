namespace Aspire.Hosting.Railway;

/// <summary>
/// Tigris airport codes used for Railway bucket instances. These are not
/// compute <see cref="RailwayRegion"/> / <c>Region.region</c> ids.
/// Confirmed on the official EnvironmentConfig schema and
/// <see href="https://docs.railway.com/cli/bucket"/> (2026-08-23).
/// </summary>
internal static class RailwayBucketRegion
{
    internal const string Iad = "iad";
    internal const string Sjc = "sjc";
    internal const string Ams = "ams";
    internal const string Sin = "sin";

    internal static readonly IReadOnlyList<string> OfficialIds = [Iad, Sjc, Ams, Sin];

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

        if (OfficialIds.Contains(region, StringComparer.Ordinal))
        {
            return region;
        }

        throw new InvalidOperationException(
            $"Railway bucket region '{region}' is not a Tigris airport code. " +
            $"Use {string.Join(", ", OfficialIds)}. " +
            "Do not send compute Region.region ids such as us-east4-eqdc4a.");
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
