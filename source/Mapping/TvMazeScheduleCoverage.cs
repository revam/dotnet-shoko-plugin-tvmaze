using System;
using System.Collections.Generic;
using System.Linq;
using Shoko.Plugin.TvMaze.Client.Models;

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

    /// <summary>
    /// The last episode a season's schedule covers. A finished season ends at
    /// its highest TVmaze episode number, which is its count of numbered
    /// episodes unless TVmaze skips a number; a running one is left open.
    /// </summary>
    /// <param name="seasonEpisodes">The season's TVmaze episodes.</param>
    /// <param name="isFinished">Whether the season's schedule is finished.</param>
    /// <returns>
    /// The highest episode number of a finished season, or <c>null</c> for a
    /// running season or one without numbered episodes.
    /// </returns>
    public static int? GetLastEpisodeNumber(IEnumerable<TvMazeEpisode> seasonEpisodes, bool isFinished)
    {
        if (!isFinished)
            return null;

        return seasonEpisodes
            .Select(episode => episode.Number)
            .Where(number => number is > 0)
            .Max();
    }
}
