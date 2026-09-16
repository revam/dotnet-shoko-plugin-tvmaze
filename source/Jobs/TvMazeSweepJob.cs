using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Plugin.TvMaze.Mapping;
using Shoko.QueueProcessor.Abstractions;
using Shoko.QueueProcessor.Acquisition.Attributes;
using Shoko.QueueProcessor.Concurrency;

namespace Shoko.Plugin.TvMaze.Jobs;

/// <summary>
/// The provider's own recurring sweep. Core only ever asks a provider to
/// refresh a specific series on request (a user click, a newly linked show);
/// nothing walks every provider for every series on a schedule. TVmaze owns
/// its own cadence instead, the same way a Syoboi or AnimeSchedule.net
/// provider would: this job runs daily (configurable through
/// <see cref="TvMazeConfiguration.SweepInterval"/>), finds every TMDB show
/// that could plausibly be keyed to TVmaze, and refreshes each one in turn.
/// </summary>
/// <remarks>
/// One instance runs at a time. Throughput is bounded by
/// <see cref="Client.TvMazeRateLimiter"/> inside the shared
/// <see cref="Client.TvMazeClient"/>, not by this job's own concurrency, so a
/// large library simply takes longer per sweep rather than risking TVmaze's
/// rate limit.
/// </remarks>
[DatabaseRequired]
[NetworkRequired]
[DisallowConcurrentExecution]
public class TvMazeSweepJob(
    IMetadataService metadataService,
    TvMazeAiringScheduleProvider provider,
    ConfigurationProvider<TvMazeConfiguration> configurationProvider,
    ILogger<TvMazeSweepJob> logger
) : IQueueJob
{
    /// <inheritdoc/>
    public string TypeName => "TVmaze Airing Sweep";

    /// <inheritdoc/>
    public string Title => "Sweeping TVmaze for airing schedules...";

    /// <inheritdoc/>
    public async Task Process()
    {
        var config = configurationProvider.Load();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var plan = TvMazeSweepPlanner.Plan(
            metadataService.GetAllSeriesForProvider(IMetadataService.ProviderName.TMDB),
            config.StopSweepingEndedShowsAfterDays,
            today
        );

        // A sweep that looks at nothing is the same shape as a broken sweep,
        // so say what was dropped and why rather than only how many are left.
        logger.LogInformation(
            "Sweeping {Count} of {Total} TMDB series for TVmaze airing schedules; left out {NotAShow} non-show series, {WithoutTvdbShowID} show(s) without a TheTVDB ID and {EndedTooLongAgo} show(s) that ended too long ago.",
            plan.Shows.Count,
            plan.TotalSeries,
            plan.NotAShow,
            plan.WithoutTvdbShowID,
            plan.EndedTooLongAgo
        );

        if (plan.Shows.Count == 0)
            return;

        var refreshed = 0;
        var failed = 0;
        foreach (var show in plan.Shows)
        {
            try
            {
                if (await provider.RefreshAsync(show).ConfigureAwait(false))
                    refreshed++;
                else
                    logger.LogDebug("TVmaze had nothing to write for TMDB show {TmdbShowID} (\"{ShowTitle}\").", show.ID, show.Title);
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex, "TVmaze sweep failed for TMDB show {TmdbShowID} (\"{ShowTitle}\").", show.ID, show.Title);
            }
        }

        logger.LogInformation(
            "TVmaze sweep complete: {Refreshed} refreshed, {Skipped} had nothing to do, {Failed} failed.",
            refreshed,
            plan.Shows.Count - refreshed - failed,
            failed
        );
    }
}
