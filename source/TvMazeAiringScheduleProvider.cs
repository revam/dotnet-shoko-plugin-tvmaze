using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Airing;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Shoko;
using Shoko.Abstractions.Metadata.Tmdb;
using Shoko.Plugin.TvMaze.Client;
using Shoko.Plugin.TvMaze.Mapping;

namespace Shoko.Plugin.TvMaze;

/// <summary>
/// Airing schedule provider backed by <see href="https://www.tvmaze.com"/>,
/// keyed through a TMDB show's <see cref="ITmdbShow.TvdbShowID"/> (TVmaze has
/// no AniDB or TMDB IDs of its own to look shows up by). Schedules are always
/// written against TMDB shows, seasons and episodes, which are core
/// <c>ISeries</c>/<c>ISeason</c>/<c>IEpisode</c> entities, so no entity
/// resolver is needed: shoko series pick the schedules up through their
/// normal linked-entity walk.
/// </summary>
/// <remarks>
/// <para>
/// A refresh, on the other hand, is requested for whatever entity the caller
/// happened to have in hand — for an ordinary series refresh that is an
/// <see cref="IShokoSeries"/>, not an <see cref="ITmdbShow"/> — so
/// <see cref="RefreshAsync(ISeries, CancellationToken)"/> resolves the linked
/// TMDB shows itself instead of demanding one.
/// </para>
/// <para>
/// Only <see cref="AiringKind.Original"/> is supported. TVmaze does not
/// distinguish a dub or a subtitled release from the original broadcast, so
/// every track this provider writes is Original.
/// </para>
/// </remarks>
public sealed class TvMazeAiringScheduleProvider : IAiringScheduleProvider<TvMazeConfiguration>
{
    private readonly TvMazeClient _client;
    private readonly IAiringScheduleService _airingScheduleService;
    private readonly ILogger<TvMazeAiringScheduleProvider> _logger;

    /// <inheritdoc/>
    public string Name => "TVmaze";

    /// <inheritdoc/>
    public string Description => "Broadcast and streaming airing times for TMDB-linked shows, from tvmaze.com.";

    /// <inheritdoc/>
    public IReadOnlySet<AiringKind> AvailableKinds { get; } = new HashSet<AiringKind> { AiringKind.Original };

    /// <summary>
    /// Initializes a new instance of the <see cref="TvMazeAiringScheduleProvider"/> class.
    /// </summary>
    /// <param name="client">The TVmaze API client.</param>
    /// <param name="airingScheduleService">The airing schedule service.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public TvMazeAiringScheduleProvider(TvMazeClient client, IAiringScheduleService airingScheduleService, ILogger<TvMazeAiringScheduleProvider> logger)
    {
        _client = client;
        _airingScheduleService = airingScheduleService;
        _logger = logger;
    }

