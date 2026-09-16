using System;
using Shoko.Plugin.TvMaze.Mapping;
using Xunit;

namespace Shoko.Plugin.TvMaze.Tests;

/// <summary>
/// Tests for deciding which TMDB shows the daily sweep should spend a TVmaze
/// request on.
/// </summary>
public class TvMazeSweepPlannerTests
{
    private static readonly DateOnly Today = new(2026, 9, 16);

    [Fact]
    public void A_show_with_no_tvdb_id_is_never_swept()
    {
        Assert.False(TvMazeSweepPlanner.ShouldSweep(hasTvdbShowId: false, endDate: null, stopSweepingEndedShowsAfterDays: 60, today: Today));
    }

    [Fact]
    public void A_show_with_no_end_date_is_always_swept()
    {
        Assert.True(TvMazeSweepPlanner.ShouldSweep(hasTvdbShowId: true, endDate: null, stopSweepingEndedShowsAfterDays: 60, today: Today));
    }

    [Fact]
    public void A_show_that_ended_recently_is_still_swept()
    {
        var endDate = Today.AddDays(-10);

        Assert.True(TvMazeSweepPlanner.ShouldSweep(hasTvdbShowId: true, endDate: endDate, stopSweepingEndedShowsAfterDays: 60, today: Today));
    }

    [Fact]
    public void A_show_that_ended_past_the_cutoff_stops_being_swept()
    {
        var endDate = Today.AddDays(-61);

        Assert.False(TvMazeSweepPlanner.ShouldSweep(hasTvdbShowId: true, endDate: endDate, stopSweepingEndedShowsAfterDays: 60, today: Today));
    }

    [Fact]
    public void The_cutoff_boundary_is_inclusive()
    {
        var endDate = Today.AddDays(-60);

        Assert.True(TvMazeSweepPlanner.ShouldSweep(hasTvdbShowId: true, endDate: endDate, stopSweepingEndedShowsAfterDays: 60, today: Today));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_cutoff_disables_the_cutoff_entirely(int stopAfterDays)
    {
        var longEnded = Today.AddYears(-10);

        Assert.True(TvMazeSweepPlanner.ShouldSweep(hasTvdbShowId: true, endDate: longEnded, stopSweepingEndedShowsAfterDays: stopAfterDays, today: Today));
    }
}
