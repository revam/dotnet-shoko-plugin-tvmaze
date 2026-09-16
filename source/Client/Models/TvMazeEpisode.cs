using System;

namespace Shoko.Plugin.TvMaze.Client.Models;

/// <summary>
/// One episode, as returned by TVmaze's <c>/shows/{id}/episodes</c> endpoint.
/// By default that endpoint excludes specials, so <see cref="Season"/> and
/// <see cref="Number"/> are numbered exactly like a TMDB season's regular
/// episodes.
/// </summary>
public sealed class TvMazeEpisode
{
    /// <summary>
    /// The TVmaze internal ID of the episode.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The episode's display name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The season the episode belongs to, in TVmaze's own numbering.
    /// </summary>
    public int? Season { get; set; }

    /// <summary>
    /// The episode number within its season. <c>null</c> for some specials,
    /// which is why the episodes endpoint is queried without them.
    /// </summary>
    public int? Number { get; set; }

    /// <summary>
    /// The episode type, e.g. <c>"regular"</c> or <c>"significant_special"</c>.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// The precise airing instant, in UTC. <c>null</c> for an episode with no
    /// confirmed air date and time yet.
    /// </summary>
    public DateTimeOffset? Airstamp { get; set; }
}
