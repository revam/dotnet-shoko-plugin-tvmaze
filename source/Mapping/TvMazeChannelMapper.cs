using System.Collections.Generic;
using Shoko.Abstractions.Metadata.Airing;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Plugin.TvMaze.Client.Models;

namespace Shoko.Plugin.TvMaze.Mapping;

/// <summary>
/// Turns a TVmaze show's <c>network</c> and <c>webChannel</c> fields into the
/// channels <c>IAiringScheduleService.FindOrRegisterChannel</c> expects: a
/// Television channel for the network, a Streaming channel for the web
/// channel, named regionally through
/// <see cref="IAiringScheduleService.GetRegionalChannelName"/> whenever
/// TVmaze names a country for it. A show can carry either, both (a show that
/// simulcasts on a streaming service alongside its broadcast run) or
/// neither.
/// </summary>
/// <remarks>
/// This only reflects the show's <em>current</em> network and web channel,
/// which is all the <c>/shows/{id}</c> shape exposes. A show that moved
/// networks between seasons is not modeled precisely; see the plugin README.
/// </remarks>
public static class TvMazeChannelMapper
{
    /// <summary>
    /// Resolves the channels a TVmaze show airs on.
    /// </summary>
    /// <param name="show">The TVmaze show.</param>
    /// <returns>
    /// Zero, one or two descriptors: at most one Television (from
    /// <see cref="TvMazeShow.Network"/>) and at most one Streaming (from
    /// <see cref="TvMazeShow.WebChannel"/>).
    /// </returns>
    public static IReadOnlyList<TvMazeChannelDescriptor> Resolve(TvMazeShow show)
    {
        var descriptors = new List<TvMazeChannelDescriptor>(2);

        if (TryDescribe(show.Network, AiringChannelType.Television, out var network))
            descriptors.Add(network);

        if (TryDescribe(show.WebChannel, AiringChannelType.Streaming, out var webChannel))
            descriptors.Add(webChannel);

        return descriptors;
    }

    private static bool TryDescribe(TvMazeNetwork? network, AiringChannelType type, out TvMazeChannelDescriptor descriptor)
    {
        if (network is null || string.IsNullOrWhiteSpace(network.Name))
        {
            descriptor = default!;
            return false;
        }

        var countryCode = string.IsNullOrWhiteSpace(network.Country?.Code) ? null : network.Country.Code;
        var name = countryCode is null
            ? network.Name
            : IAiringScheduleService.GetRegionalChannelName(network.Name, countryCode);
        var timeZoneId = string.IsNullOrWhiteSpace(network.Country?.Timezone) ? null : network.Country.Timezone;

        descriptor = new TvMazeChannelDescriptor(name, type, countryCode, timeZoneId);
        return true;
    }
}
