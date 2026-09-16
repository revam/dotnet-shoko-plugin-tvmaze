using System;

namespace Shoko.Plugin.TvMaze.Mapping;

/// <summary>
/// TVmaze reports one run status for the whole show, not per season, so
/// whether an individual season's schedule is finished has to be inferred:
/// every season below the show's latest is necessarily done, and the latest
/// one is only open while the show itself is still <c>"Running"</c>.
/// </summary>
public static class TvMazeScheduleCoverage
{
    /// <summary>
    /// Whether the schedule for one season should be marked finished.
    /// </summary>
    /// <param name="seasonNumber">The season being written.</param>
    /// <param name="latestSeasonNumber">
    /// The highest season number found in the show's TVmaze episode list.
    /// </param>
    /// <param name="showStatus">
    /// The show's TVmaze <c>status</c> field, e.g. <c>"Running"</c> or
    /// <c>"Ended"</c>. Compared case-insensitively; anything other than
    /// <c>"Running"</c> (including <c>null</c>) is treated as not running.
    /// </param>
    /// <returns>
    /// <c>false</c> only for the latest season of a show TVmaze still
    /// considers running; <c>true</c> for every earlier season, and for the
    /// latest season of a show that has ended, was cancelled, or whose status
    /// is otherwise not "Running".
    /// </returns>
    public static bool IsSeasonFinished(int seasonNumber, int latestSeasonNumber, string? showStatus)
    {
        var isLatestSeason = seasonNumber == latestSeasonNumber;
        var isShowRunning = string.Equals(showStatus, "Running", StringComparison.OrdinalIgnoreCase);
        return !(isLatestSeason && isShowRunning);
    }
}
