using System.Collections.Generic;
using System.Linq;
using Shoko.Abstractions.Metadata.Airing;
using Shoko.Abstractions.Metadata.Tmdb;
using Shoko.Plugin.TvMaze.Client.Models;

namespace Shoko.Plugin.TvMaze.Mapping;

/// <summary>
/// Matches TVmaze episodes to TMDB episodes by season and episode number, and
/// builds the <see cref="EpisodeAiringData"/> the airing schedule service
/// expects. TVmaze and TMDB orderings usually agree but not always, so an
/// episode that cannot be matched is skipped rather than guessed at — and
/// counted, so the caller can say how many were dropped and why instead of
/// leaving a season that matched nothing looking like a working refresh.
/// </summary>
public static class TvMazeEpisodeMatcher
{
    /// <summary>
    /// Matches the TVmaze episodes of one season against a TMDB season's
    /// episodes.
    /// </summary>
    /// <param name="episodes">
    /// The show's full TVmaze episode list. Only the ones whose
    /// <see cref="TvMazeEpisode.Season"/> equals <paramref name="seasonNumber"/>
    /// are considered; the rest belong to other seasons and are ignored.
    /// </param>
    /// <param name="seasonNumber">The TVmaze (and TMDB) season number to match.</param>
    /// <param name="season">The TMDB season to match episodes against.</param>
    /// <returns>
    /// One <see cref="EpisodeAiringData"/> per TVmaze episode that has a
    /// season, number and airstamp, and whose number matches a TMDB episode
    /// of <paramref name="season"/>, plus a count of everything that was
    /// skipped, broken down by why.
    /// </returns>
    public static TvMazeEpisodeMatchResult Match(IEnumerable<TvMazeEpisode> episodes, int seasonNumber, ITmdbSeason season)
    {
        var airings = new List<EpisodeAiringData>();
        var skippedWithoutAirstamp = 0;
        var skippedWithoutTmdbEpisode = 0;

        foreach (var episode in episodes)
        {
            if (episode.Season != seasonNumber)
                continue;

            if (episode.Number is not { } number || episode.Airstamp is not { } airstamp)
            {
                skippedWithoutAirstamp++;
                continue;
            }

            var tmdbEpisode = season.Episodes.FirstOrDefault(e => e.EpisodeNumber == number);
            if (tmdbEpisode is null)
            {
                skippedWithoutTmdbEpisode++;
                continue;
            }

            airings.Add(new EpisodeAiringData
            {
                Episode = tmdbEpisode,
                // TVmaze's airstamp is an absolute instant carrying its own
                // offset (the show's local broadcast time, normalised to
                // UTC by TVmaze itself), so this is the one conversion the
                // schedule service wants: a UTC DateTime, no local time
                // anywhere in sight.
                AiredAt = airstamp.UtcDateTime,
                Key = $"tvmaze:{episode.Id}",
            });
        }

        return new TvMazeEpisodeMatchResult(airings, skippedWithoutAirstamp, skippedWithoutTmdbEpisode);
    }
}

/// <summary>
/// What <see cref="TvMazeEpisodeMatcher.Match"/> made of one season's TVmaze
/// episodes.
/// </summary>
/// <param name="Airings">The airings to submit, in TVmaze's own order.</param>
/// <param name="SkippedWithoutAirstamp">
/// How many episodes of the season were skipped because TVmaze had no episode
/// number or no confirmed air time for them yet.
/// </param>
/// <param name="SkippedWithoutTmdbEpisode">
/// How many episodes of the season were skipped because the TMDB season has
/// no episode with that number.
/// </param>
public sealed record TvMazeEpisodeMatchResult(
    IReadOnlyList<EpisodeAiringData> Airings,
    int SkippedWithoutAirstamp,
    int SkippedWithoutTmdbEpisode
);
