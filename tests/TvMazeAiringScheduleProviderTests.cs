using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Airing;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Shoko;
using Shoko.Abstractions.Metadata.Tmdb;
using Xunit;

namespace Shoko.Plugin.TvMaze.Tests;

/// <summary>
/// Tests for what the provider does with the entity a refresh is requested
/// for. The core hands <c>RefreshAsync</c> whatever the refresh was asked for
/// — an <see cref="IShokoSeries"/> for an ordinary series refresh — so
/// demanding an <see cref="ITmdbShow"/> would leave the provider silently
/// doing nothing, which is exactly the bug these tests pin down. Every
/// response is a real TVmaze capture; see <see cref="TvMazeFixtures"/>.
/// </summary>
public class TvMazeAiringScheduleProviderTests
{
    private const int FrierenTvdbShowID = 424536;

    private const int FrierenTvMazeShowID = 69956;

    private const int OnePieceTvdbShowID = 81797;

    private const int OnePieceTvMazeShowID = 1505;

    #region Shoko series → linked TMDB shows

    [Fact]
    public async Task A_shoko_series_resolves_its_linked_tmdb_show_instead_of_being_skipped()
    {
        using var api = new StubTvMazeApi()
            .WithShow(FrierenTvdbShowID, "frieren-show.json")
            .WithEpisodes(FrierenTvMazeShowID, "frieren-episodes.json");
        var service = new RecordingScheduleService();
        var show = TmdbShow(id: 209867, tvdbShowId: FrierenTvdbShowID, TmdbSeason(seasonNumber: 1, id: "1", episodeCount: 28));
        var series = ShokoSeries(id: 42, show);

        var refreshed = await Provider(api, service).RefreshAsync(series, TestContext.Current.CancellationToken);

        Assert.True(refreshed);
        Assert.Equal([$"/lookup/shows?thetvdb={FrierenTvdbShowID}", $"/shows/{FrierenTvMazeShowID}/episodes"], api.Requests);
        var written = Assert.Single(service.WrittenAirings);
        Assert.Equal(3, written.Airings.Count);
    }

    [Fact]
    public async Task A_shoko_series_linked_to_two_tmdb_shows_on_one_tvdb_show_is_looked_up_once_and_written_twice()
    {
        // The split-cour shape: TheTVDB (and so TVmaze) has one show with two
        // seasons, while TMDB has one show per cour. Both cours are keyed to
        // the same TheTVDB ID, so one lookup serves both.
        using var api = new StubTvMazeApi()
            .WithShow(FrierenTvdbShowID, "frieren-show.json")
            .WithEpisodes(FrierenTvMazeShowID, "frieren-episodes.json");
        var service = new RecordingScheduleService();
        var firstCour = TmdbShow(id: 209867, tvdbShowId: FrierenTvdbShowID, TmdbSeason(seasonNumber: 1, id: "1", episodeCount: 28));
        var secondCour = TmdbShow(id: 209868, tvdbShowId: FrierenTvdbShowID, TmdbSeason(seasonNumber: 2, id: "2", episodeCount: 10));
        var series = ShokoSeries(id: 42, firstCour, secondCour);

        var refreshed = await Provider(api, service).RefreshAsync(series, TestContext.Current.CancellationToken);

        Assert.True(refreshed);
        Assert.Equal([$"/lookup/shows?thetvdb={FrierenTvdbShowID}", $"/shows/{FrierenTvMazeShowID}/episodes"], api.Requests);
        Assert.Equal(2, service.WrittenAirings.Count);
        Assert.Equal([firstCour, secondCour], service.WrittenAirings.Select(entry => entry.Schedule.Series));
        Assert.Equal([1, 2], service.WrittenAirings.Select(entry => entry.Schedule.Season!.SeasonNumber));
    }

