using System;
using Moq;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Tmdb;
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

    [Fact]
    public void The_plan_counts_everything_it_leaves_out()
    {
        var series = new ISeries[]
        {
            TmdbMovieShapedSeries(),
            TmdbShow(tvdbShowId: null, endDate: null),
            TmdbShow(tvdbShowId: 1, endDate: Today.AddDays(-61)),
            TmdbShow(tvdbShowId: 2, endDate: Today.AddDays(-10)),
            TmdbShow(tvdbShowId: 3, endDate: null),
        };

        var plan = TvMazeSweepPlanner.Plan(series, stopSweepingEndedShowsAfterDays: 60, today: Today);

        Assert.Equal(5, plan.TotalSeries);
        Assert.Equal(1, plan.NotAShow);
        Assert.Equal(1, plan.WithoutTvdbShowID);
        Assert.Equal(1, plan.EndedTooLongAgo);
        Assert.Equal(2, plan.Shows.Count);
    }

    [Fact]
    public void An_empty_metadata_service_plans_an_empty_sweep()
    {
        var plan = TvMazeSweepPlanner.Plan([], stopSweepingEndedShowsAfterDays: 60, today: Today);

        Assert.Empty(plan.Shows);
        Assert.Equal(0, plan.TotalSeries);
    }

    private static ISeries TmdbMovieShapedSeries()
        => new Mock<ISeries>().Object;

    private static ITmdbShow TmdbShow(int? tvdbShowId, DateOnly? endDate)
    {
        var show = new Mock<ITmdbShow>();
        show.Setup(s => s.TvdbShowID).Returns(tvdbShowId);
        show.Setup(s => s.EndDate).Returns(endDate is { } date ? new PartialDateOnly(date) : null);
        return show.Object;
    }
}
