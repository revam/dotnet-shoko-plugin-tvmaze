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
/// episode that cannot be matched is skipped rather than guessed at.
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
    /// of <paramref name="season"/>. Everything else is skipped.
    /// </returns>
    public static IReadOnlyList<EpisodeAiringData> Match(IEnumerable<TvMazeEpisode> episodes, int seasonNumber, ITmdbSeason season)
    {
        var airings = new List<EpisodeAiringData>();

        foreach (var episode in episodes)
        {
            if (episode.Season != seasonNumber)
                continue;

            if (episode.Number is not { } number || episode.Airstamp is not { } airstamp)
                continue;

            var tmdbEpisode = season.Episodes.FirstOrDefault(e => e.EpisodeNumber == number);
            if (tmdbEpisode is null)
                continue;

            airings.Add(new EpisodeAiringData
            {
                Episode = tmdbEpisode,
                AiredAt = airstamp.UtcDateTime,
                Key = $"tvmaze:{episode.Id}",
            });
        }

        return airings;
    }
}
