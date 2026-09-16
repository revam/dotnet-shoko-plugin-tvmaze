using System;

namespace Shoko.Plugin.TvMaze.Mapping;

/// <summary>
/// Decides which TMDB shows are worth spending a TVmaze request on during the
/// provider's daily sweep (<see cref="Jobs.TvMazeSweepJob"/>). Kept separate
/// from the job so the "should we bother" decision can be unit tested without
/// standing up the metadata service.
/// </summary>
public static class TvMazeSweepPlanner
{
    /// <summary>
    /// Whether a show is worth sweeping.
    /// </summary>
    /// <param name="hasTvdbShowId">
    /// Whether the show has a <c>TvdbShowID</c>, TVmaze's own lookup key. A
    /// show without one can never be keyed to TVmaze.
    /// </param>
    /// <param name="endDate">The show's known end date, if any.</param>
    /// <param name="stopSweepingEndedShowsAfterDays">
    /// How many days after <paramref name="endDate"/> the show stops being
    /// swept. Zero or negative disables the cutoff entirely (every ended show
    /// keeps being swept).
    /// </param>
    /// <param name="today">The current UTC date.</param>
    /// <returns><c>true</c> when the show should be swept.</returns>
    public static bool ShouldSweep(bool hasTvdbShowId, DateOnly? endDate, int stopSweepingEndedShowsAfterDays, DateOnly today)
    {
        if (!hasTvdbShowId)
            return false;

        if (stopSweepingEndedShowsAfterDays <= 0)
            return true;

        if (endDate is not { } end)
            return true;

        var cutoff = end.AddDays(stopSweepingEndedShowsAfterDays);
        return today <= cutoff;
    }
}