    #region Refreshing

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="series"/> is <c>null</c>.
    /// </exception>
    public async Task<bool> RefreshAsync(ISeries series, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(series);

        var shows = ResolveTmdbShows(series);
        if (shows.Count == 0)
            return false;

        return await RefreshTmdbShowsAsync(shows, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the TMDB shows a refresh request should be carried out
    /// against. TVmaze is keyed through a TMDB show's TheTVDB ID, but a
    /// refresh arrives for whatever entity the request was made for, so any
    /// series that can reach a TMDB show is accepted:
    /// <list type="bullet">
    ///   <item>an <see cref="ITmdbShow"/> is used as-is;</item>
    ///   <item>
    ///     an <see cref="IShokoSeries"/> resolves to every TMDB show it is
    ///     linked to;
    ///   </item>
    ///   <item>
    ///     anything else — an AniDB anime, an AniList anime — goes through its
    ///     shoko series to reach the same TMDB shows.
    ///   </item>
    /// </list>
    /// Every dead end is logged, because a refresh that silently does nothing
    /// is indistinguishable from a broken provider.
    /// </summary>
    /// <param name="series">The series the refresh was requested for.</param>
    /// <returns>
    /// The distinct TMDB shows to refresh, which is empty when the series
    /// reaches none.
    /// </returns>
    private IReadOnlyList<ITmdbShow> ResolveTmdbShows(ISeries series)
    {
        switch (series)
        {
            case ITmdbShow show:
                return [show];

            case IShokoSeries shokoSeries:
            {
                var shows = Distinct(shokoSeries.TmdbShows);
                if (shows.Count == 0)
                    _logger.LogDebug(
                        "Skipping shoko series {ShokoSeriesID} (\"{SeriesTitle}\"): it has no linked TMDB shows to key TVmaze through.",
                        shokoSeries.ID,
                        shokoSeries.Title
                    );
                else
                    _logger.LogDebug(
                        "Shoko series {ShokoSeriesID} (\"{SeriesTitle}\") resolved to {Count} linked TMDB show(s): {TmdbShowIDs}.",
                        shokoSeries.ID,
                        shokoSeries.Title,
                        shows.Count,
                        string.Join(", ", shows.Select(show => show.ID))
                    );
                return shows;
            }

            default:
            {
                var shows = Distinct(series.ShokoSeries.SelectMany(shokoSeries => shokoSeries.TmdbShows));
                if (shows.Count == 0)
                    _logger.LogDebug(
                        "Skipping {Source} series {SeriesID} (\"{SeriesTitle}\"): it is not a TMDB show, and reaches none through its {ShokoSeriesCount} shoko series.",
                        series.Source,
                        series.ID,
                        series.Title,
                        series.ShokoSeries.Count
                    );
                else
                    _logger.LogDebug(
                        "{Source} series {SeriesID} (\"{SeriesTitle}\") resolved to {Count} linked TMDB show(s) through its shoko series: {TmdbShowIDs}.",
                        series.Source,
                        series.ID,
                        series.Title,
                        shows.Count,
                        string.Join(", ", shows.Select(show => show.ID))
                    );
                return shows;
            }
        }

        static IReadOnlyList<ITmdbShow> Distinct(IEnumerable<ITmdbShow> shows)
            => shows.DistinctBy(show => show.ID).ToList();
    }

    /// <summary>
    /// Refreshes every given TMDB show, grouped by TheTVDB ID so each distinct
    /// TVmaze show costs one lookup and one episode list no matter how many
    /// TMDB shows are keyed to it. A split-cour anime linked to several TMDB
    /// shows gets a schedule per show, since each one carries its own seasons
    /// and episodes for the airings to hang off.
    /// </summary>
    /// <param name="shows">The TMDB shows to refresh.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><c>true</c> when at least one show produced a schedule.</returns>
    private async Task<bool> RefreshTmdbShowsAsync(IReadOnlyList<ITmdbShow> shows, CancellationToken cancellationToken)
    {
        var keyed = new List<(int TvdbShowID, ITmdbShow Show)>(shows.Count);
        foreach (var show in shows)
        {
            if (show.TvdbShowID is not { } tvdbShowId)
            {
                _logger.LogDebug(
                    "Skipping TMDB show {TmdbShowID} (\"{ShowTitle}\"): it has no TheTVDB ID, which is the only key TVmaze can be looked up by.",
                    show.ID,
                    show.Title
                );
                continue;
            }

            keyed.Add((tvdbShowId, show));
        }

        if (keyed.Count == 0)
            return false;

        var wroteAnything = false;
        foreach (var group in keyed.GroupBy(entry => entry.TvdbShowID))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var groupedShows = group.Select(entry => entry.Show).ToList();
            if (await RefreshByTvdbShowIdAsync(group.Key, groupedShows, cancellationToken).ConfigureAwait(false))
                wroteAnything = true;
        }

        return wroteAnything;
    }

    /// <summary>
    /// Fetches the TVmaze show for one TheTVDB ID and writes its schedule for
    /// every TMDB show keyed to that ID.
    /// </summary>
    /// <param name="tvdbShowId">The TheTVDB show ID to look TVmaze up by.</param>
    /// <param name="shows">The TMDB shows carrying that TheTVDB ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><c>true</c> when at least one show produced a schedule.</returns>
    private async Task<bool> RefreshByTvdbShowIdAsync(int tvdbShowId, IReadOnlyList<ITmdbShow> shows, CancellationToken cancellationToken)
    {
        var tvMazeShow = await _client.LookupShowByTvdbIdAsync(tvdbShowId, cancellationToken).ConfigureAwait(false);
        if (tvMazeShow is null)
        {
            _logger.LogDebug(
                "TVmaze has no show for TheTVDB ID {TvdbShowID} (TMDB show(s) {TmdbShowIDs}).",
                tvdbShowId,
                string.Join(", ", shows.Select(show => show.ID))
            );
            return false;
        }

        var episodes = await _client.GetEpisodesAsync(tvMazeShow.Id, cancellationToken).ConfigureAwait(false);
        var seasonGroups = episodes
            .Where(episode => episode.Season is > 0)
            .GroupBy(episode => episode.Season!.Value)
            .OrderBy(group => group.Key)
            .ToList();

        if (seasonGroups.Count == 0)
        {
            _logger.LogDebug(
                "TVmaze show {TvMazeShowID} has no numbered episodes among its {EpisodeCount} episode(s), for TMDB show(s) {TmdbShowIDs}.",
                tvMazeShow.Id,
                episodes.Count,
                string.Join(", ", shows.Select(show => show.ID))
            );
            return false;
        }

        var channels = TvMazeChannelMapper.Resolve(tvMazeShow);
        if (channels.Count == 0)
            _logger.LogDebug(
                "TVmaze show {TvMazeShowID} names neither a network nor a web channel; its airings are recorded without a channel.",
                tvMazeShow.Id
            );

        var latestSeasonNumber = seasonGroups[^1].Key;
        var wroteAnything = false;
        foreach (var show in shows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (WriteShow(show, tvMazeShow, seasonGroups, channels, latestSeasonNumber, cancellationToken))
                wroteAnything = true;
        }

        return wroteAnything;
    }

    /// <summary>
    /// Writes every TVmaze season that has a counterpart on the TMDB show.
    /// </summary>
    /// <param name="show">The TMDB show to write the schedules for.</param>
    /// <param name="tvMazeShow">The TVmaze show the episodes came from.</param>
    /// <param name="seasonGroups">The TVmaze episodes, grouped by season number, in ascending order.</param>
    /// <param name="channels">The channels resolved for the TVmaze show.</param>
    /// <param name="latestSeasonNumber">The highest TVmaze season number for the show.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><c>true</c> when at least one season produced a schedule.</returns>
    private bool WriteShow(
        ITmdbShow show,
        Client.Models.TvMazeShow tvMazeShow,
        IReadOnlyList<IGrouping<int, Client.Models.TvMazeEpisode>> seasonGroups,
        IReadOnlyList<TvMazeChannelDescriptor> channels,
        int latestSeasonNumber,
        CancellationToken cancellationToken
    )
    {
        var wroteAnything = false;
        foreach (var group in seasonGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var seasonNumber = group.Key;
            var tmdbSeason = show.Seasons.FirstOrDefault(season => season.SeasonNumber == seasonNumber);
            if (tmdbSeason is null)
            {
                _logger.LogDebug(
                    "TVmaze season {SeasonNumber} of show {TvMazeShowID} has no matching TMDB season on show {TmdbShowID}; skipping it.",
                    seasonNumber,
                    tvMazeShow.Id,
                    show.ID
                );
                continue;
            }

            var isFinished = TvMazeScheduleCoverage.IsSeasonFinished(seasonNumber, latestSeasonNumber, tvMazeShow.Status);
            var episodeCount = tmdbSeason.Episodes.Count;

            if (WriteSeason(show, tmdbSeason, group, channels, isFinished, episodeCount, tvMazeShow))
                wroteAnything = true;
        }

        return wroteAnything;
    }

    private bool WriteSeason(
        ITmdbShow show,
        ITmdbSeason tmdbSeason,
        IEnumerable<Client.Models.TvMazeEpisode> tvMazeEpisodesInSeason,
        IReadOnlyList<TvMazeChannelDescriptor> channels,
        bool isFinished,
        int episodeCount,
        Client.Models.TvMazeShow tvMazeShow
    )
    {
        var tvMazeEpisodes = tvMazeEpisodesInSeason as IReadOnlyCollection<Client.Models.TvMazeEpisode> ?? tvMazeEpisodesInSeason.ToList();
        var wroteAnything = false;

        // A show with neither a network nor a web channel still has airing
        // times worth recording, just with nothing to attribute them to.
        var channelSlots = channels.Count > 0
            ? channels.Select(channel => (TvMazeChannelDescriptor?)channel).ToList()
            : [null];

        foreach (var channel in channelSlots)
        {
            var airingChannel = channel is null
                ? null
                : _airingScheduleService.FindOrRegisterChannel(channel.Name, channel.Type);

            TimeZoneInfo? timeZone = null;
            if (channel?.TimeZoneId is { } timeZoneId && !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out timeZone))
                _logger.LogDebug(
                    "TVmaze gave the unknown time zone \"{TimeZoneID}\" for channel \"{ChannelName}\"; the schedule is written without a display time zone.",
                    timeZoneId,
                    channel.Name
                );

            var data = new AiringScheduleData
            {
                Series = show,
                Season = tmdbSeason,
                ChannelID = airingChannel?.ID,
                Tracks = [new AiringTrackData(AiringKind.Original, show.OriginalLanguageCode, channel?.CountryCode)],
                FirstEpisodeNumber = episodeCount > 0 ? 1 : null,
                LastEpisodeNumber = episodeCount > 0 ? episodeCount : null,
                IsFinished = isFinished,
                TimeZone = timeZone,
                // Left null on purpose: the identity is then derived from the
                // channel and track set, so a show that moves network or web
                // channel between refreshes naturally gets a new schedule
                // instead of AddOrUpdateSchedule throwing over the channel
                // change. See the "Known limitations" section of the README.
                Key = null,
                Url = tvMazeShow.Url,
            };

            var schedule = _airingScheduleService.AddOrUpdateSchedule(this, data);

            var result = TvMazeEpisodeMatcher.Match(tvMazeEpisodes, tmdbSeason.SeasonNumber, tmdbSeason);
            if (result.SkippedWithoutAirstamp > 0 || result.SkippedWithoutTmdbEpisode > 0)
                _logger.LogDebug(
                    "TVmaze season {SeasonNumber} of show {TvMazeShowID}: {Matched} episode(s) matched TMDB show {TmdbShowID}, {WithoutAirstamp} had no air time yet, and {WithoutEpisode} had no matching TMDB episode.",
                    tmdbSeason.SeasonNumber,
                    tvMazeShow.Id,
                    result.Airings.Count,
                    show.ID,
                    result.SkippedWithoutAirstamp,
                    result.SkippedWithoutTmdbEpisode
                );

            if (result.Airings.Count == 0)
            {
                _logger.LogDebug(
                    "TVmaze season {SeasonNumber} of show {TvMazeShowID} matched no episodes on TMDB season {TmdbSeasonID} of show {TmdbShowID}; the schedule is left without airings.",
                    tmdbSeason.SeasonNumber,
                    tvMazeShow.Id,
                    tmdbSeason.ID,
                    show.ID
                );
                continue;
            }

            _airingScheduleService.SetAirings(this, schedule, result.Airings);
            wroteAnything = true;
        }

        return wroteAnything;
    }

    #endregion
}
