using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Config.Services;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Airing;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Tmdb;

namespace Shoko.Plugin.TvMaze.Tests;

/// <summary>
/// The host services the provider is built on top of, stood up so a test can
/// drive it without a running Shoko.
/// </summary>
internal static class TvMazeHost
{
    /// <summary>
    /// A configuration provider handing back one configuration instance.
    /// </summary>
    /// <param name="configuration">The configuration to hand back. Defaults to a fresh one.</param>
    /// <returns>The provider.</returns>
    public static ConfigurationProvider<TvMazeConfiguration> ConfigurationProvider(TvMazeConfiguration? configuration = null)
    {
        var service = new Mock<IConfigurationService>();
        // The provider routes Load() through the configuration's info, and the
        // info is only compared for identity on a Saved event nothing here
        // raises, so it need not be a real one.
        service.Setup(s => s.GetConfigurationInfo<TvMazeConfiguration>()).Returns((ConfigurationInfo)null!);
        service.Setup(s => s.Load(It.IsAny<ConfigurationInfo>(), It.IsAny<bool>())).Returns(configuration ?? new TvMazeConfiguration());
        return new ConfigurationProvider<TvMazeConfiguration>(service.Object);
    }

    /// <summary>
    /// A metadata service whose TMDB provider holds the given series.
    /// </summary>
    /// <param name="series">The series a sweep walks.</param>
    /// <returns>The metadata service.</returns>
    public static IMetadataService MetadataService(params ISeries[] series)
    {
        var service = new Mock<IMetadataService>();
        service.Setup(s => s.GetAllSeriesForProvider(IMetadataService.ProviderName.TMDB)).Returns(series);
        return service.Object;
    }

    /// <summary>
    /// A TMDB show with the handful of members a sweep and a refresh read.
    /// </summary>
    /// <param name="id">The TMDB show ID.</param>
    /// <param name="tvdbShowId">The TheTVDB show ID TVmaze is keyed through.</param>
    /// <param name="endDate">Optional. The show's known end date.</param>
    /// <param name="seasons">The show's TMDB seasons.</param>
    /// <returns>The show.</returns>
    public static ITmdbShow TmdbShow(int id, int? tvdbShowId, DateOnly? endDate = null, params ITmdbSeason[] seasons)
    {
        var show = new Mock<ITmdbShow>();
        show.Setup(s => s.ID).Returns(id);
        show.Setup(s => s.Title).Returns($"TMDB show {id}");
        show.Setup(s => s.TvdbShowID).Returns(tvdbShowId);
        show.Setup(s => s.EndDate).Returns(endDate is { } date ? new PartialDateOnly(date) : null);
        show.Setup(s => s.OriginalLanguageCode).Returns("ja");
        show.Setup(s => s.Seasons).Returns(seasons);
        return show.Object;
    }
}

/// <summary>
/// An <see cref="ILogger{TCategoryName}"/> that records the level of each
/// entry, so a test can assert how noisy a code path is.
/// </summary>
/// <typeparam name="T">The category the logger is for.</typeparam>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<LogLevel> Entries { get; } = [];

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add(logLevel);
}

/// <summary>
/// An <see cref="IAiringScheduleService"/> that records what the provider
/// pushed into it, in the order it arrived.
/// </summary>
internal sealed class RecordingScheduleService
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
