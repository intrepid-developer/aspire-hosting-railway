namespace Aspire.Hosting.Railway;

/// <summary>
/// Railway-specific settings for official Aspire Postgres published with
/// <c>PublishAsRailwayPostgres</c>. Volume backup schedule booleans map
/// to GraphQL <c>VolumeInstanceBackupScheduleKind</c> (<c>DAILY</c>,
/// <c>WEEKLY</c>, <c>MONTHLY</c>). Unset / false omits that kind.
/// </summary>
public sealed class RailwayPostgresSettings
{
    /// <summary>
    /// Gets or sets whether the Daily volume backup schedule should be
    /// requested. Product mapping only: Railway keeps daily backups for
    /// 6 days. See <see href="https://docs.railway.com/volumes/backups"/>.
    /// </summary>
    public bool VolumeBackupDaily { get; set; }

    /// <summary>
    /// Gets or sets whether the Weekly volume backup schedule should be
    /// requested. Product mapping only: Railway keeps weekly backups for
    /// 1 month.
    /// </summary>
    public bool VolumeBackupWeekly { get; set; }

    /// <summary>
    /// Gets or sets whether the Monthly volume backup schedule should be
    /// requested. Product mapping only: Railway keeps monthly backups for
    /// 3 months.
    /// </summary>
    public bool VolumeBackupMonthly { get; set; }
}