    [Fact]
    public async Task A_shoko_series_linked_to_two_tvdb_shows_looks_each_one_up()
    {
        // One Piece is included on purpose: TVmaze numbers its seasons by
        // broadcast year (1999, 2000, ...), so nothing lines up with a TMDB
        // season and it writes no airings - while the other linked show still
        // does, and the refresh still counts as work done.
        using var api = new StubTvMazeApi()
            .WithShow(FrierenTvdbShowID, "frieren-show.json")
            .WithEpisodes(FrierenTvMazeShowID, "frieren-episodes.json")
            .WithShow(OnePieceTvdbShowID, "one-piece-show.json")
            .WithEpisodes(OnePieceTvMazeShowID, "one-piece-episodes.json");
        var service = new RecordingScheduleService();
        var frieren = TmdbShow(id: 209867, tvdbShowId: FrierenTvdbShowID, TmdbSeason(seasonNumber: 1, id: "1", episodeCount: 28));
        var onePiece = TmdbShow(id: 37854, tvdbShowId: OnePieceTvdbShowID, TmdbSeason(seasonNumber: 1, id: "2", episodeCount: 61));
        var series = ShokoSeries(id: 42, frieren, onePiece);

        var refreshed = await Provider(api, service).RefreshAsync(series, TestContext.Current.CancellationToken);

        Assert.True(refreshed);
        Assert.Equal(
            [
                $"/lookup/shows?thetvdb={FrierenTvdbShowID}",
                $"/shows/{FrierenTvMazeShowID}/episodes",
                $"/lookup/shows?thetvdb={OnePieceTvdbShowID}",
                $"/shows/{OnePieceTvMazeShowID}/episodes",
            ],
            api.Requests
        );
        var written = Assert.Single(service.WrittenAirings);
        Assert.Equal(frieren, written.Schedule.Series);
    }

    [Fact]
    public async Task The_same_tmdb_show_linked_twice_is_only_refreshed_once()
    {
        using var api = new StubTvMazeApi()
            .WithShow(FrierenTvdbShowID, "frieren-show.json")
            .WithEpisodes(FrierenTvMazeShowID, "frieren-episodes.json");
        var service = new RecordingScheduleService();
        var show = TmdbShow(id: 209867, tvdbShowId: FrierenTvdbShowID, TmdbSeason(seasonNumber: 1, id: "1", episodeCount: 28));
        var series = ShokoSeries(id: 42, show, show);

        Assert.True(await Provider(api, service).RefreshAsync(series, TestContext.Current.CancellationToken));

        Assert.Single(service.WrittenAirings);
    }

    [Fact]
    public async Task A_shoko_series_with_no_linked_tmdb_shows_does_nothing_without_calling_tvmaze()
    {
        using var api = new StubTvMazeApi();
        var service = new RecordingScheduleService();

        var refreshed = await Provider(api, service).RefreshAsync(ShokoSeries(id: 42), TestContext.Current.CancellationToken);

        Assert.False(refreshed);
        Assert.Empty(api.Requests);
        Assert.Empty(service.Schedules);
    }

    [Fact]
    public async Task Another_series_reaches_its_tmdb_shows_through_its_shoko_series()
    {
        using var api = new StubTvMazeApi()
            .WithShow(FrierenTvdbShowID, "frieren-show.json")
            .WithEpisodes(FrierenTvMazeShowID, "frieren-episodes.json");
        var service = new RecordingScheduleService();
        var show = TmdbShow(id: 209867, tvdbShowId: FrierenTvdbShowID, TmdbSeason(seasonNumber: 1, id: "1", episodeCount: 28));
        var anidbAnime = new Mock<ISeries>();
        anidbAnime.Setup(s => s.Source).Returns(Shoko.Abstractions.Metadata.Enums.DataSource.AniDB);
        anidbAnime.Setup(s => s.ShokoSeries).Returns([ShokoSeries(id: 42, show)]);

        Assert.True(await Provider(api, service).RefreshAsync(anidbAnime.Object, TestContext.Current.CancellationToken));

        Assert.Single(service.WrittenAirings);
    }

    #endregion

    #region TMDB shows

    [Fact]
    public async Task A_tmdb_show_is_still_refreshed_directly()
    {
        using var api = new StubTvMazeApi()
            .WithShow(FrierenTvdbShowID, "frieren-show.json")
            .WithEpisodes(FrierenTvMazeShowID, "frieren-episodes.json");
        var service = new RecordingScheduleService();
        var show = TmdbShow(id: 209867, tvdbShowId: FrierenTvdbShowID, TmdbSeason(seasonNumber: 1, id: "1", episodeCount: 28));

        Assert.True(await Provider(api, service).RefreshAsync(show, TestContext.Current.CancellationToken));

        Assert.Single(service.WrittenAirings);
    }

