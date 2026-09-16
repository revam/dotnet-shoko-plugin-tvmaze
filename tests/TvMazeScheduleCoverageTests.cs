using Shoko.Plugin.TvMaze.Mapping;
using Xunit;

namespace Shoko.Plugin.TvMaze.Tests;

/// <summary>
/// Tests for inferring whether a season's schedule is finished from TVmaze's
/// single show-level status, since TVmaze reports one run status for the
/// whole show rather than per season.
/// </summary>
public class TvMazeScheduleCoverageTests
{
    [Theory]
    [InlineData("Running")]
    [InlineData("running")]
    [InlineData("RUNNING")]
    public void The_latest_season_of_a_running_show_is_not_finished(string status)
    {
        Assert.False(TvMazeScheduleCoverage.IsSeasonFinished(seasonNumber: 3, latestSeasonNumber: 3, showStatus: status));
    }

    [Theory]
    [InlineData("Ended")]
    [InlineData("To Be Determined")]
    [InlineData("In Development")]
    [InlineData(null)]
    public void The_latest_season_of_a_show_that_is_not_running_is_finished(string? status)
    {
        Assert.True(TvMazeScheduleCoverage.IsSeasonFinished(seasonNumber: 3, latestSeasonNumber: 3, showStatus: status));
    }

    [Fact]
    public void An_earlier_season_is_always_finished_even_while_the_show_is_running()
    {
        Assert.True(TvMazeScheduleCoverage.IsSeasonFinished(seasonNumber: 1, latestSeasonNumber: 3, showStatus: "Running"));
        Assert.True(TvMazeScheduleCoverage.IsSeasonFinished(seasonNumber: 2, latestSeasonNumber: 3, showStatus: "Running"));
    }

    [Fact]
    public void A_single_season_show_that_is_running_is_not_finished()
    {
        Assert.False(TvMazeScheduleCoverage.IsSeasonFinished(seasonNumber: 1, latestSeasonNumber: 1, showStatus: "Running"));
    }
}
