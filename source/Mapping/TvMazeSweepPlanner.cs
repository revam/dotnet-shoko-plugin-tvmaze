using System;
using System.Collections.Generic;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Tmdb;

namespace Shoko.Plugin.TvMaze.Mapping;

/// <summary>
/// Decides which TMDB shows are worth spending a TVmaze request on during a
/// sweep. Kept separate from the provider so the "should we bother" decision
/// can be unit tested without standing up the metadata service.
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

    /// <summary>
    /// Picks the TMDB shows to sweep out of everything the metadata service
    /// returned for TMDB, counting what was dropped and why so the sweep can
    /// report it instead of silently looking at nothing.
    /// </summary>
    /// <param name="series">Every series the metadata service has for TMDB.</param>
    /// <param name="stopSweepingEndedShowsAfterDays">
    /// How many days after a show's end date it stops being swept. Zero or
    /// negative disables the cutoff.
    /// </param>
    /// <param name="today">The current UTC date.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="series"/> is <c>null</c>.
    /// </exception>
    /// <returns>The shows to sweep, and the tally of everything left out.</returns>
    public static TvMazeSweepPlan Plan(IEnumerable<ISeries> series, int stopSweepingEndedShowsAfterDays, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(series);

        var shows = new List<ITmdbShow>();
        var total = 0;
        var notAShow = 0;
        var withoutTvdbShowId = 0;
        var endedTooLongAgo = 0;

        foreach (var entry in series)
        {
            total++;
            if (entry is not ITmdbShow show)
            {
                notAShow++;
                continue;
            }

            if (show.TvdbShowID is null)
            {
                withoutTvdbShowId++;
                continue;
            }

            if (!ShouldSweep(hasTvdbShowId: true, show.EndDate?.ToDateOnly(), stopSweepingEndedShowsAfterDays, today))
            {
                endedTooLongAgo++;
                continue;
            }

            shows.Add(show);
        }

        return new TvMazeSweepPlan(shows, total, notAShow, withoutTvdbShowId, endedTooLongAgo);
    }
}

/// <summary>
/// What <see cref="TvMazeSweepPlanner.Plan"/> decided to sweep, and what it
/// left out.
/// </summary>
/// <param name="Shows">The TMDB shows to sweep.</param>
/// <param name="TotalSeries">How many TMDB series the metadata service returned.</param>
/// <param name="NotAShow">How many of those were not shows (TMDB movies, mostly).</param>
/// <param name="WithoutTvdbShowID">
/// How many shows carry no TheTVDB ID, and so can never be keyed to TVmaze.
/// </param>
/// <param name="EndedTooLongAgo">
/// How many shows ended further back than the configured cutoff.
/// </param>
public sealed record TvMazeSweepPlan(
    IReadOnlyList<ITmdbShow> Shows,
    int TotalSeries,
    int NotAShow,
    int WithoutTvdbShowID,
    int EndedTooLongAgo
);
