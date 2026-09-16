namespace Shoko.Plugin.TvMaze.Client.Models;

/// <summary>
/// A TVmaze network (broadcast) or web channel (streaming). The same shape
/// is used for both the <c>network</c> and <c>webChannel</c> fields of a
/// TVmaze show.
/// </summary>
public sealed class TvMazeNetwork
{
    /// <summary>
    /// The TVmaze internal ID of the network or web channel.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The display name, e.g. <c>"TV Tokyo"</c> or <c>"Crunchyroll"</c>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The country the network or web channel is based in. Broadcast
    /// networks always carry one; a worldwide streaming service may not.
    /// </summary>
    public TvMazeCountry? Country { get; set; }
}
