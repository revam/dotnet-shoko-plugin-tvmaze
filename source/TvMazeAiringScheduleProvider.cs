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
using Shoko.Abstractions.Metadata.Tmdb;
using Shoko.Plugin.TvMaze.Client;
using Shoko.Plugin.TvMaze.Mapping;

namespace Shoko.Plugin.TvMaze;

/// <summary>
/// Airing schedule provider backed by <see href="https://www.tvmaze.com"/>,
/// keyed through a TMDB show's <see cref="ITmdbShow.TvdbShowID"/> (TVmaze has
/// no AniDB or TMDB IDs of its own to look shows up by). TMDB shows, seasons
/// and episodes are core <c>ISeries</c>/<c>ISeason</c>/<c>IEpisode</c>
/// entities, so no entity resolver is needed: shoko series pick schedules up
/// through their normal linked-entity walk.
/// </summary>
/// <remarks>
/// Only <see cref="AiringKind.Original"/> is supported. TVmaze does not
/// distinguish a dub or a subtitled release from the original broadcast, so
/// every track this provider writes is Original.
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

    /// <inheritdoc/>
    public async Task<bool> RefreshAsync(ISeries series, CancellationToken cancellationToken = default)
    {
        // Only TMDB shows can be keyed to TVmaze. Everything else - a shoko
        // series, an AniDB anime, an AniList anime - is not this provider's
        // business, so it answers "nothing to do" rather than guessing.
        if (series is not ITmdbShow show)
            return false;

        if (show.TvdbShowID is not { } tvdbShowId)
            return false;

        var tvMazeShow = await _client.LookupShowByTvdbIdAsync(tvdbShowId, cancellationToken).ConfigureAwait(false);
        if (tvMazeShow is null)
        {
            _logger.LogDebug("TVmaze has no show for TheTVDB ID {TvdbShowId} (TMDB show {TmdbShowId}).", tvdbShowId, show.ID);
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
            _logger.LogDebug("TVmaze show {TvMazeShowId} has no numbered episodes for TMDB show {TmdbShowId}.", tvMazeShow.Id, show.ID);
            return false;
        }

        var channels = TvMazeChannelMapper.Resolve(tvMazeShow);
        var latestSeasonNumber = seasonGroups[^1].Key;
        var wroteAnything = false;

        foreach (var group in seasonGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var seasonNumber = group.Key;
            var tmdbSeason = show.Seasons.FirstOrDefault(season => season.SeasonNumber == seasonNumber);
            if (tmdbSeason is null)
            {
                _logger.LogDebug(
                    "TVmaze season {SeasonNumber} of show {TvMazeShowId} has no matching TMDB season on show {TmdbShowId}; skipping it.",
                    seasonNumber,
                    tvMazeShow.Id,
                    show.ID
                );
                continue;
            }

            var isFinished = TvMazeScheduleCoverage.IsSeasonFinished(seasonNumber, latestSeasonNumber, tvMazeShow.Status);
            var episodeCount = tmdbSeason.Episodes.Count;

            if (WriteSeason(show, tmdbSeason, group, channels, isFinished, episodeCount, tvMazeShow.Url))
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
        string? url
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
            if (channel?.TimeZoneId is { } timeZoneId)
                TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out timeZone);

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
                Url = url,
            };

            var schedule = _airingScheduleService.AddOrUpdateSchedule(this, data);

            var airings = TvMazeEpisodeMatcher.Match(tvMazeEpisodes, tmdbSeason.SeasonNumber, tmdbSeason);
            if (airings.Count == 0)
                continue;

            _airingScheduleService.SetAirings(this, schedule, airings);
            wroteAnything = true;
        }

        return wroteAnything;
    }
}
