namespace Shoko.Plugin.TvMaze.Client.Models;

/// <summary>
/// A show, as returned by TVmaze's <c>/lookup/shows</c> and <c>/shows/{id}</c>
/// endpoints. Only the fields the airing schedule provider needs are mapped;
/// TVmaze returns a great deal more (genres, ratings, images, ...).
/// </summary>
public sealed class TvMazeShow
{
    /// <summary>
    /// The TVmaze internal ID of the show, used for every further request
    /// about it (e.g. its episode list).
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The show's display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The show's TVmaze page.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// The run status TVmaze tracks for the show, e.g. <c>"Running"</c>,
    /// <c>"Ended"</c>, <c>"To Be Determined"</c> or <c>"In Development"</c>.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// The broadcast network currently airing the show, or <c>null</c> when
    /// it airs on a streaming service instead (or on neither).
    /// </summary>
    public TvMazeNetwork? Network { get; set; }

    /// <summary>
    /// The streaming service currently airing the show, or <c>null</c> when
    /// it airs on a broadcast network instead (or on neither).
    /// </summary>
    public TvMazeNetwork? WebChannel { get; set; }
}
