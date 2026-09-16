using Shoko.Abstractions.Metadata.Airing;

namespace Shoko.Plugin.TvMaze.Mapping;

/// <summary>
/// What <see cref="TvMazeChannelMapper"/> resolved a TVmaze network or web
/// channel to, before it is registered through
/// <c>IAiringScheduleService.FindOrRegisterChannel</c>.
/// </summary>
/// <param name="Name">
/// The channel's display name, already carrying its region (see
/// <c>IAiringScheduleService.GetRegionalChannelName</c>) when TVmaze gave a
/// country.
/// </param>
/// <param name="Type">Television for a network, Streaming for a web channel.</param>
/// <param name="CountryCode">
/// The ISO 3166-1 alpha-2 country code TVmaze gave for the network or web
/// channel, or <c>null</c> when it named none.
/// </param>
/// <param name="TimeZoneId">
/// The IANA time zone id TVmaze gave for the country, or <c>null</c>.
/// </param>
public sealed record TvMazeChannelDescriptor(string Name, AiringChannelType Type, string? CountryCode, string? TimeZoneId);
