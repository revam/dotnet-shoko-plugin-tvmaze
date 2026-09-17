using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Tmdb;
using Xunit;

namespace Shoko.Plugin.TvMaze.Tests;

/// <summary>
/// Tests for the core-driven sweep: the server hands the provider a cursor
/// and a deadline, and expects a chunk that stops when the deadline fires and
/// says where the next one picks up.
/// </summary>
public class TvMazeSweepTests
{
    private static readonly DateTimeOffset Today = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    #region Cursors

    [Fact]
    public async Task A_sweep_that_walks_every_show_ends_with_no_cursor()
    {
        // No fixture is registered for either TheTVDB ID, so TVmaze answers
        // that it has no such show and the walk itself is what is under test.
        using var api = new StubTvMazeApi();
        var provider = Provider(api, TmdbShow(id: 10, tvdbShowId: 100), TmdbShow(id: 20, tvdbShowId: 200));

        var cursor = await provider.SweepAsync(null, TestContext.Current.CancellationToken);

        Assert.Null(cursor);
        Assert.Equal(["/lookup/shows?thetvdb=100", "/lookup/shows?thetvdb=200"], api.Requests);
    }

    [Fact]
    public async Task A_sweep_resumes_after_the_show_the_cursor_names()
    {
        using var api = new StubTvMazeApi();
        var provider = Provider(api, TmdbShow(id: 10, tvdbShowId: 100), TmdbShow(id: 20, tvdbShowId: 200));

        var cursor = await provider.SweepAsync("10", TestContext.Current.CancellationToken);

        Assert.Null(cursor);
        Assert.Equal(["/lookup/shows?thetvdb=200"], api.Requests);
    }

    [Fact]
    public async Task A_sweep_with_nothing_left_to_walk_ends_without_asking_tvmaze()
    {
        using var api = new StubTvMazeApi();
        var provider = Provider(api, TmdbShow(id: 10, tvdbShowId: 100));

        var cursor = await provider.SweepAsync("9000", TestContext.Current.CancellationToken);

        Assert.Null(cursor);
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task An_unreadable_cursor_starts_the_sweep_over_rather_than_ending_it()
    {
        using var api = new StubTvMazeApi();
        var logger = new RecordingLogger<TvMazeAiringScheduleProvider>();
        var provider = Provider(api, logger, TmdbShow(id: 10, tvdbShowId: 100));

        var cursor = await provider.SweepAsync("yesterday", TestContext.Current.CancellationToken);

        Assert.Null(cursor);
        Assert.Equal(["/lookup/shows?thetvdb=100"], api.Requests);
        Assert.Contains(LogLevel.Warning, logger.Entries);
    }

    #endregion

    #region Deadlines

    [Fact]
    public async Task A_chunk_whose_budget_is_already_spent_hands_its_cursor_straight_back()
    {
        using var api = new StubTvMazeApi();
        var provider = Provider(api, TmdbShow(id: 10, tvdbShowId: 100), TmdbShow(id: 20, tvdbShowId: 200));
        using var deadline = new CancellationTokenSource();
        await deadline.CancelAsync();

        var cursor = await provider.SweepAsync("10", deadline.Token);

        Assert.Equal("10", cursor);
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task A_chunk_that_runs_out_of_budget_resumes_at_the_last_show_it_finished()
    {
        using var deadline = new CancellationTokenSource();
        // The first show is looked up and answered; the deadline fires during
        // the second, the way it fires mid-request on a slow source.
        using var api = new StubTvMazeApi((_, requests) =>
        {
            if (requests.Count < 2)
                return false;

            deadline.Cancel();
            return true;
        });
        var provider = Provider(api, TmdbShow(id: 10, tvdbShowId: 100), TmdbShow(id: 20, tvdbShowId: 200));

        var cursor = await provider.SweepAsync(null, deadline.Token);

        // The first show, so the work the chunk did is kept.
        Assert.Equal("10", cursor);
    }

    #endregion

    #region What a sweep leaves out

    [Fact]
    public async Task A_show_that_ended_past_the_cutoff_is_never_asked_about()
    {
        using var api = new StubTvMazeApi();
        var provider = Provider(
            api,
            TmdbShow(id: 10, tvdbShowId: 100, endDate: DateOnly.FromDateTime(Today.UtcDateTime).AddDays(-61)),
            TmdbShow(id: 20, tvdbShowId: 200, endDate: DateOnly.FromDateTime(Today.UtcDateTime).AddDays(-10))
        );

        var cursor = await provider.SweepAsync(null, TestContext.Current.CancellationToken);

        Assert.Null(cursor);
        Assert.Equal(["/lookup/shows?thetvdb=200"], api.Requests);
    }

    #endregion

    #region Logging

    [Fact]
    public async Task A_sweep_says_nothing_at_information_level()
    {
        using var api = new StubTvMazeApi();
        var logger = new RecordingLogger<TvMazeAiringScheduleProvider>();
        var provider = Provider(api, logger, TmdbShow(id: 10, tvdbShowId: 100));

        await provider.SweepAsync(null, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(LogLevel.Information, logger.Entries);
    }

    #endregion

    #region Helpers

    private static ITmdbShow TmdbShow(int id, int tvdbShowId, DateOnly? endDate = null)
        => TvMazeHost.TmdbShow(id, tvdbShowId, endDate);

    private static TvMazeAiringScheduleProvider Provider(StubTvMazeApi api, params ISeries[] series)
        => Provider(api, new RecordingLogger<TvMazeAiringScheduleProvider>(), series);

    private static TvMazeAiringScheduleProvider Provider(StubTvMazeApi api, RecordingLogger<TvMazeAiringScheduleProvider> logger, params ISeries[] series)
        => new(
            api.Client,
            new RecordingScheduleService().Object,
            TvMazeHost.MetadataService(series),
            TvMazeHost.ConfigurationProvider(),
            logger,
            new ManualTimeProvider(Today)
        );

    #endregion
}
