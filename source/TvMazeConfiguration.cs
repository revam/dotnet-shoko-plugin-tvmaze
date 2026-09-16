using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Metadata.Airing;

namespace Shoko.Plugin.TvMaze;

/// <summary>
/// Configuration for the TVmaze airing schedule provider.
/// </summary>
[Display(Name = "TVmaze")]
public class TvMazeConfiguration : IAiringScheduleProviderConfiguration, INewtonsoftJsonConfiguration
{
    /// <summary>
    /// How often the provider's own sweep job (<see cref="Jobs.TvMazeSweepJob"/>)
    /// runs, refreshing every eligible TMDB show on its own schedule rather
    /// than waiting for a core-issued refresh hint. Changing this requires a
    /// restart, since the interval is only read when the recurring job is
    /// registered at startup.
    /// </summary>
    [Display(Name = "Sweep Interval")]
    [DefaultValue(typeof(TimeSpan), "1.00:00:00")]
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How many days past a show's known end date the sweep keeps refreshing
    /// it before giving up, in case TVmaze corrects an air date after the
    /// fact. Set to <c>0</c> to keep sweeping ended shows forever.
    /// </summary>
    [Display(Name = "Stop Sweeping Ended Shows After (days)")]
    [Range(0, int.MaxValue)]
    [DefaultValue(60)]
    public int StopSweepingEndedShowsAfterDays { get; set; } = 60;
}