    [Fact]
    public async Task A_tmdb_show_without_a_tvdb_id_does_nothing_without_calling_tvmaze()
    {
        using var api = new StubTvMazeApi();
        var service = new RecordingScheduleService();
        var show = TmdbShow(id: 209867, tvdbShowId: null, TmdbSeason(seasonNumber: 1, id: "1", episodeCount: 28));

        Assert.False(await Provider(api, service).RefreshAsync(show, TestContext.Current.CancellationToken));

        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task A_tvdb_id_tvmaze_has_no_show_for_does_nothing()
    {
        using var api = new StubTvMazeApi();
        var service = new RecordingScheduleService();
        var show = TmdbShow(id: 209867, tvdbShowId: FrierenTvdbShowID, TmdbSeason(seasonNumber: 1, id: "1", episodeCount: 28));

        Assert.False(await Provider(api, service).RefreshAsync(show, TestContext.Current.CancellationToken));

        Assert.Equal([$"/lookup/shows?thetvdb={FrierenTvdbShowID}"], api.Requests);
        Assert.Empty(service.Schedules);
    }

    #endregion

    #region Channels, time zones and timestamps

    [Fact]
    public async Task The_network_reaches_shoko_as_a_regional_television_channel()
    {
        using var api = new StubTvMazeApi()
            .WithShow(FrierenTvdbShowID, "frieren-show.json")
            .WithEpisodes(FrierenTvMazeShowID, "frieren-episodes.json");
        var service = new RecordingScheduleService();
        var show = TmdbShow(id: 209867, tvdbShowId: FrierenTvdbShowID, TmdbSeason(seasonNumber: 1, id: "1", episodeCount: 28));

        await Provider(api, service).RefreshAsync(show, TestContext.Current.CancellationToken);

        // NTV, in Japan, is what the live /lookup/shows?thetvdb=424536
        // response names as the show's network.
        var channel = Assert.Single(service.RegisteredChannels);
        Assert.Equal("NTV (JP)", channel.Name);
        Assert.Equal(AiringChannelType.Television, channel.Type);

        var schedule = Assert.Single(service.Schedules);
        Assert.Equal(service.ChannelIDs["NTV (JP)"], schedule.ChannelID);
        Assert.Equal("Asia/Tokyo", schedule.TimeZone?.Id);
        Assert.Equal("https://www.tvmaze.com/shows/69956/frieren-beyond-journeys-end", schedule.Url);
        var track = Assert.Single(schedule.Tracks);
        Assert.Equal(AiringKind.Original, track.Kind);
        Assert.Equal("JP", track.CountryCode);
    }

    [Fact]
    public async Task Airings_reach_shoko_as_utc_instants_taken_from_the_airstamp()
    {
        using var api = new StubTvMazeApi()
            .WithShow(FrierenTvdbShowID, "frieren-show.json")
            .WithEpisodes(FrierenTvMazeShowID, "frieren-episodes.json");
        var service = new RecordingScheduleService();
        // Season 2 airs in the 01:00 JST late-night slot, where TVmaze's
        // airdate (2026-01-16) is the broadcast day and the airstamp is the
        // real instant, 2026-01-17T01:00+09:00. Anything built from airdate +
        // airtime instead would land a full day early.
        var show = TmdbShow(id: 209868, tvdbShowId: FrierenTvdbShowID, TmdbSeason(seasonNumber: 2, id: "2", episodeCount: 10));

        await Provider(api, service).RefreshAsync(show, TestContext.Current.CancellationToken);

        var written = Assert.Single(service.WrittenAirings);
        Assert.Equal(
            [
                new DateTime(2026, 1, 16, 16, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 23, 16, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 30, 16, 0, 0, DateTimeKind.Utc),
            ],
            written.Airings.Select(airing => airing.AiredAt)
        );
        Assert.All(written.Airings, airing => Assert.Equal(DateTimeKind.Utc, airing.AiredAt!.Value.Kind));
        Assert.Equal(["tvmaze:3449494", "tvmaze:3526004", "tvmaze:3526005"], written.Airings.Select(airing => airing.Key));
    }

    [Fact]
    public async Task A_season_below_the_latest_one_of_a_running_show_is_finished_and_the_latest_is_not()
    {
        using var api = new StubTvMazeApi()
            .WithShow(FrierenTvdbShowID, "frieren-show.json")
            .WithEpisodes(FrierenTvMazeShowID, "frieren-episodes.json");
        var service = new RecordingScheduleService();
        var show = TmdbShow(
            id: 209867,
            tvdbShowId: FrierenTvdbShowID,
            TmdbSeason(seasonNumber: 1, id: "1", episodeCount: 28),
            TmdbSeason(seasonNumber: 2, id: "2", episodeCount: 10)
        );

        await Provider(api, service).RefreshAsync(show, TestContext.Current.CancellationToken);

        Assert.Equal(2, service.Schedules.Count);
        Assert.True(service.Schedules[0].IsFinished);
        Assert.False(service.Schedules[1].IsFinished);
    }

    #endregion

    #region Helpers

    private static TvMazeAiringScheduleProvider Provider(StubTvMazeApi api, RecordingScheduleService service)
        => new(api.Client, service.Object, NullLogger<TvMazeAiringScheduleProvider>.Instance);

    private static ITmdbEpisode TmdbEpisode(int number)
    {
        var episode = new Mock<ITmdbEpisode>();
        episode.Setup(e => e.EpisodeNumber).Returns(number);
        return episode.Object;
    }

    private static ITmdbSeason TmdbSeason(int seasonNumber, string id, int episodeCount)
    {
        var season = new Mock<ITmdbSeason>();
        season.Setup(s => s.ID).Returns(id);
        season.Setup(s => s.SeasonNumber).Returns(seasonNumber);
        season.Setup(s => s.Episodes).Returns(Enumerable.Range(1, episodeCount).Select(TmdbEpisode).ToList());
        return season.Object;
    }

    private static ITmdbShow TmdbShow(int id, int? tvdbShowId, params ITmdbSeason[] seasons)
    {
        var show = new Mock<ITmdbShow>();
        show.Setup(s => s.ID).Returns(id);
        show.Setup(s => s.Title).Returns($"TMDB show {id}");
        show.Setup(s => s.TvdbShowID).Returns(tvdbShowId);
        show.Setup(s => s.OriginalLanguageCode).Returns("ja");
        show.Setup(s => s.Seasons).Returns(seasons);
        return show.Object;
    }

    private static IShokoSeries ShokoSeries(int id, params ITmdbShow[] shows)
    {
        var series = new Mock<IShokoSeries>();
        series.Setup(s => s.ID).Returns(id);
        series.Setup(s => s.Title).Returns($"Shoko series {id}");
        series.Setup(s => s.TmdbShows).Returns(shows);
        return series.Object;
    }

    /// <summary>
    /// An <see cref="IAiringScheduleService"/> that records what the provider
    /// pushed into it, in the order it arrived.
    /// </summary>
    private sealed class RecordingScheduleService
    {
        private readonly Dictionary<IAiringSchedule, AiringScheduleData> _byHandle = [];

        public IAiringScheduleService Object { get; }

        public List<(string Name, AiringChannelType Type)> RegisteredChannels { get; } = [];

        public Dictionary<string, Guid> ChannelIDs { get; } = [];

        public List<AiringScheduleData> Schedules { get; } = [];

        public List<(AiringScheduleData Schedule, IReadOnlyList<EpisodeAiringData> Airings)> WrittenAirings { get; } = [];

        public RecordingScheduleService()
        {
            var mock = new Mock<IAiringScheduleService>();
            mock.Setup(service => service.FindOrRegisterChannel(It.IsAny<string>(), It.IsAny<AiringChannelType>()))
                .Returns((string name, AiringChannelType type) =>
                {
                    RegisteredChannels.Add((name, type));
                    if (!ChannelIDs.TryGetValue(name, out var channelId))
                        ChannelIDs[name] = channelId = Guid.NewGuid();

                    var channel = new Mock<IAiringChannel>();
                    channel.Setup(c => c.ID).Returns(channelId);
                    channel.Setup(c => c.Name).Returns(name);
                    channel.Setup(c => c.Type).Returns(type);
                    return channel.Object;
                });
            mock.Setup(service => service.AddOrUpdateSchedule(It.IsAny<IAiringScheduleProvider>(), It.IsAny<AiringScheduleData>()))
                .Returns((IAiringScheduleProvider _, AiringScheduleData data) =>
                {
                    var schedule = new Mock<IAiringSchedule>().Object;
                    Schedules.Add(data);
                    _byHandle[schedule] = data;
                    return schedule;
                });
            mock.Setup(service => service.SetAirings(
                    It.IsAny<IAiringScheduleProvider>(),
                    It.IsAny<IAiringSchedule>(),
                    It.IsAny<IEnumerable<EpisodeAiringData>>(),
                    It.IsAny<EpisodeAiringUpdateOptions?>()
                ))
                .Returns((IAiringScheduleProvider _, IAiringSchedule schedule, IEnumerable<EpisodeAiringData> airings, EpisodeAiringUpdateOptions? _) =>
                {
                    WrittenAirings.Add((_byHandle[schedule], airings.ToList()));
                    return [];
                });
            Object = mock.Object;
        }
    }

    #endregion
}
