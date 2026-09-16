using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using Shoko.Abstractions.Metadata.Tmdb;
using Shoko.Plugin.TvMaze.Client.Models;
using Shoko.Plugin.TvMaze.Mapping;
using Xunit;

namespace Shoko.Plugin.TvMaze.Tests;

/// <summary>
/// Tests for matching TVmaze episodes to TMDB episodes by season and episode
/// number. TVmaze and TMDB orderings usually agree but not always, so an
/// episode that cannot be matched must be skipped rather than guessed at.
/// </summary>
public class TvMazeEpisodeMatcherTests
{
    private static Mock<ITmdbEpisode> TmdbEpisode(int number)
    {
        var episode = new Mock<ITmdbEpisode>();
        episode.Setup(e => e.EpisodeNumber).Returns(number);
        return episode;
    }

    private static ITmdbSeason Season(params Mock<ITmdbEpisode>[] episodes)
    {
        var season = new Mock<ITmdbSeason>();
        season.Setup(s => s.Episodes).Returns(episodes.Select(e => e.Object).ToList());
        return season.Object;
    }

    private static TvMazeEpisode Episode(int? season, int? number, DateTimeOffset? airstamp, int id = 1)
        => new() { Id = id, Season = season, Number = number, Airstamp = airstamp };

    [Fact]
    public void An_episode_matching_season_and_number_is_mapped()
    {
        var tmdbEpisode1 = TmdbEpisode(1);
        var season = Season(tmdbEpisode1);
        var airstamp = new DateTimeOffset(2022, 10, 11, 15, 0, 0, TimeSpan.Zero);
        var episodes = new[] { Episode(season: 1, number: 1, airstamp: airstamp, id: 42) };

        var result = TvMazeEpisodeMatcher.Match(episodes, seasonNumber: 1, season);

        var airing = Assert.Single(result.Airings);
        Assert.Same(tmdbEpisode1.Object, airing.Episode);
        Assert.Equal(airstamp.UtcDateTime, airing.AiredAt);
        Assert.Equal("tvmaze:42", airing.Key);
    }

    [Fact]
    public void Episodes_of_another_season_are_ignored()
    {
        var season = Season(TmdbEpisode(1));
        var episodes = new[] { Episode(season: 2, number: 1, airstamp: DateTimeOffset.UtcNow) };

        var result = TvMazeEpisodeMatcher.Match(episodes, seasonNumber: 1, season);

        Assert.Empty(result.Airings);
        Assert.Equal(0, result.SkippedWithoutAirstamp);
        Assert.Equal(0, result.SkippedWithoutTmdbEpisode);
    }

    [Fact]
    public void An_episode_with_no_number_is_skipped_and_counted()
    {
        var season = Season(TmdbEpisode(1));
        var episodes = new[] { Episode(season: 1, number: null, airstamp: DateTimeOffset.UtcNow) };

        var result = TvMazeEpisodeMatcher.Match(episodes, seasonNumber: 1, season);

        Assert.Empty(result.Airings);
        Assert.Equal(1, result.SkippedWithoutAirstamp);
    }

    [Fact]
    public void An_episode_with_no_airstamp_is_skipped_and_counted()
    {
        var season = Season(TmdbEpisode(1));
        var episodes = new[] { Episode(season: 1, number: 1, airstamp: null) };

        var result = TvMazeEpisodeMatcher.Match(episodes, seasonNumber: 1, season);

        Assert.Empty(result.Airings);
        Assert.Equal(1, result.SkippedWithoutAirstamp);
    }

    [Fact]
    public void An_episode_number_with_no_matching_tmdb_episode_is_skipped_rather_than_guessed_and_counted()
    {
        var season = Season(TmdbEpisode(1), TmdbEpisode(2));
        var episodes = new[] { Episode(season: 1, number: 99, airstamp: DateTimeOffset.UtcNow) };

        var result = TvMazeEpisodeMatcher.Match(episodes, seasonNumber: 1, season);

        Assert.Empty(result.Airings);
        Assert.Equal(1, result.SkippedWithoutTmdbEpisode);
    }

    [Fact]
    public void Airstamp_is_converted_to_utc()
    {
        var tmdbEpisode = TmdbEpisode(1);
        var season = Season(tmdbEpisode);
        // JST (+9) 00:00 is the previous day at 15:00 UTC.
        var airstamp = new DateTimeOffset(2022, 10, 12, 0, 0, 0, TimeSpan.FromHours(9));
        var episodes = new[] { Episode(season: 1, number: 1, airstamp: airstamp) };

        var airing = Assert.Single(TvMazeEpisodeMatcher.Match(episodes, seasonNumber: 1, season).Airings);

        Assert.Equal(new DateTime(2022, 10, 11, 15, 0, 0, DateTimeKind.Utc), airing.AiredAt);
        Assert.Equal(DateTimeKind.Utc, airing.AiredAt!.Value.Kind);
    }

    [Fact]
    public void Multiple_matched_episodes_are_all_returned_in_order()
    {
        var season = Season(TmdbEpisode(1), TmdbEpisode(2), TmdbEpisode(3));
        var episodes = new List<TvMazeEpisode>
        {
            Episode(season: 1, number: 1, airstamp: DateTimeOffset.UtcNow, id: 1),
            Episode(season: 1, number: 2, airstamp: DateTimeOffset.UtcNow, id: 2),
            Episode(season: 1, number: 3, airstamp: DateTimeOffset.UtcNow, id: 3),
        };

        var result = TvMazeEpisodeMatcher.Match(episodes, seasonNumber: 1, season);

        Assert.Equal(3, result.Airings.Count);
        Assert.Equal(["tvmaze:1", "tvmaze:2", "tvmaze:3"], result.Airings.Select(a => a.Key));
    }

    [Fact]
    public void Captured_late_night_episodes_keep_the_instant_their_airstamp_names()
    {
        // Frieren's second season airs at 01:00 JST on the night after the
        // airdate TVmaze lists, so airdate + airtime read as Asia/Tokyo would
        // be a day early. The airstamp is the instant, and it is what the
        // matcher uses.
        var season = Season(TmdbEpisode(1), TmdbEpisode(2), TmdbEpisode(3));
        var episodes = TvMazeFixtures.Episodes("frieren-episodes.json");

        var result = TvMazeEpisodeMatcher.Match(episodes, seasonNumber: 2, season);

        Assert.Equal(
            [
                new DateTime(2026, 1, 16, 16, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 23, 16, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 30, 16, 0, 0, DateTimeKind.Utc),
            ],
            result.Airings.Select(a => a.AiredAt)
        );
        Assert.All(result.Airings, airing => Assert.Equal(DateTimeKind.Utc, airing.AiredAt!.Value.Kind));
    }

    [Fact]
    public void Captured_episodes_with_no_counterpart_on_the_tmdb_season_are_counted()
    {
        // The TMDB season only has one episode; the captured season has three.
        var season = Season(TmdbEpisode(1));
        var episodes = TvMazeFixtures.Episodes("frieren-episodes.json");

        var result = TvMazeEpisodeMatcher.Match(episodes, seasonNumber: 1, season);

        Assert.Single(result.Airings);
        Assert.Equal(2, result.SkippedWithoutTmdbEpisode);
        Assert.Equal(0, result.SkippedWithoutAirstamp);
    }
}
