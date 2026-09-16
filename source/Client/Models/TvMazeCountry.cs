namespace Shoko.Plugin.TvMaze.Client.Models;

/// <summary>
/// The country a TVmaze network or web channel is based in. Deserialized
/// directly from the TVmaze API's <c>country</c> object.
/// </summary>
public sealed class TvMazeCountry
{
    /// <summary>
    /// The country's full name, e.g. <c>"Japan"</c>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The ISO 3166-1 alpha-2 country code, e.g. <c>"JP"</c>.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// The IANA time zone the country's broadcasts are timed in, e.g.
    /// <c>"Asia/Tokyo"</c>.
    /// </summary>
    public string? Timezone { get; set; }
}
