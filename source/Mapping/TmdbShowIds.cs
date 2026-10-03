using System.Linq;
using Shoko.Abstractions.Metadata;

namespace Shoko.Plugin.TvMaze.Mapping;

/// <summary>
/// Reads the IDs TVmaze is keyed through off a TMDB show, which reaches the
/// plugin as a plain <see cref="ISeries"/> from <see cref="MetadataSource.TMDB"/>.
/// </summary>
public static class TmdbShowIds
{
    /// <summary>
    /// The source TMDB lists a show's TheTVDB ID under.
    /// </summary>
    private const string TvdbSourceValue = "tvdb";

    /// <summary>
    /// Whether the series is a TMDB show with a numeric TMDB ID.
    /// </summary>
    /// <param name="series">The series.</param>
    /// <returns><c>true</c> for a TMDB show.</returns>
    public static bool IsTmdbShow(this ISeries series)
        => series.GetTmdbID() > 0;

    /// <summary>
    /// The TMDB ID of a TMDB show.
    /// </summary>
    /// <param name="series">The series.</param>
    /// <returns>The TMDB show ID, or <c>0</c> when the series is not a TMDB show.</returns>
    public static int GetTmdbID(this ISeries series)
        => series.ID.Source == MetadataSource.TMDB && series.ID.EntityType == MetadataEntityType.Series && series.ID.TryGetNumericID<int>(out var tmdbID) && tmdbID > 0
            ? tmdbID
            : 0;

    /// <summary>
    /// The TheTVDB ID TMDB gave a show, as listed in its cross-source IDs.
    /// </summary>
    /// <param name="series">The series.</param>
    /// <returns>The TheTVDB show ID, or <c>null</c> when TMDB lists none.</returns>
    public static int? GetTvdbShowID(this ISeries series)
        => series.CrossSourceIDs
            .Where(id => id.Source.Value == TvdbSourceValue && id.EntityType == MetadataEntityType.Series)
            .Select(id => id.TryGetNumericID<int>(out var tvdbID) && tvdbID > 0 ? tvdbID : (int?)null)
            .FirstOrDefault(tvdbID => tvdbID is not null);
}
