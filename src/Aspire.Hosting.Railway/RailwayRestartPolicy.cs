namespace Aspire.Hosting.Railway;

/// <summary>
/// Railway process restart policy (<c>RestartPolicyType</c>).
/// </summary>
/// <remarks>
/// Members map to GraphQL <c>RestartPolicyType</c> values confirmed on the
/// live schema 2026-08-20, not dashboard labels. GraphQL apply sends the
/// mapped enum string only. See
/// <see href="https://docs.railway.com/deployments/restart-policy"/>.
/// </remarks>
public enum RailwayRestartPolicy
{
    /// <summary>Restart on non-zero exit — GraphQL <c>ON_FAILURE</c>. Railway dashboard default.</summary>
    OnFailure,

    /// <summary>Restart every stop — GraphQL <c>ALWAYS</c>.</summary>
    Always,

    /// <summary>Do not auto-restart — GraphQL <c>NEVER</c>.</summary>
    Never
}
