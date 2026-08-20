namespace Aspire.Hosting.Railway;

/// <summary>
/// Maps <see cref="RailwayRestartPolicy"/> to official GraphQL
/// <c>RestartPolicyType</c> strings and validates leftover string ids
/// (deserialized plans).
/// </summary>
internal static class RailwayRestartPolicyMapper
{
    internal const string OnFailure = "ON_FAILURE";
    internal const string Always = "ALWAYS";
    internal const string Never = "NEVER";

    internal static readonly IReadOnlyList<string> OfficialTypes =
    [
        OnFailure,
        Always,
        Never
    ];

    /// <summary>
    /// Returns the official GraphQL <c>RestartPolicyType</c> string for
    /// <paramref name="policy"/>. Undefined enum values fail honestly.
    /// </summary>
    internal static string ToGraphQL(RailwayRestartPolicy policy) => policy switch
    {
        RailwayRestartPolicy.OnFailure => OnFailure,
        RailwayRestartPolicy.Always => Always,
        RailwayRestartPolicy.Never => Never,
        _ => throw new InvalidOperationException(
            $"Unknown Railway restart policy '{policy}' ({(int)policy}). " +
            "Use RailwayRestartPolicy (OnFailure, Always, Never). " +
            $"Official RestartPolicyType values: {string.Join(", ", OfficialTypes)}. " +
            "See https://docs.railway.com/deployments/restart-policy.")
    };

    internal static bool IsOfficialType(string? type) =>
        !string.IsNullOrWhiteSpace(type) &&
        OfficialTypes.Contains(type, StringComparer.Ordinal);

    /// <summary>
    /// Defensive check for leftover string paths (deserialized
    /// <c>railway-plan.json</c>). Official GraphQL enum strings pass through;
    /// other strings fail honestly.
    /// </summary>
    internal static string RequireOfficialType(string serviceName, string type)
    {
        if (IsOfficialType(type))
        {
            return type;
        }

        throw new InvalidOperationException(
            $"Unknown Railway restart policy '{type}' for service '{serviceName}'. " +
            $"Use official RestartPolicyType values: {string.Join(", ", OfficialTypes)}. " +
            "See https://docs.railway.com/deployments/restart-policy.");
    }
}
