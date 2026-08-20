namespace Aspire.Hosting.Railway;

/// <summary>
/// Official GraphQL <c>VolumeInstanceBackupScheduleKind</c> values
/// (live schema 2026-08-20) and plan-time validation. AppHosts set
/// booleans on <c>PublishAsRailwayPostgres</c>; plan JSON stores these
/// strings. Product retention (Daily keep 6 days, Weekly keep 1 month,
/// Monthly keep 3 months) is mapping only and is not hardcoded as API.
/// </summary>
internal static class RailwayVolumeBackupSchedule
{
    internal const string Daily = "DAILY";
    internal const string Weekly = "WEEKLY";
    internal const string Monthly = "MONTHLY";

    internal static readonly IReadOnlyList<string> OfficialKinds =
    [
        Daily,
        Weekly,
        Monthly
    ];

    internal static bool IsOfficialKind(string? kind) =>
        !string.IsNullOrWhiteSpace(kind) &&
        OfficialKinds.Contains(kind, StringComparer.Ordinal);

    /// <summary>
    /// Validates and orders leftover string paths (deserialized
    /// <c>railway-plan.json</c>). Official enum strings pass through;
    /// empty or unknown strings fail honestly. Duplicates are collapsed
    /// in official order.
    /// </summary>
    internal static List<string> Normalize(IEnumerable<string?> kinds, string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in kinds)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new InvalidOperationException(
                    $"Railway volume backup schedule kind for '{serviceName}' is empty. " +
                    $"Use official VolumeInstanceBackupScheduleKind values: {string.Join(", ", OfficialKinds)}.");
            }

            if (!IsOfficialKind(kind))
            {
                throw new InvalidOperationException(
                    $"Unknown Railway volume backup schedule kind '{kind}' for '{serviceName}'. " +
                    $"Use official VolumeInstanceBackupScheduleKind values: {string.Join(", ", OfficialKinds)}. " +
                    "See https://docs.railway.com/volumes/backups.");
            }

            seen.Add(kind);
        }

        return OfficialKinds.Where(seen.Contains).ToList();
    }

    /// <summary>
    /// Validates <c>volumeBackupScheduleKinds</c> on managed services.
    /// Empty lists are omitted (leave dashboard as-is). Invalid strings
    /// fail before GraphQL.
    /// </summary>
    internal static void ValidatePlan(RailwayPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var managed in plan.ManagedServices)
        {
            if (managed.VolumeBackupScheduleKinds is not { Count: > 0 } kinds)
            {
                continue;
            }

            if (string.Equals(managed.Kind, "bucket", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Railway bucket '{managed.Name}' cannot set volumeBackupScheduleKinds. " +
                    "Volume backup schedules are for official Postgres volumes only.");
            }

            managed.VolumeBackupScheduleKinds = Normalize(kinds, managed.Name);
        }
    }

    /// <summary>
    /// Unions requested kinds with already-present kinds so
    /// <c>volumeInstanceBackupScheduleUpdate</c> never removes a
    /// dashboard schedule this plan did not mention. Official order.
    /// </summary>
    internal static List<string> Union(IEnumerable<string?> requested, IEnumerable<string?> existing, string serviceName)
    {
        var requestedOfficial = Normalize(requested, serviceName);
        var existingOfficial = Normalize(existing, serviceName);
        return OfficialKinds
            .Where(kind => requestedOfficial.Contains(kind) || existingOfficial.Contains(kind))
            .ToList();
    }

    internal static bool IsSubset(IReadOnlyCollection<string> requested, IReadOnlyCollection<string> existing) =>
        requested.Count > 0 &&
        requested.All(kind => existing.Contains(kind, StringComparer.Ordinal));
}
