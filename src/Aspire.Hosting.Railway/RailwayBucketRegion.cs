namespace Aspire.Hosting.Railway;

/// <summary>
/// Official Tigris airport codes for Railway bucket instances.
/// These are not compute <see cref="RailwayRegion"/> / <c>Region.region</c> ids.
/// </summary>
/// <remarks>
/// Members map to the codes documented at
/// <see href="https://docs.railway.com/cli/bucket"/> (verified 2026-08-23):
/// <c>iad</c>, <c>sjc</c>, <c>ams</c>, <c>sin</c>. Region is immutable after
/// the bucket instance is provisioned; changing it means drop + recreate.
/// Unset AppHosts keep today's default (<see cref="Iad"/>).
/// </remarks>
public enum RailwayBucketRegion
{
    /// <summary>US East, Virginia — Tigris <c>iad</c>. Default when unset.</summary>
    Iad,

    /// <summary>US West, California — Tigris <c>sjc</c>.</summary>
    Sjc,

    /// <summary>EU West, Amsterdam — Tigris <c>ams</c>.</summary>
    Ams,

    /// <summary>Asia Pacific, Singapore — Tigris <c>sin</c>.</summary>
    Sin
}
