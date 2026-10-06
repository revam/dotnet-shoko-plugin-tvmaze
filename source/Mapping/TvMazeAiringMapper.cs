using System.Collections.Generic;
using Shoko.Abstractions.Metadata.Airing;
using Shoko.Plugin.TvMaze.Client.Models;

namespace Shoko.Plugin.TvMaze.Mapping;

/// <summary>
/// Builds the <see cref="EpisodeAiringData"/> the airing schedule service
/// expects from one season's TVmaze episodes. Each numbered episode is placed
/// on the season schedule's numbered line by its TVmaze number, and the core
/// resolves it to an episode when the airing is read, so nothing has to exist
/// on the TMDB season yet. What is left out is counted, so the caller can say
/// how many were dropped and why.
/// </summary>
public static class TvMazeAiringMapper
{
    /// <summary>
    /// Builds the airings for one season.
    /// </summary>
    /// <param name="episodes">
    /// The show's full TVmaze episode list. Only the ones whose
    /// <see cref="TvMazeEpisode.Season"/> equals <paramref name="seasonNumber"/>
    /// are considered; the rest belong to other seasons and are ignored.
    /// </param>
    /// <param name="seasonNumber">The TVmaze (and TMDB) season number to build airings for.</param>
    /// <returns>
    /// One <see cref="EpisodeAiringData"/> per numbered TVmaze episode of the
    /// season that has an airstamp, plus a count of everything that was
    /// skipped, broken down by why.
    /// </returns>
    public static TvMazeSeasonAirings Map(IEnumerable<TvMazeEpisode> episodes, int seasonNumber)
    {
        var airings = new List<EpisodeAiringData>();
        var skippedWithoutAirstamp = 0;
        var skippedUnnumbered = 0;

        foreach (var episode in episodes)
        {
            if (episode.Season != seasonNumber)
                continue;

            // An unnumbered episode is one of TVmaze's significant specials.
            // It would have to be pinned to an episode off the numbered line,
            // and the TMDB season the schedule is for only holds regular
            // episodes, so there is nothing to pin it to.
            if (episode.Number is not (> 0 and var number))
            {
                skippedUnnumbered++;
                continue;
            }

            if (episode.Airstamp is not { } airstamp)
            {
                skippedWithoutAirstamp++;
                continue;
            }

            airings.Add(new EpisodeAiringData
            {
                SequenceNumber = number,
                // TVmaze's airstamp is an absolute instant carrying its own
                // offset (the show's local broadcast time, normalised to
                // UTC by TVmaze itself), so this is the one conversion the
                // schedule service wants: a UTC DateTime, no local time
                // anywhere in sight.
                AiredAt = airstamp.UtcDateTime,
                Key = $"tvmaze:{episode.Id}",
            });
        }

        return new TvMazeSeasonAirings(airings, skippedWithoutAirstamp, skippedUnnumbered);
    }
}

/// <summary>
/// What <see cref="TvMazeAiringMapper.Map"/> made of one season's TVmaze
/// episodes.
/// </summary>
/// <param name="Airings">The airings to submit, in TVmaze's own order.</param>
/// <param name="SkippedWithoutAirstamp">
/// How many numbered episodes of the season were skipped because TVmaze had no
/// confirmed air time for them yet.
/// </param>
/// <param name="SkippedUnnumbered">
/// How many episodes of the season were skipped because TVmaze gives them no
/// episode number, which is how it lists a significant special.
/// </param>
public sealed record TvMazeSeasonAirings(
    IReadOnlyList<EpisodeAiringData> Airings,
    int SkippedWithoutAirstamp,
    int SkippedUnnumbered
);
