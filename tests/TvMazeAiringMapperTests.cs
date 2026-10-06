using System;
using System.Collections.Generic;
using System.Linq;
using Shoko.Plugin.TvMaze.Client.Models;
using Shoko.Plugin.TvMaze.Mapping;
using Xunit;

namespace Shoko.Plugin.TvMaze.Tests;

/// <summary>
/// Tests for turning TVmaze episodes into airings placed on the season
/// schedule's numbered line. The core resolves the number to an episode when
/// the airing is read, so an episode TMDB does not list yet is still written.
/// </summary>
public class TvMazeAiringMapperTests
{
    private static TvMazeEpisode Episode(int? season, int? number, DateTimeOffset? airstamp, int id = 1)
        => new() { Id = id, Season = season, Number = number, Airstamp = airstamp };

    [Fact]
    public void A_numbered_episode_is_placed_by_its_number()
    {
        var airstamp = new DateTimeOffset(2022, 10, 11, 15, 0, 0, TimeSpan.Zero);
        var episodes = new[] { Episode(season: 1, number: 7, airstamp: airstamp, id: 42) };

        var result = TvMazeAiringMapper.Map(episodes, seasonNumber: 1);

        var airing = Assert.Single(result.Airings);
        Assert.Equal(7, airing.SequenceNumber);
        Assert.Null(airing.Episode);
        Assert.Equal(airstamp.UtcDateTime, airing.AiredAt);
        Assert.Equal("tvmaze:42", airing.Key);
    }

    [Fact]
    public void Episodes_of_another_season_are_ignored()
    {
        var episodes = new[] { Episode(season: 2, number: 1, airstamp: DateTimeOffset.UtcNow) };

        var result = TvMazeAiringMapper.Map(episodes, seasonNumber: 1);

        Assert.Empty(result.Airings);
        Assert.Equal(0, result.SkippedWithoutAirstamp);
        Assert.Equal(0, result.SkippedUnnumbered);
    }

    [Fact]
    public void An_unnumbered_special_is_skipped_and_counted()
    {
        var episodes = new[] { Episode(season: 1, number: null, airstamp: DateTimeOffset.UtcNow) };

        var result = TvMazeAiringMapper.Map(episodes, seasonNumber: 1);

        Assert.Empty(result.Airings);
        Assert.Equal(1, result.SkippedUnnumbered);
    }

    [Fact]
    public void An_episode_with_no_airstamp_is_skipped_and_counted()
    {
        var episodes = new[] { Episode(season: 1, number: 1, airstamp: null) };

        var result = TvMazeAiringMapper.Map(episodes, seasonNumber: 1);

        Assert.Empty(result.Airings);
        Assert.Equal(1, result.SkippedWithoutAirstamp);
    }

    [Fact]
    public void Airstamp_is_converted_to_utc()
    {
        // JST (+9) 00:00 is the previous day at 15:00 UTC.
        var airstamp = new DateTimeOffset(2022, 10, 12, 0, 0, 0, TimeSpan.FromHours(9));
        var episodes = new[] { Episode(season: 1, number: 1, airstamp: airstamp) };

        var airing = Assert.Single(TvMazeAiringMapper.Map(episodes, seasonNumber: 1).Airings);

        Assert.Equal(new DateTime(2022, 10, 11, 15, 0, 0, DateTimeKind.Utc), airing.AiredAt);
        Assert.Equal(DateTimeKind.Utc, airing.AiredAt!.Value.Kind);
    }

    [Fact]
    public void Multiple_episodes_are_all_returned_in_order()
    {
        var episodes = new List<TvMazeEpisode>
        {
            Episode(season: 1, number: 1, airstamp: DateTimeOffset.UtcNow, id: 1),
            Episode(season: 1, number: 2, airstamp: DateTimeOffset.UtcNow, id: 2),
            Episode(season: 1, number: 3, airstamp: DateTimeOffset.UtcNow, id: 3),
        };

        var result = TvMazeAiringMapper.Map(episodes, seasonNumber: 1);

        Assert.Equal([1, 2, 3], result.Airings.Select(a => a.SequenceNumber));
        Assert.Equal(["tvmaze:1", "tvmaze:2", "tvmaze:3"], result.Airings.Select(a => a.Key));
    }

    [Fact]
    public void Captured_late_night_episodes_keep_the_instant_their_airstamp_names()
    {
        // Frieren's second season airs at 01:00 JST on the night after the
        // airdate TVmaze lists, so airdate + airtime read as Asia/Tokyo would
        // be a day early. The airstamp is the instant, and it is what the
        // mapper uses.
        var episodes = TvMazeFixtures.Episodes("frieren-episodes.json");

        var result = TvMazeAiringMapper.Map(episodes, seasonNumber: 2);

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
}
