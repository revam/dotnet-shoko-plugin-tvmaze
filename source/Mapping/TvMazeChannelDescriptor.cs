using Shoko.Abstractions.Metadata.Airing;

namespace Shoko.Plugin.TvMaze.Mapping;

/// <summary>
/// What <see cref="TvMazeChannelMapper"/> resolved a TVmaze network or web
/// channel to, before it is registered through
/// <c>IAiringScheduleService.FindOrRegisterChannel</c>.
/// </summary>
/// <param name="Name">The channel's display name, as TVmaze gives it.</param>
/// <param name="Type">Television for a network, Streaming for a web channel.</param>
/// <param name="CountryCode">
/// The ISO 3166-1 alpha-2 country code TVmaze gave for the network or web
/// channel, or <c>null</c> when it named none. The schedule's track carries it.
/// </param>
/// <param name="ChannelCountryCode">
/// The country the channel is registered under: TVmaze's country, except for
/// a global streaming service, which has none.
/// </param>
/// <param name="TimeZoneId">
/// The IANA time zone id TVmaze gave for the country, or <c>null</c>.
/// </param>
public sealed record TvMazeChannelDescriptor(string Name, AiringChannelType Type, string? CountryCode, string? ChannelCountryCode, string? TimeZoneId);
